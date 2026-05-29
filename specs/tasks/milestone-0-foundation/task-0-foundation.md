# Task 0: Foundation — .NET Aspire AppHost, DB Schema, Shared Tokeniser & ServiceDefaults

## Goal

Bootstrap the entire .NET Aspire 10 distributed application skeleton, including the AppHost orchestrator, shared service defaults, the PostgreSQL database schema (via EF Core 10 migrations), and the shared tokenisation library. This task produces **zero user-facing endpoints** but provides the foundation that all Milestone 1 and 2 tasks depend on.

---

## SDD Spec References

Read these spec files **before writing any code**:

| Spec File | What to Extract |
| :-------- | :-------------- |
| [aspire-topology.yaml](../../aspire-topology.yaml) | Container names, service resource names, ports, connection string keys, and the reference `Program.cs` |
| [db-schema.yaml](../../db-schema.yaml) | Entity definitions (`TenantSection`, `IndexCompilationJob`, `TenantFile`), column types, indexes, EF Core Fluent API reference |
| [appsettings.schema.json](../../appsettings.schema.json) | Configuration keys and defaults for `BM25`, `OpenAI`, `Storage`, and `ConnectionStrings` |
| [tokenizer-spec.md](../../tokenizer-spec.md) | The 4-stage normalisation pipeline and the official 50-word stopword list |
| [openapi.yaml](../../openapi.yaml) | Shared DTO schemas: `SourceCitation`, `ChatStreamChunk`, `NotificationEvent`, `ProblemDetails` |

---

## Implementation Steps

### Step 1: Create the Solution Structure

Create the following .NET solution and project structure:

```
CloudKB/
├── CloudKB.sln
├── src/
│   ├── CloudKB.AppHost/                    # .NET Aspire AppHost (orchestrator)
│   ├── CloudKB.ServiceDefaults/            # Shared OpenTelemetry, Health Checks, Resiliency
│   ├── CloudKB.SharedKernel/               # Shared DTOs, Tokeniser, BM25 Engine
│   ├── CloudKB.Infrastructure/             # EF Core DbContext, Migrations, Redis helpers
│   ├── CloudKB.Gateway/                    # Yarp Reverse Proxy (Task 5)
│   ├── CloudKB.ApiService.Chat/            # Chat QA Service (Task 6)
│   ├── CloudKB.ApiService.Indexing/        # Indexing API (Task 2)
│   ├── CloudKB.ApiService.Notification/    # Notification SSE (Task 4)
│   └── CloudKB.Worker.Indexer/             # Background Worker (Task 3)
└── tests/
    └── CloudKB.Tests.BDD/                  # SpecFlow + xUnit BDD tests
```

**Commands:**
```bash
dotnet new aspire-starter -n CloudKB -o CloudKB
```

> **Critical:** Use the resource names defined in `aspire-topology.yaml` exactly. Do not invent different names.

### Step 2: Configure the AppHost (`CloudKB.AppHost/Program.cs`)

Wire up the infrastructure containers and project references. Follow the reference code in `aspire-topology.yaml` Section 3.

**Key resources to register:**
- `postgres` → PostgreSQL container with database `cloudkb`
- `redis` → Redis container
- `rabbitmq` → RabbitMQ container (needed for Milestone 2, register now)
- `minio` → MinIO container (needed for Milestone 2, register now)

**Key services to register:**
- `gateway` → `CloudKB.Gateway` with references to downstream API services
- `apiservice-chat` → `CloudKB.ApiService.Chat` with references to `postgres` + `redis`
- `apiservice-indexing` → `CloudKB.ApiService.Indexing` (skeleton only)
- `apiservice-notification` → `CloudKB.ApiService.Notification` (skeleton only)
- `worker-indexer` → `CloudKB.Worker.Indexer` (skeleton only)

### Step 3: Create ServiceDefaults (`CloudKB.ServiceDefaults`)

Configure the standard .NET Aspire service defaults:
- OpenTelemetry (Metrics, Tracing, Logging)
- Health check endpoints (`/health`, `/alive`)
- HTTP client resilience (Polly circuit breaker, retry)

### Step 4: Define Shared DTOs (`CloudKB.SharedKernel`)

Create strong-typed record classes matching the schemas in `openapi.yaml`:

```csharp
// From openapi.yaml → components/schemas
public record ChatRequest(string Query);
public record ChatStreamChunk(string Text, bool IsFinal, List<SourceCitation>? Sources);
public record SourceCitation(string SectionId, string FileName, string Heading, List<string>? HeadingPath, double? Score);
public record NotificationEvent(string EventType, string TaskId, string Message, object? Metadata);
public record IndexAcceptedResponse(Guid TaskId, string Message);
public record IndexAuditLogResponse(Guid Id, string FileName, string ActionType, int SectionsAffected, string CommitMessage, DateTime LoggedAt);
public record HealthResponse(string Status);
```

### Step 5: Implement the Shared Tokeniser (`CloudKB.SharedKernel/Tokeniser.cs`)

Implement the **exact** 4-stage pipeline defined in `tokenizer-spec.md`:

1. `ToLowerInvariant()`
2. Regex replace `[^a-z0-9\s]` → `" "`
3. `Split(' ', '\r', '\n', '\t')` with `RemoveEmptyEntries`
4. Filter against the 50-word stopword set + reject single-char tokens

Use `FrozenSet<string>` for the stopword lookup.

**Public API:**
```csharp
public static class Tokeniser
{
    public static IReadOnlyList<string> Tokenise(string text);
}
```

### Step 6: Implement BM25 Engine (`CloudKB.SharedKernel/Bm25Engine.cs`)

Create a reusable BM25 scoring engine that can be used both by the Index Worker (Milestone 2) and the Chat Service (Milestone 1).

**Configuration parameters** (from `appsettings.schema.json`):
- `K1` = 1.2
- `B` = 0.75
- `HeadingBoost` = 1.5
- `RetrievalScoreThreshold` = 0.5
- `TopK` = 3

**Public API:**
```csharp
public class Bm25Engine
{
    public Bm25Engine(Bm25Options options);
    public IReadOnlyList<ScoredSection> Score(string query, TenantKbIndex index);
}

public record Bm25Options(double K1, double B, double HeadingBoost, double RetrievalScoreThreshold, int TopK);
public record ScoredSection(string SectionId, double Score);
```

### Step 7: Create EF Core DbContext (`CloudKB.Infrastructure`)

Define entities and Fluent API configuration matching `db-schema.yaml` exactly:

- `TenantSection` entity → `tenant_sections` table
- `IndexCompilationJob` entity → `index_compilation_jobs` table
- `TenantFile` entity → `tenant_files` table
- `TenantFileState` entity → `tenant_file_states` table
- `IndexAuditLog` entity → `index_audit_logs` table

Create and apply the initial migration:
```bash
dotnet ef migrations add InitialCreate -p src/CloudKB.Infrastructure -s src/CloudKB.AppHost
dotnet ef database update -p src/CloudKB.Infrastructure -s src/CloudKB.AppHost
```

### Step 8: Create a Test Data Seeder

Since Milestone 1 (Read Pipeline) needs data to query against but the Write Pipeline is not yet built, create a `SeedDataService` in `CloudKB.Infrastructure` that:

1. Reads the 3 Markdown files from `docs/TestingMarkdown/` (`account_help.md`, `refund_policy.md`, `shipping_faq.md`)
2. Parses them using the heading-based Markdown splitter
3. Tokenises using `Tokeniser.Tokenise()`
4. Inserts `TenantSection` rows into PostgreSQL for `tenant-01`
5. Computes BM25 stats and caches a `TenantKbIndex` JSON into Redis key `kb:index:tenant-01`

This seeder runs on AppHost startup or via a CLI command.

### Step 9: Create `appsettings.json` for Each Service

Generate `appsettings.json` files that conform to `appsettings.schema.json`. Aspire will inject connection strings automatically, but the `BM25`, `OpenAI`, and `Storage` sections must be set explicitly.

### Step 10: Bootstrap the BDD Test Harness (`tests/CloudKB.Tests.BDD`)

Configure the test harness to allow integration testing of microservices without calling external endpoints or failing security gates:

1. **JWT Mock Generator**: Implement a utility in the test project that signs local developer JWTs with a symmetric key matching the gateway's validation configuration during testing, ensuring automated test clients can bypass JWT validation.
2. **LLM Client Mocking**: Register a mock or fake implementation of `IChatClient` (using `Microsoft.Extensions.AI`) in the test dependency injection container. This fake client returns static tokens or simulated token responses rather than invoking the real OpenAI API, protecting token budget during test runs.

---

## Verification

- [ ] `dotnet build CloudKB.sln` compiles without errors
- [ ] `dotnet ef migrations has-pending-model-changes` reports no pending changes
- [ ] AppHost starts all containers (postgres, redis, rabbitmq, minio) via Aspire Dashboard
- [ ] `Tokeniser.Tokenise("I want you to be happy with a refund.")` returns `["want", "happy", "refund"]`
- [ ] Seeded data: Redis key `kb:index:tenant-01` contains a valid `TenantKbIndex` JSON
- [ ] Seeded data: PostgreSQL `tenant_sections` table contains rows for `tenant-01`
- [ ] BDD Project scaffolds with the mock JWT generator and mock `IChatClient` configured

---

## Output Artifacts

| Artifact | Description |
| :------- | :---------- |
| `CloudKB.AppHost` | Fully wired Aspire orchestrator |
| `CloudKB.ServiceDefaults` | Shared telemetry and resilience |
| `CloudKB.SharedKernel` | DTOs, Tokeniser, BM25 Engine |
| `CloudKB.Infrastructure` | DbContext, Migrations, Seed Data |
| `appsettings.json` (per service) | Validated configuration |
| `tests/CloudKB.Tests.BDD` | Scaffolding for BDD tests with JWT and LLM mocking |

