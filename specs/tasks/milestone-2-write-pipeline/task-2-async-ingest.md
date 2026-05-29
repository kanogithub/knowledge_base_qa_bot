# Task 2: Async File Ingestion — S3 Stream Upload + RabbitMQ Task Dispatch + 202 Fast Return

## Goal

Implement `CloudKB.ApiService.Indexing`, a lightweight ingest API that receives Markdown file uploads, streams them to MinIO/S3 under tenant-isolated paths, pushes a `CompileKnowledgeTask` message to RabbitMQ, and returns `HTTP 202 Accepted` within 100ms.

---

## SDD Spec References

| Spec File | What to Extract |
| :-------- | :-------------- |
| [openapi.yaml](../../openapi.yaml) | `POST /api/index`: `IndexRequest` (multipart/form-data), `IndexAcceptedResponse` (taskId + message), error responses |
| [asyncapi.yaml](../../asyncapi.yaml) | `CompileKnowledgeTask` message schema: `taskId`, `tenantId`, `s3StoragePath`, `fileNames`, `requestedAt`; Queue: `cloudkb.indexing.compile` |
| [aspire-topology.yaml](../../aspire-topology.yaml) | Service name `apiservice-indexing`, MinIO connection, RabbitMQ connection, S3 bucket `knowledge-base` |
| [db-schema.yaml](../../db-schema.yaml) | `TenantFile` entity: register uploaded files; `IndexCompilationJob` entity: create audit record |
| [appsettings.schema.json](../../appsettings.schema.json) | `Storage.BucketName` configuration key |
| [feature-2-async-ingest.feature](../../features/feature-2-async-ingest.feature) | All BDD scenarios |

---

## Prerequisites

- **Task 0** (Foundation) completed
- **Task 1** (Gateway Auth) completed — requests arrive with `X-User-Id` header

---

## Implementation Steps

### Step 1: Create the Minimal API Project

Create `CloudKB.ApiService.Indexing` as an ASP.NET Core Minimal API.

**NuGet dependencies:**
- `AWSSDK.S3` or `Minio` (.NET client for MinIO)
- `RabbitMQ.Client` or `MassTransit.RabbitMQ`

### Step 2: Implement the `POST /api/index` Endpoint

```csharp
app.MapPost("/api/index", async (
    HttpContext httpContext,
    IFormFileCollection files,      // from multipart/form-data
    IIndexingService indexingService) =>
{
    var tenantId = httpContext.Request.Headers["X-User-Id"].ToString();
    
    if (files == null || files.Count == 0)
        return Results.Problem("No files provided", statusCode: 400);
    
    // Validate all files are .md
    if (files.Any(f => !f.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        return Results.Problem("Only .md files are accepted", statusCode: 400);
    
    var result = await indexingService.IngestAsync(tenantId, files, httpContext.RequestAborted);
    return Results.Accepted(value: result);  // HTTP 202
})
.DisableAntiforgery();  // Required for multipart uploads
```

### Step 3: Implement `IndexingService`

The service performs 3 operations in sequence:

```
IngestAsync(tenantId, files, cancellationToken)
│
├── 1. Generate a new TaskId (Guid.NewGuid())
│
├── 2. Stream each file to S3/MinIO
│     └── Path: /{tenantId}/raw/{fileName}
│     └── Use streaming upload (do NOT buffer entire file in memory)
│
├── 3. Upsert TenantFile records in PostgreSQL
│     └── Set content_hash = SHA256 of file bytes (this hash will be evaluated by the background worker for fast-pass incremental check)
│     └── Set is_indexed = false
│
├── 4. Create IndexCompilationJob record in PostgreSQL
│     └── status = "Pending"
│     └── file_names = list of uploaded filenames
│
├── 5. Publish CompileKnowledgeTask message to RabbitMQ
│     └── Queue: cloudkb.indexing.compile
│     └── Routing key: compile.{tenantId}
│     └── Payload matches asyncapi.yaml schema exactly
│
└── 6. Return IndexAcceptedResponse { taskId, message }
```

### Step 4: Implement S3 Upload Service

```csharp
public class S3StorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName; // from config: Storage:BucketName
    
    public S3StorageService(IAmazonS3 s3Client, IConfiguration config)
    {
        _s3Client = s3Client;
        _bucketName = config["Storage:BucketName"] ?? "knowledge-base";
        
        // Ensure bucket exists on startup (or implement checking logic synchronously/asynchronously)
        EnsureBucketExistsAsync().GetAwaiter().GetResult();
    }
    
    private async Task EnsureBucketExistsAsync()
    {
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(_s3Client, _bucketName))
        {
            await _s3Client.PutBucketAsync(new PutBucketRequest { BucketName = _bucketName });
        }
    }
    
    public async Task UploadAsync(string tenantId, string fileName, Stream fileStream, CancellationToken ct)
    {
        var key = $"{tenantId}/raw/{fileName}";
        // Stream upload to MinIO/S3 using TransferUtility or PutObjectRequest with InputStream
    }
}
```

### Step 5: Implement RabbitMQ Publisher

```csharp
public class RabbitMqPublisher
{
    public async Task PublishCompileTaskAsync(CompileKnowledgeTaskPayload task, CancellationToken ct)
    {
        // Serialise to JSON
        // Publish to exchange: cloudkb.indexing, routing key: compile.{tenantId}
        // Set message headers: messageId, timestamp, retryCount=0
    }
}
```

> **Message schema compliance:** The published JSON MUST match `CompileKnowledgeTaskPayload` in `asyncapi.yaml` exactly. Field names, types, and required fields must align.

### Step 6: Ensure Non-Blocking Response Time

The entire `IngestAsync` pipeline must complete in under **100 milliseconds**. Key strategies:
- Use S3 streaming (no in-memory buffer)
- Fire-and-forget the RabbitMQ publish (await only the basic ack)
- Do NOT wait for the background worker to start processing

---

## Verification

### BDD Scenarios from `feature-2-async-ingest.feature`

- [ ] Single file upload → HTTP 202 + taskId + file in S3 + message in RabbitMQ
- [ ] Multiple files upload → all files in S3 + single RabbitMQ message with all filenames
- [ ] HTTP 202 returned within 100ms (before worker starts)
- [ ] Empty multipart body → HTTP 400
- [ ] Non-.md file → HTTP 400
- [ ] Tenant isolation: tenant-01 and tenant-02 files are in separate S3 paths

### Smoke Test

```bash
TOKEN=$(dotnet user-jwts create --claim user_id=tenant-01)

# Single file
curl -X POST http://localhost:5000/api/index \
  -H "Authorization: Bearer $TOKEN" \
  -F "files=@docs/TestingMarkdown/refund_policy.md"
# → 202 {"taskId":"...","message":"Knowledge compilation job enqueued."}

# Multiple files
curl -X POST http://localhost:5000/api/index \
  -H "Authorization: Bearer $TOKEN" \
  -F "files=@docs/TestingMarkdown/refund_policy.md" \
  -F "files=@docs/TestingMarkdown/account_help.md" \
  -F "files=@docs/TestingMarkdown/shipping_faq.md"
# → 202

# Verify S3
# Check MinIO console at http://localhost:9001 → bucket: knowledge-base → tenant-01/raw/

# Verify RabbitMQ
# Check RabbitMQ management at http://localhost:15672 → queue: cloudkb.indexing.compile
```

---

## Output Artifacts

| Artifact | Description |
| :------- | :---------- |
| `CloudKB.ApiService.Indexing/Program.cs` | Minimal API with `POST /api/index` |
| `CloudKB.ApiService.Indexing/Services/IndexingService.cs` | Orchestrates S3 upload + RabbitMQ publish |
| `CloudKB.ApiService.Indexing/Services/S3StorageService.cs` | MinIO/S3 streaming upload |
| `CloudKB.ApiService.Indexing/Services/RabbitMqPublisher.cs` | CompileKnowledgeTask publisher |
