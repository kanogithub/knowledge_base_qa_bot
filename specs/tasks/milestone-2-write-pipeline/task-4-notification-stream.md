# Task 4: Notification SSE Stream — Redis Pub/Sub Subscriber + Long-Lived SSE Push Channel

## Goal

Implement `CloudKB.ApiService.Notification`, a long-lived SSE service that subscribes to Redis Pub/Sub channels per tenant and relays progress/completion/failure events to the frontend in real time. This is the final piece of the Write Pipeline, completing the feedback loop from background compilation back to the user's browser.

---

## SDD Spec References

| Spec File | What to Extract |
| :-------- | :-------------- |
| [openapi.yaml](../../openapi.yaml) | `GET /api/notifications/stream`: response headers (`text/event-stream`, `no-cache`, `keep-alive`), `NotificationEvent` schema |
| [asyncapi.yaml](../../asyncapi.yaml) | Redis Pub/Sub channel pattern `ch:notifications:{tenantId}`, message schemas: `IndexProgressPayload`, `IndexCompletedPayload`, `IndexFailedPayload` |
| [sse-protocol.md](../../sse-protocol.md) | Section 2: Notification Channel — event type names (`IndexProcessing`, `IndexCompleted`, `IndexFailed`), keep-alive `:ping\n\n` every 30s, wire format |
| [feature-4-notification-stream.feature](../../features/feature-4-notification-stream.feature) | All BDD scenarios: SSE establishment, heartbeat, event relay, multi-tenant isolation, reconnection |

---

## Prerequisites

- **Task 0** (Foundation) completed — Redis available via Aspire
- **Task 3** (Knowledge Compilation) completed — worker publishes events to `ch:notifications:{tenantId}`

---

## Implementation Steps

### Step 1: Create the Minimal API Project

Create `CloudKB.ApiService.Notification` as an ASP.NET Core Minimal API.

**Project references:**
- `CloudKB.ServiceDefaults`
- `CloudKB.SharedKernel` (DTOs: `NotificationEvent`, `IndexAuditLogResponse`)
- `CloudKB.Infrastructure` (DbContext to read audit logs)

**NuGet dependencies:**
- `StackExchange.Redis`

### Step 2: Implement the `GET /api/notifications/stream` Endpoint

```csharp
app.MapGet("/api/notifications/stream", async (
    HttpContext httpContext,
    INotificationStreamService streamService) =>
{
    var tenantId = httpContext.Request.Headers["X-User-Id"].ToString();
    
    if (string.IsNullOrWhiteSpace(tenantId))
        return Results.Problem("Missing X-User-Id header", statusCode: 401);
    
    // Set SSE response headers
    httpContext.Response.ContentType = "text/event-stream; charset=utf-8";
    httpContext.Response.Headers.CacheControl = "no-cache";
    httpContext.Response.Headers.Connection = "keep-alive";
    
    // Disable response buffering for real-time streaming
    var bufferingFeature = httpContext.Features.Get<IHttpResponseBodyFeature>();
    bufferingFeature?.DisableBuffering();
    
    await streamService.StreamEventsAsync(tenantId, httpContext.Response, httpContext.RequestAborted);
    return Results.Empty;
});
```

### Step 2.5: Implement the `GET /api/notifications/logs` Endpoint

```csharp
app.MapGet("/api/notifications/logs", async (
    HttpContext httpContext,
    CloudKbDbContext dbContext) =>
{
    var tenantId = httpContext.Request.Headers["X-User-Id"].ToString();
    
    if (string.IsNullOrWhiteSpace(tenantId))
        return Results.Problem("Missing X-User-Id header", statusCode: 401);
        
    var logs = await dbContext.IndexAuditLogs
        .Where(l => l.TenantId == tenantId)
        .OrderByDescending(l => l.LoggedAt)
        .Select(l => new IndexAuditLogResponse(
            l.Id,
            l.FileName,
            l.ActionType,
            l.SectionsAffected,
            l.CommitMessage,
            l.LoggedAt
        ))
        .ToListAsync();
        
    return Results.Ok(logs);
});
```

### Step 3: Implement `NotificationStreamService`

This service has two concurrent concerns running in parallel:
1. **Redis subscription** — receives events and writes them to the SSE stream
2. **Keep-alive timer** — sends `:ping` comments every 30 seconds

```csharp
public class NotificationStreamService : INotificationStreamService
{
    private readonly IConnectionMultiplexer _redis;
    
    public async Task StreamEventsAsync(string tenantId, HttpResponse response, CancellationToken ct)
    {
        var subscriber = _redis.GetSubscriber();
        var channel = RedisChannel.Literal($"ch:notifications:{tenantId}");
        
        // Channel to pass events from the Redis callback to the SSE writer
        var eventQueue = Channel.CreateUnbounded<string>();
        
        // Subscribe to the tenant's Redis Pub/Sub channel
        await subscriber.SubscribeAsync(channel, (ch, message) =>
        {
            if (message.HasValue)
                eventQueue.Writer.TryWrite(message.ToString());
        });
        
        try
        {
            await WriteEventsLoopAsync(response, eventQueue.Reader, ct);
        }
        finally
        {
            // Clean up subscription when client disconnects
            await subscriber.UnsubscribeAsync(channel);
        }
    }
    
    private async Task WriteEventsLoopAsync(
        HttpResponse response, 
        ChannelReader<string> eventReader, 
        CancellationToken ct)
    {
        var keepAliveInterval = TimeSpan.FromSeconds(30);
        
        while (!ct.IsCancellationRequested)
        {
            // Wait for either an event or a keep-alive timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(keepAliveInterval);
            
            try
            {
                if (await eventReader.WaitToReadAsync(timeoutCts.Token))
                {
                    while (eventReader.TryRead(out var rawJson))
                    {
                        await WriteEventFrameAsync(response, rawJson, ct);
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout — send keep-alive ping
                await WriteKeepAliveAsync(response, ct);
            }
        }
    }
}
```

### Step 4: Implement SSE Frame Writers

Following the exact wire format defined in `sse-protocol.md` Section 2:

```csharp
private static async Task WriteEventFrameAsync(HttpResponse response, string rawJson, CancellationToken ct)
{
    // Parse the JSON to extract eventType for the SSE event field
    var doc = JsonDocument.Parse(rawJson);
    var eventType = doc.RootElement.GetProperty("eventType").GetString();
    
    // Write SSE frame:
    //   event: IndexProcessing
    //   data: {"taskId":"...","message":"..."}
    //   \n
    var writer = response.BodyWriter;
    
    await response.WriteAsync($"event: {eventType}\n", ct);
    await response.WriteAsync($"data: {rawJson}\n", ct);
    await response.WriteAsync("\n", ct);
    await response.Body.FlushAsync(ct);
}

private static async Task WriteKeepAliveAsync(HttpResponse response, CancellationToken ct)
{
    // SSE comment line — ignored by EventSource clients but keeps connection alive
    await response.WriteAsync(":ping\n\n", ct);
    await response.Body.FlushAsync(ct);
}
```

### Step 5: Handle Client Disconnection Gracefully

When the frontend closes the `EventSource` connection (browser tab closed, page navigation, etc.):

1. The `CancellationToken` (`httpContext.RequestAborted`) fires.
2. The `WriteEventsLoopAsync` exits the while loop.
3. The `finally` block unsubscribes from the Redis channel.
4. No orphan subscriptions remain.

```csharp
// In Program.cs — configure Kestrel to detect disconnections quickly
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(10);
});
```

### Step 6: Handle Multiple Concurrent Tenants

Each incoming `GET /api/notifications/stream` request creates an independent Redis subscription scoped to that tenant's channel. Ensure:
- Subscriptions are per-request, not shared singletons.
- `IConnectionMultiplexer` is registered as a singleton in DI (StackExchange.Redis best practice).
- Each request gets its own `ISubscriber` instance from the shared multiplexer.

```csharp
// Program.cs DI registration
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("redis")!));
builder.Services.AddScoped<INotificationStreamService, NotificationStreamService>();
```

---

## Verification

### BDD Scenarios from `feature-4-notification-stream.feature`

- [ ] **SSE establishment:** `GET /api/notifications/stream` returns `Content-Type: text/event-stream`, connection stays open
- [ ] **Keep-alive heartbeat:** After 30s of silence, server sends `:ping\n\n`
- [ ] **Processing event relay:** Worker publishes `IndexProcessing` → frontend receives SSE event with correct `taskId` and `message`
- [ ] **Completed event relay:** Worker publishes `IndexCompleted` → frontend receives event with `metadata.sectionsCompiled`
- [ ] **Failed event relay:** Worker publishes `IndexFailed` → frontend receives event with `errorCode`
- [ ] **Multi-tenant isolation:** Events for `tenant-02` are NOT delivered to `tenant-01`'s stream
- [ ] **Reconnection:** After disconnect, a new `GET` request re-establishes subscription and receives new events

### Full Write Pipeline End-to-End Smoke Test

This test validates the entire Milestone 2 flow from upload to notification:

```bash
# Terminal 1: Open notification stream
TOKEN=$(dotnet user-jwts create --claim user_id=tenant-01)
curl -N http://localhost:5000/api/notifications/stream \
  -H "Authorization: Bearer $TOKEN"
# → Keep open, watch for SSE events

# Terminal 2: Upload files
curl -X POST http://localhost:5000/api/index \
  -H "Authorization: Bearer $TOKEN" \
  -F "files=@docs/TestingMarkdown/refund_policy.md" \
  -F "files=@docs/TestingMarkdown/account_help.md" \
  -F "files=@docs/TestingMarkdown/shipping_faq.md"
# → 202 Accepted

# Terminal 1 should now show:
# event: IndexProcessing
# data: {"eventType":"IndexProcessing","taskId":"...","message":"正在切分 Markdown Section..."}
#
# ... (after compilation completes) ...
#
# event: IndexCompleted
# data: {"eventType":"IndexCompleted","taskId":"...","message":"您的知識庫已編譯完成！","metadata":{"sectionsCompiled":...,"filesProcessed":3}}

# Terminal 3: Verify the full pipeline by asking a question
curl -N -X POST http://localhost:5000/api/chat \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"query": "How long do refunds take?"}'
# → SSE stream with grounded answer citing refund_policy.md
```

### Isolation Test

```bash
# Open stream for tenant-02
TOKEN2=$(dotnet user-jwts create --claim user_id=tenant-02)
curl -N http://localhost:5000/api/notifications/stream \
  -H "Authorization: Bearer $TOKEN2"

# Upload for tenant-01 (different terminal)
curl -X POST http://localhost:5000/api/index \
  -H "Authorization: Bearer $TOKEN" \
  -F "files=@docs/TestingMarkdown/refund_policy.md"

# tenant-02's stream should show NOTHING (only :ping heartbeats)
```

---

## Output Artifacts

| Artifact | Description |
| :------- | :---------- |
| `CloudKB.ApiService.Notification/Program.cs` | Minimal API with `GET /api/notifications/stream`, Kestrel config |
| `CloudKB.ApiService.Notification/Services/NotificationStreamService.cs` | Redis Pub/Sub subscriber + SSE writer loop + keep-alive |
