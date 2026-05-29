# Task 3: Knowledge Compilation Worker — RabbitMQ Consumer, Markdown Parsing, BM25 Stats & Redis Index Cache

## Goal

Implement `CloudKB.Worker.Indexer`, a .NET `BackgroundService` that consumes `CompileKnowledgeTask` messages from RabbitMQ, acquires a per-tenant Redis distributed lock, parses Markdown files from S3 into `TenantSection` entities, computes BM25 statistics, bulk-inserts into PostgreSQL, caches the lightweight `TenantKbIndex` into Redis, and publishes progress/completion events to Redis Pub/Sub.

---

## SDD Spec References

| Spec File | What to Extract |
| :-------- | :-------------- |
| [asyncapi.yaml](../../asyncapi.yaml) | `CompileKnowledgeTask` message schema (consume), `IndexProgressEvent` / `IndexCompletedEvent` / `IndexFailedEvent` (publish), `TenantKbIndex` cache schema, distributed lock key `lock:index:{tenantId}`, DLQ `cloudkb.indexing.compile.dlq` |
| [db-schema.yaml](../../db-schema.yaml) | `TenantSection` entity (bulk insert), `IndexCompilationJob` entity (status transitions: Pending → Processing → Completed/Failed), `TenantFile` entity (mark `is_indexed = true`) |
| [tokenizer-spec.md](../../tokenizer-spec.md) | The exact 4-stage normalisation pipeline used to tokenise section content |
| [appsettings.schema.json](../../appsettings.schema.json) | `Storage.BucketName` for S3 reads |
| [feature-3-knowledge-compilation.feature](../../features/feature-3-knowledge-compilation.feature) | All BDD scenarios: lock acquisition, Markdown parsing, BM25 stats, bulk ops, DLQ |

---

## Prerequisites

- **Task 0** (Foundation) completed — SharedKernel (Tokeniser, BM25Engine), Infrastructure (DbContext), Aspire AppHost
- **Task 2** (Async Ingest) completed — messages are landing in `cloudkb.indexing.compile` queue

---

## Implementation Steps

### Step 1: Create the Worker Service Project

Create `CloudKB.Worker.Indexer` as a .NET Worker Service project.

**Project references:**
- `CloudKB.ServiceDefaults`
- `CloudKB.SharedKernel` (Tokeniser, BM25 stats computation)
- `CloudKB.Infrastructure` (DbContext, entities)

**NuGet dependencies:**
- `RabbitMQ.Client` or `MassTransit.RabbitMQ`
- `StackExchange.Redis`
- `AWSSDK.S3` or `Minio`

### Step 2: Implement the RabbitMQ Consumer (`IndexCompilationConsumer`)

Create a `BackgroundService` that polls the durable queue:

```csharp
public class IndexCompilationConsumer : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. Connect to RabbitMQ
        // 2. Declare queue: cloudkb.indexing.compile (durable, with dead-letter exchange)
        // 3. Set prefetch = 1 (process one task at a time per instance)
        // 4. Register async consumer
        // 5. On message received → call CompilationPipeline.ExecuteAsync(...)
        // 6. ACK on success, NACK+requeue on transient failure
        // 7. After 3 retries → message routed to DLQ
    }
}
```

**Dead-Letter Queue setup:**
```csharp
// Queue arguments
var args = new Dictionary<string, object>
{
    { "x-dead-letter-exchange", "" },
    { "x-dead-letter-routing-key", "cloudkb.indexing.compile.dlq" }
};
channel.QueueDeclare("cloudkb.indexing.compile", durable: true, arguments: args);
channel.QueueDeclare("cloudkb.indexing.compile.dlq", durable: true);
```

### Step 3: Implement the Compilation Pipeline

The core orchestrator follows this sequence:

```
CompilationPipeline.ExecuteAsync(CompileKnowledgeTaskPayload task, CancellationToken ct)
│
├── 1. Update IndexCompilationJob: status = "Processing", startedAt = now
│
├── 2. Acquire distributed lock
│     └── Redis SET NX EX: key = "lock:index:{tenantId}", TTL = 300s
│     └── If lock not acquired → wait with exponential backoff (max 3 attempts)
│     └── If still locked → throw LockTimeoutException
│
├── 3. Publish IndexProgressEvent to Redis Pub/Sub
│     └── Channel: ch:notifications:{tenantId}
│     └── Payload: { eventType: "IndexProcessing", taskId, message: "正在啟動增量檢查與知識編譯..." }
│
├── 4. For each fileName in task.fileNames:
│     │
│     ├── 4a. Get content_hash from TenantFile in PostgreSQL (uploaded in Task 2)
│     ├── 4b. Fetch TenantFileState record from database where tenant_id = tenantId and file_name = fileName
│     │
│     ├── 4c. [🔍 Stage 1: Fast-Pass Skip Check]
│     │     └── If TenantFileState exists AND TenantFileState.ContentHash == new content_hash:
│     │         └── Log: "File {fileName} hash matches. Skipping incremental split."
│     │         └── Continue to next file (no S3 download, no parsing, no DB writes)
│     │
│     ├── 4d. If hash differs or no previous state exists:
│     │     └── Download file from S3: /{tenantId}/raw/{fileName}
│     │
│     ├── 4e. Parse Markdown into sections (MarkdownParser) -> newParsedSections
│     │
│     ├── 4f. [✂️ Stage 2: Section Diffing]
│     │     └── Query existing TenantSections from PostgreSQL for this tenant and file -> oldDbSections
│     │     └── Compare newParsedSections against oldDbSections using SectionId:
│     │         ├── **ADDED**: Exist in newParsedSections but not in oldDbSections
│     │         │     └── Action: Add to DB insert queue
│     │         │     └── Audit: Add "ADDED" IndexAuditLog entry (sectionsAffected = 1, message = "Added Heading: # {heading}")
│     │         ├── **MODIFIED**: Exist in both, but Content or Tokens differ
│     │         │     └── Action: Add to DB update queue
│     │         │     └── Audit: Add "MODIFIED" IndexAuditLog entry (sectionsAffected = 1, message = "Modified Heading: # {heading}")
│     │         └── **DELETED**: Exist in oldDbSections but not in newParsedSections
│     │               └── Action: Add to DB delete queue
│     │               └── Audit: Add "DELETED" IndexAuditLog entry (sectionsAffected = 1, message = "Deleted Heading: # {heading}")
│     │
│     ├── 4g. Apply batch actions to PostgreSQL (using AddRangeAsync, UpdateRange, and RemoveRange)
│     │
│     ├── 4h. Upsert TenantFileState record: set content_hash = new content_hash, last_indexed_at = now
│     │
│     └── 4i. Update TenantFile record: set is_indexed = true, last_indexed_at = now
│
├── 5. [🔄 Stage 3: BM25 Re-aggregation & Cache Update]
│     ├── If any file was compiled (hash changed):
│     │     └── Query all current TenantSections for this tenantId from database
│     │     └── Recompute BM25 statistics (totalDocuments, averageDocumentLength)
│     │     └── Rebuild TenantKbIndex (without raw content)
│     │     └── Cache TenantKbIndex to Redis (Key: kb:index:{tenantId})
│     └── Else (if all files skipped):
│           └── Skip BM25 calculations and Redis updates (existing cache is valid!)
│
├── 6. Release distributed lock
│     └── Redis DEL: lock:index:{tenantId}
│
├── 7. Update IndexCompilationJob: status = "Completed", completedAt = now,
│       sectionsCompiled = N, filesProcessed = M, compileDurationMs = elapsed
│
└── 8. Publish IndexCompletedEvent to Redis Pub/Sub
        └── Channel: ch:notifications:{tenantId}
        └── Payload: { eventType: "IndexCompleted", taskId, message: "您的知識庫已編譯完成！",
                       metadata: { sectionsCompiled, filesProcessed } }
```

**Error handling:**
```
On any exception:
├── Release distributed lock (always, even on failure)
├── Update IndexCompilationJob: status = "Failed", errorCode, errorDetail
└── Publish IndexFailedEvent to Redis Pub/Sub
```

### Step 4: Implement the Markdown Parser (`MarkdownParser`)

```csharp
public static class MarkdownParser
{
    /// <summary>
    /// Splits a Markdown file into sections by heading.
    /// Each section contains the heading, heading breadcrumb path, and body content.
    /// </summary>
    public static IReadOnlyList<ParsedSection> Parse(string tenantId, string fileName, string markdownContent);
}

public record ParsedSection(
    string SectionId,           // {tenantId}#{fileName}#{slugified-heading}
    string TenantId,
    string FileName,
    string Heading,
    List<string> HeadingPath,   // Breadcrumb: ["Parent H1", "Child H2", "Current H3"]
    string Content,             // Raw Markdown body under this heading
    IReadOnlyList<string> Tokens,  // Tokenised content
    int TokenCount
);
```

**Parsing algorithm:**
1. Read line by line.
2. When a heading line is encountered (`#` to `######`):
   - Determine heading level (count of `#` characters).
   - Pop the heading path stack back to the parent level.
   - Push the current heading onto the stack.
   - Start a new section with the accumulated heading path.
3. Non-heading lines are appended to the current section's content.
4. Slugify the heading for the section ID: lowercase, replace spaces with `-`, strip non-alphanumeric.

### Step 5: Implement the Distributed Lock Service

```csharp
public class RedisDistributedLock : IAsyncDisposable
{
    private readonly IDatabase _redis;
    private readonly string _lockKey;
    private readonly string _lockValue;  // Unique per acquisition (Guid)
    
    public static async Task<RedisDistributedLock> AcquireAsync(
        IDatabase redis, string tenantId, TimeSpan ttl, CancellationToken ct)
    {
        var lockKey = $"lock:index:{tenantId}";
        var lockValue = Guid.NewGuid().ToString();
        
        // Retry with exponential backoff: 1s, 2s, 4s
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (await redis.StringSetAsync(lockKey, lockValue, ttl, When.NotExists))
                return new RedisDistributedLock(redis, lockKey, lockValue);
            
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
        }
        throw new LockTimeoutException($"Cannot acquire lock: {lockKey}");
    }
    
    public async ValueTask DisposeAsync()
    {
        // Only release if we still own the lock (compare lockValue)
        var script = "if redis.call('get',KEYS[1]) == ARGV[1] then return redis.call('del',KEYS[1]) else return 0 end";
        await _redis.ScriptEvaluateAsync(script, new RedisKey[] { _lockKey }, new RedisValue[] { _lockValue });
    }
}
```

### Step 6: Implement Redis Pub/Sub Publisher

```csharp
public class RedisEventPublisher
{
    private readonly ISubscriber _subscriber;
    
    public async Task PublishProgressAsync(string tenantId, string taskId, string message)
    {
        var channel = $"ch:notifications:{tenantId}";
        var payload = JsonSerializer.Serialize(new IndexProgressPayload
        {
            EventType = "IndexProcessing",
            TaskId = taskId,
            Message = message
        });
        await _subscriber.PublishAsync(RedisChannel.Literal(channel), payload);
    }
    
    public async Task PublishCompletedAsync(string tenantId, string taskId, int sections, int files) { /* ... */ }
    public async Task PublishFailedAsync(string tenantId, string taskId, string errorCode, string detail) { /* ... */ }
}
```

> **Schema compliance:** All published JSON payloads MUST match the corresponding schemas in `asyncapi.yaml` (`IndexProgressPayload`, `IndexCompletedPayload`, `IndexFailedPayload`).

### Step 7: Build the TenantKbIndex Cache Object

After bulk insert, construct the lightweight index that the Chat Service (Task 6) will consume:

```csharp
public TenantKbIndex BuildIndex(string tenantId, List<TenantSection> allSections)
{
    var avgdl = allSections.Average(s => s.TokenCount);
    
    return new TenantKbIndex
    {
        TenantId = tenantId,
        TotalDocuments = allSections.Count,
        AverageDocumentLength = avgdl,
        LastUpdatedAt = DateTime.UtcNow,
        Sections = allSections.Select(s => new IndexedSectionMeta
        {
            SectionId = s.Id,
            FileName = s.FileName,
            Heading = s.Heading,
            HeadingPath = s.HeadingPath,
            TokenCount = s.TokenCount,
            TermFrequencies = s.Tokens
                .GroupBy(t => t)
                .ToDictionary(g => g.Key, g => g.Count())
        }).ToList()
    };
    // ⚠️ Do NOT include raw section Content — this is a lightweight map only
}
```

---

## Verification

### BDD Scenarios from `feature-3-knowledge-compilation.feature`

- [ ] **Lock acquired:** Redis key `lock:index:tenant-01` exists during compilation
- [ ] **Concurrent lock serialisation:** Second task for same tenant waits for lock release
- [ ] **Lock released on success:** Redis key deleted after completion
- [ ] **Lock released on failure:** Redis key deleted even on error
- [ ] **Markdown splitting:** `refund_policy.md` with 3 headings → 3 `TenantSection` rows
- [ ] **Heading path breadcrumb:** Nested headings produce correct `headingPath` arrays
- [ ] **BM25 stats:** `TenantKbIndex` in Redis contains correct `totalDocuments` and `averageDocumentLength`
- [ ] **Index excludes content:** Cached index does NOT contain raw Markdown content
- [ ] **Re-indexing:** Old sections for re-uploaded file are replaced
- [ ] **Bulk insert:** 50 headings → 50 rows written efficiently
- [ ] **DLQ routing:** 3 consecutive failures → message in `cloudkb.indexing.compile.dlq`

### End-to-End Smoke Test

```bash
# 1. Upload files via Task 2 endpoint (or directly push a message to RabbitMQ)
TOKEN=$(dotnet user-jwts create --claim user_id=tenant-01)
curl -X POST http://localhost:5000/api/index \
  -H "Authorization: Bearer $TOKEN" \
  -F "files=@docs/TestingMarkdown/refund_policy.md" \
  -F "files=@docs/TestingMarkdown/account_help.md"

# 2. Wait for worker to process (check worker logs)

# 3. Verify PostgreSQL
# SELECT count(*) FROM tenant_sections WHERE tenant_id = 'tenant-01';

# 4. Verify Redis cached index
# redis-cli GET kb:index:tenant-01 | python -m json.tool

# 5. Verify IndexCompilationJob status
# SELECT status, sections_compiled FROM index_compilation_jobs WHERE tenant_id = 'tenant-01';

# 6. Test chat (should now use the worker-compiled index instead of seeded data)
curl -N -X POST http://localhost:5300/api/chat \
  -H "Content-Type: application/json" \
  -H "X-User-Id: tenant-01" \
  -d '{"query": "How long do refunds take?"}'
```

---

## Output Artifacts

| Artifact | Description |
| :------- | :---------- |
| `CloudKB.Worker.Indexer/Program.cs` | Worker service registration and DI |
| `CloudKB.Worker.Indexer/Consumers/IndexCompilationConsumer.cs` | RabbitMQ BackgroundService consumer |
| `CloudKB.Worker.Indexer/Services/CompilationPipeline.cs` | Core orchestrator: lock → parse → insert → cache → notify |
| `CloudKB.Worker.Indexer/Services/MarkdownParser.cs` | Heading-based Markdown splitter |
| `CloudKB.Worker.Indexer/Services/RedisDistributedLock.cs` | SET NX EX lock with Lua release script |
| `CloudKB.Worker.Indexer/Services/RedisEventPublisher.cs` | Pub/Sub event publisher |
| `CloudKB.Worker.Indexer/Services/IndexBuilder.cs` | TenantKbIndex construction |
