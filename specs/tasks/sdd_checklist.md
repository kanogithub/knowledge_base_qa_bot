# Cloud-KB — SDD Feature & Task Master Checklist

This checklist tracks the implementation progress of all features and tasks in the Cloud-KB system. AI Agents MUST use this file as the source of truth to check where to resume development in the event of failures, timeouts, or context restarts.

---

## State Legend
* `[ ]` : Not Started (未進行)
* `[-]` : In Progress (進行中)
* `[x]` : Completed (已完成)

---

## Progress Overview

- [x] **Milestone 0: Foundation & Infrastructure**
  - [x] **Task 0: Foundation Setup** ([task-0-foundation.md](./milestone-0-foundation/task-0-foundation.md))
    - [x] Create Aspire 10 Solution and wired project references
    - [x] Configure AppHost orchestration and ServiceDefaults
    - [x] Define shared kernel DTOs (Chat, Notifications, Logs, Health)
    - [x] Implement 4-stage shared Tokeniser (`SearchValues<string>`)
    - [x] Implement BM25 Engine with heading boost & threshold
    - [x] Setup PostgreSQL DbContext with EF Core 10 entities
    - [x] Create testing seeder for `tenant-01`
    - [x] Bootstrap BDD Test Scaffolding with JWT & LLM Mocking

- [x] **Milestone 1: Read Pipeline (Epic 2)**
  - [x] **Feature 6: In-Memory Retrieval & Short-Lived Chat SSE**
    - [x] **Task 6: Chat QA Engine** ([task-6-chat-qa-engine.md](./milestone-1-read-pipeline/task-6-chat-qa-engine.md))
      - [x] Implement `POST /api/chat` Minimal API Endpoint
      - [x] Implement Redis Index Loader
      - [x] Implement Early-Exit logic (Threshold < 0.5)
      - [x] Implement PostgreSQL Top-K section fetching
      - [x] Implement Microsoft.Extensions.AI streaming prompt call
      - [x] Implement short-lived SSE writer and connection closer
  - [x] **Feature 5: Chat Gateway Routing & HTTP/2**
    - [x] **Task 5: Chat Gateway** ([task-5-chat-gateway.md](./milestone-1-read-pipeline/task-5-chat-gateway.md))
      - [x] Scaffold Gateway project with Yarp
      - [x] Configure Kestrel HTTP/2 for stream multiplexing
      - [x] Configure JWT Authentication & Authorization
      - [x] Configure Yarp chat routing and Downstream custom transforms
      - [x] Map anonymous `/health` check path

  - [x] **Feature 7: System-Wide Health Check Endpoint**
    - [x] **Task 7: Health Check** ([task-7-health-check.md](./milestone-1-read-pipeline/task-7-health-check.md))
      - [x] Implement local health endpoints in all downstream API services
      - [x] Expose anonymous gateway liveness check endpoint returning `{"status": "ok"}`
      - [x] Verify health status responses match specs using acceptance tests

- [-] **Milestone 2: Write Pipeline & Frontend (Epic 1 & 3)**
  - [x] **Feature 1: Gateway Write Route Auth & Security Boundaries**
    - [x] **Task 1: Full Gateway Auth** ([task-1-gateway-auth.md](./milestone-2-write-pipeline/task-1-gateway-auth.md))
      - [x] Add Yarp proxy routes for Ingest (`POST /api/index`) and Notification stream/logs
      - [x] Extend `TenantIdTransformProvider` for all write routes
      - [x] Configure SSE-friendly Proxy buffering & Activity timeout

  - [x] **Feature 2: Async Ingest & S3 Stream Ingestion**
    - [x] **Task 2: Async File Ingestion** ([task-2-async-ingest.md](./milestone-2-write-pipeline/task-2-async-ingest.md))
      - [x] Create `CloudKB.ApiService.Indexing` project
      - [x] Implement multipart/form-data validator (.md files only)
      - [x] Implement `EnsureBucketExistsAsync` in S3 Storage Service
      - [x] Implement non-buffered stream upload to MinIO/S3
      - [x] Create PostgreSQL `TenantFile` and compilation job audit records
      - [x] Publish `CompileKnowledgeTask` message to RabbitMQ
      - [x] Ensure non-blocking SLA < 100ms for HTTP 202 response

  - [x] **Feature 3: Background Worker & Incremental Diff Engine**
    - [x] **Task 3: Knowledge Compilation Worker** ([task-3-knowledge-compilation.md](./milestone-2-write-pipeline/task-3-knowledge-compilation.md))
      - [x] Create Background Indexer worker consuming RabbitMQ compile queue
      - [x] Setup RabbitMQ retry policy and Dead-Letter Queue (DLQ)
      - [x] Implement Redis-based distributed lock (`lock:index:{user_id}`)
      - [x] **Stage 1 (Fast-Pass Skip)**: Check S3 content hash against `TenantFileState`
      - [x] **Stage 2 (Section Diff)**: Parse Markdown headings & calculate ADDED/MODIFIED/DELETED
      - [x] Bulk insert modifications to PostgreSQL via EF Core batched SaveChanges
      - [x] Write revision items into `IndexAuditLog` changelog table
      - [x] **Stage 3 (Re-aggregation)**: Refresh BM25 stats & update cached JSON in Redis
  - [x] **Feature 4: Real-time Pub/Sub Notifications & Changelog Feed**
    - [x] **Task 4: Notification SSE Stream & Logs** ([task-4-notification-stream.md](./milestone-2-write-pipeline/task-4-notification-stream.md))
      - [x] Create Notification Minimal API service
      - [x] Implement `GET /api/notifications/stream` long-lived EventSource connection
      - [x] Implement parallel keep-alive timer (:ping heartbeat every 30s)
      - [x] Subscribe to Redis Pub/Sub channels and push event frames downstream
      - [x] Implement `GET /api/notifications/logs` returning JSON audit list
      - [x] Handle graceful client disconnection and resource cleanup
  - [ ] **Feature 8: Integrated Knowledge Base Dashboard & Grounded Chat Interface**
    - [ ] **Task 8: Frontend Interface** ([task-8-frontend.md](./milestone-2-write-pipeline/task-8-frontend.md))
      - [ ] Scaffold Vite React + TS + Tailwind project under `CloudKB.Web`
      - [ ] Implement JWT Token Mock authorization store
      - [ ] Implement Drag-and-Drop file uploader communicating with Ingest API
      - [ ] Integrate notification events to trigger status toast messages
      - [ ] Implement grounded Chat Box with streaming message parser and citation rendering
      - [ ] Implement Commit History Wall UI mapping Added/Modified/Deleted badges
