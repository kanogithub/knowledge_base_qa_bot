## Epic 3: Multi-tenant Knowledge Base Management & Streaming Chat Interface (Front-End UI)

* **Core Goal:** Build a single-page application (SPA) frontend using React, Vite, TypeScript, and Tailwind CSS. The interface will provide multi-tenant authentication simulation, drag-and-drop file upload, real-time index compilation progress tracking via SSE notifications, a commit history changelog wall, and a conversational chat UI that displays streaming model responses with interactive citations.

### ── Feature 8: Integrated Knowledge Base Dashboard & Grounded Chat Interface (React Frontend Dashboard)

* **Functional Description:** Responsible for implementing the user interface of the system. This includes tenant login simulation, drag-and-drop Markdown uploads with async task tracking, long-lived SSE notification hook to show real-time compile status, auto-refreshing commit history log wall, and a highly interactive grounded Chat UI capable of reading token streams and highlighting cited sections in a sidebar drawer.
* **Verification & Implementation Indicators:**
  - Create the React + TS project under `src/CloudKB.Web` with Tailwind CSS integration, and register it as an `AddNpmApp` resource in `CloudKB.AppHost` to run within the Aspire dashboard.
  - Implement tenant login and JWT store, establishing a token-authenticated SSE channel (`GET /api/notifications/stream`) using `event-source-polyfill` to listen for progress (`IndexProcessing`), success (`IndexCompleted`), and failure (`IndexFailed`) events.
  - Support dragging `.md` files to compile a `FormData` upload to `/api/index`, showing the `TaskId` upon `202 Accepted` response. Trigger an automatic fetch to reload the commit history wall (`GET /api/notifications/logs`) upon receiving an `IndexCompleted` SSE event.
  - Stream the response of `/api/chat` using `ReadableStream` reader, parsing the initial citations metadata and subsequent typewriter tokens. Render citations as interactive clickable badges that display the referenced section contents in a drawer when clicked.
  - Implement tenant self-registration form (`POST /api/auth/register`) validating password strength and unique account creations in the database.
  - Provide table row action to delete uploaded files, invoking `DELETE /api/index/{fileName}` to execute DB cascades, clean storage assets, and recompute/clear Redis cache indexes.

