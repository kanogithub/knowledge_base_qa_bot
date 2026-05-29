# Task 7: System-Wide Health Check Endpoint — Gateway & Service Liveness Probes

## Goal

Configure and expose a standardized health check endpoint `/api/health` on the API Gateway (`CloudKB.Gateway`) and all downstream microservices (`CloudKB.ApiService.Chat`, `CloudKB.ApiService.Indexing`, `CloudKB.ApiService.Notification`) to support container orchestration liveness checks and system status monitoring.

---

## SDD Spec References

Read these spec files **before writing any code**:

| Spec File | What to Extract |
| :-------- | :-------------- |
| [openapi.yaml](../../openapi.yaml) | Path model for `/api/health` endpoint returning `200 OK` with `{"status": "ok"}` |
| [feature-7-health-check.feature](../../features/feature-7-health-check.feature) | Acceptance criteria for gateway and local microservice health checks |

---

## Implementation Steps

### Step 1: Implement Health Endpoint on API Gateway

In `CloudKB.Gateway` project:
1. Register an anonymous route for `GET /api/health`.
2. Return an `IResult` yielding **HTTP 200 OK** with JSON body matching the schema:
   ```json
   {
     "status": "ok"
   }
   ```
3. Ensure this path is bypassed by JWT authorization and Yarp reverse proxy forwarding.

### Step 2: Implement Health Endpoint on downstream services

In `CloudKB.ApiService.Chat`, `CloudKB.ApiService.Indexing`, and `CloudKB.ApiService.Notification` projects:
1. Register local ASP.NET Core Health Checks using `builder.Services.AddHealthChecks()`.
2. Map the health check middleware using `app.MapHealthChecks("/api/health")`.
3. Verify that the response returns **HTTP 200 OK** and a status indicating `"Healthy"` or `"ok"`.

### Step 3: Configure .NET Aspire Orchestration Probes

In `CloudKB.AppHost`:
1. Ensure downstream services are configured to allow Aspire health checks if applicable, or configure resource health checks on their HTTP endpoints.

---

## Verification

- [ ] Send query to gateway:
  ```bash
  curl -i http://localhost:8000/api/health
  ```
  Response is `HTTP 200 OK` with header `Content-Type: application/json` and body `{"status": "ok"}`.
- [ ] Send query directly to microservice local ports:
  ```bash
  curl -i http://localhost:<port>/api/health
  ```
  Each service responds with `HTTP 200 OK` containing `"status": "Healthy"` or `"status": "ok"`.
- [ ] BDD tests pass:
  ```bash
  dotnet test --filter "Category=feature-7"
  ```

---

## Output Artifacts

| Artifact | Description |
| :------- | :---------- |
| `src/CloudKB.Gateway/Program.cs` | Registers anonymous health check endpoint route returning `{"status": "ok"}` on `/api/health` |
| `src/CloudKB.ApiService.Chat/Program.cs` | Maps local `/api/health` check middleware |
| `src/CloudKB.ApiService.Indexing/Program.cs` | Maps local `/api/health` check middleware |
| `src/CloudKB.ApiService.Notification/Program.cs` | Maps local `/api/health` check middleware |
