# Task 6: Chat QA Engine — In-Memory BM25 Retrieval + Grounded Short-Lived SSE Stream

## Goal

Implement `CloudKB.ApiService.Chat`, the core read-pipeline engine. This service receives a user question, performs O(1) Redis-cached index lookup, runs in-memory BM25 scoring, enforces weak-retrieval early exit, fetches fact text from PostgreSQL, and streams a grounded LLM answer token-by-token via short-lived SSE.

---

## SDD Spec References

Read these spec files **before writing any code**:

| Spec File | What to Extract |
| :-------- | :-------------- |
| [openapi.yaml](../../openapi.yaml) | `POST /api/chat` endpoint: request schema (`ChatRequest`), response schema (`ChatStreamChunk`), SSE content type, `X-User-Id` header |
| [sse-protocol.md](../../sse-protocol.md) | Section 3: Chat Channel wire format — source frame first, token frames, terminal frame, connection close |
| [tokenizer-spec.md](../../tokenizer-spec.md) | Query tokenisation uses the same pipeline as index-time tokenisation |
| [appsettings.schema.json](../../appsettings.schema.json) | `BM25` section: `K1`, `B`, `HeadingBoost`, `RetrievalScoreThreshold`, `TopK`; `OpenAI` section: `ModelName`, `Temperature` |
| [db-schema.yaml](../../db-schema.yaml) | `TenantSection` entity: primary key lookup for `content` field |
| [asyncapi.yaml](../../asyncapi.yaml) | `TenantKbIndex` schema: the Redis-cached lightweight index structure |
| [feature-6-chat-qa-engine.feature](../../features/feature-6-chat-qa-engine.feature) | All BDD scenarios to satisfy |

---

## Implementation Steps

### Step 1: Create the Minimal API Project

Create `CloudKB.ApiService.Chat` as an ASP.NET Core Minimal API project.

**Project references:**
- `CloudKB.ServiceDefaults`
- `CloudKB.SharedKernel` (DTOs, Tokeniser, BM25Engine)
- `CloudKB.Infrastructure` (DbContext for PostgreSQL queries)

**NuGet dependencies:**
- `Microsoft.Extensions.AI` (LLM abstraction)
- `Microsoft.Extensions.AI.OpenAI` (OpenAI provider)
- `StackExchange.Redis`

### Step 2: Implement the `POST /api/chat` Endpoint

Register a single Minimal API endpoint:

```csharp
app.MapPost("/api/chat", async (
    HttpContext httpContext,
    ChatRequest request,
    IChatService chatService) =>
{
    var tenantId = httpContext.Request.Headers["X-User-Id"].ToString();
    
    if (string.IsNullOrWhiteSpace(tenantId))
        return Results.Problem("Missing X-User-Id header", statusCode: 401);
    
    if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Length > 2000)
        return Results.Problem("Invalid query", statusCode: 400);
    
    httpContext.Response.ContentType = "text/event-stream; charset=utf-8";
    httpContext.Response.Headers.CacheControl = "no-cache";
    httpContext.Response.Headers.Connection = "keep-alive";
    
    await chatService.StreamAnswerAsync(tenantId, request.Query, httpContext.Response, httpContext.RequestAborted);
    return Results.Empty;
});
```

### Step 3: Implement `ChatService` — The Core Engine

Create `IChatService` / `ChatService` with the following flow:

```
StreamAnswerAsync(tenantId, query, response, cancellationToken)
│
├── 1. Load TenantKbIndex from Redis (key: kb:index:{tenantId})
│     └── If not found → write SSE refusal "knowledge base has not been indexed" → return
│
├── 2. Tokenise the query using Tokeniser.Tokenise(query)
│
├── 3. Score all sections using Bm25Engine.Score(query, index)
│
├── 4. Check early exit: if topScore < RetrievalScoreThreshold (0.5)
│     └── Write SSE refusal "我無法從現有的知識庫中確認此訊息。" → close connection → return
│     └── ⚠️ DO NOT call PostgreSQL or OpenAI in this path
│
├── 5. Fetch top-K section content from PostgreSQL by primary key
│
├── 6. Write SSE source citation frame (sources only, no text)
│
├── 7. Build grounded system prompt:
│     └── "Answer ONLY based on the following sources. Cite using filename#heading."
│     └── Inject section content as context
│
├── 8. Call LLM via Microsoft.Extensions.AI (streaming mode)
│     └── For each token: write SSE data frame { text, isFinal: false }
│
├── 9. Write terminal SSE frame { text: "", isFinal: true, sources: [...] }
│
└── 10. Close HTTP response stream (short-lived lifecycle)
```

### Step 4: Implement Redis Index Loader

Create a service that loads `TenantKbIndex` from Redis:

```csharp
public class RedisIndexLoader
{
    public async Task<TenantKbIndex?> LoadAsync(string tenantId);
}
```

- Redis key: `kb:index:{tenantId}`
- Deserialise from JSON into the `TenantKbIndex` record (schema defined in `asyncapi.yaml`)
- Return `null` if the key does not exist

### Step 5: Implement SSE Writer

Create an `SseWriter` helper that formats output exactly per `sse-protocol.md` Section 3:

```csharp
public static class SseWriter
{
    // Write: data: {json}\n\n
    public static async Task WriteDataAsync(HttpResponse response, object payload);
    
    // Flush the response stream
    public static async Task FlushAsync(HttpResponse response);
}
```

### Step 6: Configure OpenAI via Microsoft.Extensions.AI

In `Program.cs`:
```csharp
builder.Services.AddChatClient(sp =>
    new OpenAIClient(builder.Configuration["OpenAI:ApiKey"])
        .AsChatClient(builder.Configuration["OpenAI:ModelName"] ?? "gpt-4o-mini"));
```

### Step 7: Implement the Grounded System Prompt

The system prompt MUST constrain the LLM:

```text
You are a knowledge base assistant. Answer the user's question ONLY using the source sections provided below. 
If the sources do not contain the answer, say you cannot confirm.
Cite your sources using the format: [filename#heading].

--- SOURCES ---
{foreach section in topKSections}
### {section.FileName}#{section.Heading}
{section.Content}
{/foreach}
--- END SOURCES ---
```

---

## Verification

### Automated (BDD Scenarios from `feature-6-chat-qa-engine.feature`)

- [ ] **Successful retrieval:** Query "How long do refunds take?" returns SSE stream citing `refund_policy.md#refund-timeline`
- [ ] **Email question:** Query "Can I change my email address?" cites `account_help.md#change-email`
- [ ] **Weak retrieval early exit:** Query "What is the weather today?" returns refusal without calling PostgreSQL or OpenAI
- [ ] **Nonsense query:** Query "asdfjkl;qwerty12345" returns BM25 score 0.0 and refusal
- [ ] **No index exists:** Query from `tenant-99` (no Redis key) returns "knowledge base has not been indexed"
- [ ] **Multi-tenant isolation:** `tenant-02` cannot retrieve `tenant-01`'s data
- [ ] **SSE lifecycle:** Connection closes after final chunk with `isFinal: true`
- [ ] **Source-first ordering:** Sources are sent before answer tokens in the SSE stream

### Manual Smoke Test

```bash
# Grounded question (requires seeded data from Task 0)
curl -N -X POST http://localhost:5300/api/chat \
  -H "Content-Type: application/json" \
  -H "X-User-Id: tenant-01" \
  -d '{"query": "How long do refunds take?"}'

# Out-of-scope question (early exit, no LLM call)
curl -N -X POST http://localhost:5300/api/chat \
  -H "Content-Type: application/json" \
  -H "X-User-Id: tenant-01" \
  -d '{"query": "Which restaurants are nearby?"}'

# No index (tenant not seeded)
curl -N -X POST http://localhost:5300/api/chat \
  -H "Content-Type: application/json" \
  -H "X-User-Id: tenant-99" \
  -d '{"query": "Hello"}'
```

---

## Output Artifacts

| Artifact | Description |
| :------- | :---------- |
| `CloudKB.ApiService.Chat/Program.cs` | Minimal API with `POST /api/chat` endpoint |
| `CloudKB.ApiService.Chat/Services/ChatService.cs` | Core engine: Redis load → BM25 → early exit → PG fetch → LLM stream |
| `CloudKB.ApiService.Chat/Services/RedisIndexLoader.cs` | Redis `TenantKbIndex` deserialiser |
| `CloudKB.ApiService.Chat/Services/SseWriter.cs` | SSE line-format helper |
| `CloudKB.ApiService.Chat/appsettings.json` | BM25 and OpenAI configuration |
