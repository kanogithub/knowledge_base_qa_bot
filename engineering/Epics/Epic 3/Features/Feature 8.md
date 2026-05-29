## 📄 Feature 8: Integrated Knowledge Base Dashboard & Grounded Chat Interface

* **Feature Group:** User Interface & Presentation Layer
* **Implementation Components:** `CloudKB.Web (React + Vite + TS)` + `event-source-polyfill` + `CloudKB.AppHost`

### 1. Feature Overview

Provides the central console (Dashboard) for users to interact with the system. It connects the complex asynchronous ingestion pipeline, real-time SSE notifications, audit logs, and grounded streaming Q&A into a single, cohesive web interface. This dashboard enables end-to-end visualization and testing of all backend services.

### 2. Core Technical Specifications

* **Vite React Architecture & Aspire Integration:** Scaffolds the client-side SPA with React, TypeScript, and Tailwind CSS. The app runs locally and registers in `CloudKB.AppHost` using the `AddNpmApp` helper, ensuring it starts up concurrently with other microservices and resolves API endpoints through the Aspire Service Discovery/Gateway.
* **JWT Identity Simulation & Auth Headers:** Provides a mock login form for selecting/inputting a `Tenant ID` (e.g. `tenant-01`) and simulating token issuance. The token is stored locally (React context or `localStorage`) and must be automatically appended as an `Authorization: Bearer <token>` header to all subsequent API requests.
* **Notification Stream (SSE) & Changelog History Wall:** Connects to the `/api/notifications/stream` endpoint using `event-source-polyfill` to pass the authorization token. The client handles the following events:
  - `IndexProcessing` ➡️ Display status messages and a loading spinner.
  - `IndexCompleted` ➡️ Raise a success toast and trigger an automatic HTTP request to refresh the **Commit History Wall** (`GET /api/notifications/logs`).
  - `IndexFailed` ➡️ Raise an error dialog or toast showing the details.
  The Commit History Wall renders a chronological feed of file indexing audits, using color-coded badges for action types (Green for `ADDED`, Yellow for `MODIFIED`, and Red for `DELETED`).
* **Drag-and-Drop Ingestion:** Provides a user-friendly drag-and-drop dropzone that accepts only `.md` files. Dropped files are read into a `FormData` payload and dispatched to `/api/index`. Upon receiving `202 Accepted` with a `TaskId`, the UI enters a queue-tracking state, waiting for the notification stream to announce compilation completion.
* **Streaming Grounded Chat & Citation Drawer:** Posts query payloads to `/api/chat` and reads the response body via a `ReadableStream` reader line-by-line to parse Server-Sent Events.
  - The first chunk contains the citations metadata list. The UI stores this list in state.
  - Subsequent chunks contain text tokens, which are appended to the assistant's message bubble with a typewriter effect.
  - Any citation string matching the format `[filename#heading]` is parsed and rendered as an interactive, clickable link/badge.
  - Clicking a citation badge opens a sidebar drawer showing the exact text content of the cited section.
* **User Self-Registration Flow:**
  - Adds a toggleable "Sign Up" form allowing new tenant users to create credentials.
  - Client validates password matching and length (minimum 6 characters).
  - API Gateway registers `/api/auth/register`, hashes passwords securely via `IPasswordHasher`, checks for unique usernames, and persists user profiles in the PostgreSQL `tenant_users` table.
* **Knowledge File Deletion & BM25 Cache Recompute:**
  - Adds a "Delete" button to the files directory table rows.
  - Issues a `DELETE /api/index/{fileName}` request to the backend.
  - The indexing service removes the file metadata, file states, and all parsed section rows from PostgreSQL.
  - Records a `DELETED` entry in the `index_audit_logs`.
  - Removes the file from storage (MinIO/Local).
  - Triggers an immediate BM25 statistic re-aggregation over the tenant's remaining sections, rewriting the new index into the Redis cache (or deleting the key completely if 0 documents remain).

