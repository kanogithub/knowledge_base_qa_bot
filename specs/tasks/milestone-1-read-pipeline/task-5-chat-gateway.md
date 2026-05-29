# Task 5: Chat Gateway — Yarp Reverse Proxy Routing, JWT Validation & HTTP/2 Optimisation

## Goal

Implement `CloudKB.Gateway` as a Yarp reverse proxy that authenticates `POST /api/chat` requests via JWT, extracts the `user_id` claim into the `X-User-Id` downstream header, and forwards the request to `CloudKB.ApiService.Chat`. The gateway must enable HTTP/2 for SSE stream multiplexing.

---

## SDD Spec References

Read these spec files **before writing any code**:

| Spec File | What to Extract |
| :-------- | :-------------- |
| [openapi.yaml](../../openapi.yaml) | `POST /api/chat` path definition, `BearerAuth` security scheme, `X-User-Id` parameter |
| [aspire-topology.yaml](../../aspire-topology.yaml) | Gateway resource name `gateway`, downstream service name `apiservice-chat`, port bindings |
| [feature-5-chat-gateway.feature](../../features/feature-5-chat-gateway.feature) | All BDD scenarios: valid routing, JWT rejection, query validation, concurrency |
| [feature-1-gateway-auth.feature](../../features/feature-1-gateway-auth.feature) | JWT validation scenarios (shared with Milestone 2, implement the chat-related subset now) |

---

## Implementation Steps

### Step 1: Add Yarp NuGet Package

```xml
<PackageReference Include="Yarp.ReverseProxy" Version="2.*" />
```

### Step 2: Configure Kestrel for HTTP/2

In `Program.cs`:
```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });
});
```

### Step 3: Configure JWT Authentication

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidAudience = builder.Configuration["Auth:Audience"],
        };
    });

builder.Services.AddAuthorization();
```

> **For local development:** Use a self-signed JWT with a symmetric key for testing. The important thing is that `user_id` is present in the JWT claims.

### Step 4: Configure Yarp Route for Chat

In `appsettings.json`:
```json
{
  "ReverseProxy": {
    "Routes": {
      "chat-route": {
        "ClusterId": "chat-cluster",
        "AuthorizationPolicy": "default",
        "Match": {
          "Path": "/api/chat",
          "Methods": ["POST"]
        },
        "Transforms": [
          { "RequestHeader": "X-User-Id", "Set": "{ClaimValue:user_id}" },
          { "RequestHeaderRemove": "Authorization" }
        ]
      }
    },
    "Clusters": {
      "chat-cluster": {
        "Destinations": {
          "chat-primary": {
            "Address": "http://apiservice-chat"
          }
        }
      }
    }
  }
}
```

> **Critical:** The `Address` value `http://apiservice-chat` MUST match the Aspire service discovery name defined in `aspire-topology.yaml`.

### Step 5: Add Custom Transform for X-User-Id Injection

If Yarp's built-in `{ClaimValue:...}` transform is insufficient, implement a custom `ITransformProvider`:

```csharp
public class TenantIdTransformProvider : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context) { }
    public void ValidateCluster(TransformClusterValidationContext context) { }
    
    public void Apply(TransformBuilderContext context)
    {
        context.AddRequestTransform(async transformContext =>
        {
            var userId = transformContext.HttpContext.User.FindFirstValue("user_id");
            if (!string.IsNullOrEmpty(userId))
            {
                transformContext.ProxyRequest.Headers.Remove("X-User-Id");
                transformContext.ProxyRequest.Headers.Add("X-User-Id", userId);
            }
            // Remove Authorization header from downstream request
            transformContext.ProxyRequest.Headers.Remove("Authorization");
        });
    }
}
```

### Step 6: Add Health Endpoint

```csharp
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
```

### Step 7: Wire Up the Pipeline

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.MapReverseProxy();
```

---

## Verification

### Automated (BDD Scenarios from `feature-5-chat-gateway.feature`)

- [ ] **Valid chat routing:** `POST /api/chat` with valid JWT forwards to Chat Service, response is `text/event-stream`
- [ ] **Missing JWT rejected:** Request without Authorization header returns HTTP 401
- [ ] **Empty query rejected:** `POST /api/chat` with `{"query": ""}` returns HTTP 400
- [ ] **Oversized query rejected:** Query > 2000 chars returns HTTP 400
- [ ] **X-User-Id injection:** Downstream request contains `X-User-Id` header matching JWT `user_id` claim

### From `feature-1-gateway-auth.feature` (chat-related subset)

- [ ] **Expired JWT rejected:** Returns HTTP 401
- [ ] **Malformed JWT rejected:** `Bearer not-a-jwt` returns HTTP 401

### End-to-End Smoke Test

```bash
# Generate a test JWT (for local dev, use a simple JWT generator)
TOKEN=$(dotnet user-jwts create --claim user_id=tenant-01)

# Valid request through gateway
curl -N -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"query": "How long do refunds take?"}'
# → Should see SSE stream from downstream Chat Service

# No token
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"query": "Hello"}'
# → 401 Unauthorized
```

---

## Output Artifacts

| Artifact | Description |
| :------- | :---------- |
| `CloudKB.Gateway/Program.cs` | Kestrel HTTP/2, JWT auth, Yarp reverse proxy pipeline |
| `CloudKB.Gateway/TenantIdTransformProvider.cs` | Custom X-User-Id injection transform |
| `CloudKB.Gateway/appsettings.json` | Yarp route/cluster config for chat endpoint |
