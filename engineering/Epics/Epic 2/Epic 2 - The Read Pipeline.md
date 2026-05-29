## Epic 2: High-Performance, Task-Based Short Streaming QA Retrieval (The Read Pipeline)

* **Core Goal:** Handle high-QPS traffic in production environments, delivering a smooth, grounded, and typewriter-style AI streaming QA experience. The CQRS design ensures that QA traffic does not impact the transactional database or the background indexing core.

### ── Feature 5: Multi-Tenant QA Gateway Routing & High-Concurrency Transmission Optimization

* **Functional Description:** Acts as the first line of defense for the read pipeline. Handles real-time JWT authentication and tenant context injection. It optimizes Kestrel HTTP transmission protocols to ensure low-latency, uninterrupted connections across multiple client streaming QA sessions (Short-Lived SSE) during high-QPS bursts.
* **Verification & Implementation Indicators:**
  - Intercept and validate JWT tokens on `POST /api/chat`. Extract `user_id` and inject it as `X-User-Id` header, restricting Chat service access to authorized tenant cache and text segments.
  - Enable HTTP/2 or HTTP/3 multiplexing, allowing multiple concurrent users to stream answers over a single TCP connection, reducing CPU and memory overhead from frequent handshakes.

### ── Feature 6: Low-Latency In-Memory Routing & Short-Lived Chat Response Streaming

* **Functional Description:** Acts as the core execution engine for user queries. Utilizes an optimized, in-memory explicit index lookup to navigate facts without querying PostgreSQL or embeddings. It implements early exit logic to block invalid queries and streams model answers back to clients, releasing resources immediately on completion.
* **Verification & Implementation Indicators:**
  - Read `TenantKbIndex` JSON from Redis with $\mathcal{O}(1)$ speed. Leverage .NET 10's `FrozenDictionary` to run BM25 term frequency calculations and Heading Boost weighting in C# memory.
  - If the highest retrieved section score is below the threshold (e.g. 0.5), exit early and immediately respond with a rejection message (e.g., `"我無法從現有的知識庫中確認此訊息。"`). **This path must not query PostgreSQL or the LLM API.**
  - Query PostgreSQL using PK lookup for the Top-K Section IDs to fetch raw text. Pack facts into a Grounded Prompt, call LLM stream API, and write standard `text/event-stream` chunks. Once the final token (including Sources metadata) is written, close the connection.

### ── Feature 7: System-Wide Health Check Endpoint

* **Functional Description:** Exposes a standardized liveness probe endpoint `/health` on the Gateway and API services to enable orchestration and monitoring tools to check the status of the system.
* **Verification & Implementation Indicators:**
  - Expose `GET /health` on the API Gateway, returning `200 OK` with JSON `{"status": "ok"}`.
  - Implement local health endpoints in downstream services using ASP.NET Core Health Checks middleware.