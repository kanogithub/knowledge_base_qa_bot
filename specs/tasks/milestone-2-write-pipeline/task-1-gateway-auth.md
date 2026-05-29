# Task 1: Full Gateway Auth — Extend Yarp with Write Pipeline Routes & Complete JWT Governance

## Goal

Extend the `CloudKB.Gateway` (already created in Task 5 for the chat route) to cover **all** system routes: `POST /api/index`, `GET /api/notifications/stream`, and `GET /health`. This task completes the full multi-tenant security boundary for both read and write pipelines.

---

## SDD Spec References

| Spec File | What to Extract |
| :-------- | :-------------- |
| [openapi.yaml](../../openapi.yaml) | All 4 endpoints: `/health`, `/api/index`, `/api/chat`, `/api/notifications/stream` |
| [aspire-topology.yaml](../../aspire-topology.yaml) | Service names: `apiservice-indexing`, `apiservice-notification` |
| [feature-1-gateway-auth.feature](../../features/feature-1-gateway-auth.feature) | Full BDD scenario set (JWT validation, tenant injection, HTTP/2 mux) |

---

## Prerequisites

- **Task 0** (Foundation) completed
- **Task 5** (Chat Gateway) completed — `CloudKB.Gateway` already exists with the chat route

---

## Implementation Steps

### Step 1: Add Yarp Routes for Index and Notification Endpoints

Extend `appsettings.json` ReverseProxy configuration:

```json
{
  "ReverseProxy": {
    "Routes": {
      "chat-route": { /* ... already exists from Task 5 ... */ },
      
      "index-route": {
        "ClusterId": "indexing-cluster",
        "AuthorizationPolicy": "default",
        "Match": {
          "Path": "/api/index",
          "Methods": ["POST"]
        },
        "Transforms": [
          { "RequestHeader": "X-User-Id", "Set": "{ClaimValue:user_id}" },
          { "RequestHeaderRemove": "Authorization" }
        ]
      },
      
      "notification-route": {
        "ClusterId": "notification-cluster",
        "AuthorizationPolicy": "default",
        "Match": {
          "Path": "/api/notifications/stream",
          "Methods": ["GET"]
        },
        "Transforms": [
          { "RequestHeader": "X-User-Id", "Set": "{ClaimValue:user_id}" },
          { "RequestHeaderRemove": "Authorization" }
        ]
      },
      
      "notification-logs-route": {
        "ClusterId": "notification-cluster",
        "AuthorizationPolicy": "default",
        "Match": {
          "Path": "/api/notifications/logs",
          "Methods": ["GET"]
        },
        "Transforms": [
          { "RequestHeader": "X-User-Id", "Set": "{ClaimValue:user_id}" },
          { "RequestHeaderRemove": "Authorization" }
        ]
      }
    },
    "Clusters": {
      "chat-cluster": { /* ... already exists ... */ },
      
      "indexing-cluster": {
        "Destinations": {
          "indexing-primary": {
            "Address": "http://apiservice-indexing"
          }
        }
      },
      
      "notification-cluster": {
        "Destinations": {
          "notification-primary": {
            "Address": "http://apiservice-notification"
          }
        }
      }
    }
  }
}
```

### Step 2: Ensure the TenantIdTransformProvider Covers All Routes

The custom `TenantIdTransformProvider` from Task 5 should already apply to all routes. Verify that:
- `X-User-Id` is injected for `/api/index`, `/api/chat`, `/api/notifications/stream`, and `/api/notifications/logs`
- The `Authorization` header is stripped from downstream requests

### Step 3: Configure SSE-Friendly Proxy Behaviour for Notification Route

The notification route proxies a **long-lived SSE stream**. Yarp must be configured to:
- Disable response buffering
- Allow long-lived connections (no timeout or set a very long timeout)

```csharp
// In the cluster config or via transform:
"HttpRequest": {
  "ActivityTimeout": "00:30:00"  // 30 minutes for long-lived SSE
}
```

### Step 4: Keep the Health Endpoint Unauthenticated

The `GET /health` endpoint must remain accessible without a JWT token. It is served directly by the Gateway (not proxied):

```csharp
app.MapGet("/health", () => Results.Ok(new HealthResponse("ok")))
   .AllowAnonymous();
```

---

## Verification

### BDD Scenarios from `feature-1-gateway-auth.feature`

- [ ] Valid JWT passes for `POST /api/index` → downstream receives `X-User-Id`
- [ ] Valid JWT passes for `GET /api/notifications/stream` → downstream receives `X-User-Id`
- [ ] Missing JWT → HTTP 401 for all protected routes
- [ ] Expired JWT → HTTP 401
- [ ] Malformed JWT → HTTP 401
- [ ] `GET /health` → HTTP 200 without any JWT
- [ ] Multiple SSE streams multiplex over single HTTP/2 connection

### Smoke Test

```bash
TOKEN=$(dotnet user-jwts create --claim user_id=tenant-01)

# Index route
curl -X POST http://localhost:5000/api/index \
  -H "Authorization: Bearer $TOKEN" \
  -F "files=@docs/TestingMarkdown/refund_policy.md"
# → 202 or forwarded to downstream

# Notification route
curl -N http://localhost:5000/api/notifications/stream \
  -H "Authorization: Bearer $TOKEN"
# → SSE stream (long-lived)

# Health (no auth)
curl http://localhost:5000/health
# → {"status":"ok"}
```

---

## Output Artifacts

| Artifact | Description |
| :------- | :---------- |
| `CloudKB.Gateway/appsettings.json` | Extended with index + notification routes |
