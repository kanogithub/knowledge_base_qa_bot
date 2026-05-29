# Task 8: Frontend Interface — React Dashboard, Multipart Upload, SSE Notification Relay & Grounded Chat UI

## Goal

Build the `CloudKB.Web` frontend, a single-page React application using Vite, TypeScript, and Tailwind CSS. The frontend will allow users to authenticate, upload Markdown documents, track index compilation progress in real time via Server-Sent Events, and ask grounded questions in a conversational interface with streaming tokens and visual citations.

---

## SDD Spec References

Read these spec files **before writing any code**:

| Spec File | What to Extract |
| :-------- | :-------------- |
| [openapi.yaml](../../openapi.yaml) | Path models and payloads for `/api/index` (upload), `/api/chat` (chat streaming), and `/api/notifications/stream` (SSE notifications) |
| [sse-protocol.md](../../sse-protocol.md) | Wire formats for both channels: keep-alive format, notification payload formats, chat chunk frames, source citing, and termination events |
| [appsettings.schema.json](../../appsettings.schema.json) | Port bindings and service discovery names mapping to Gateway (`http://localhost:5000` locally) |

---

## Implementation Steps

### Step 1: Scaffold the Vite React Application

Under `src/`, scaffold a new React + TS project.

**Commands:**
```bash
# From workspace root
npx -y create-vite@latest src/CloudKB.Web --template react-ts
# Install tailwindcss and basic dependencies
cd src/CloudKB.Web
npm install
npm install -D tailwindcss postcss autoprefixer
npx tailwindcss init -p
```

Configure `tailwind.config.js` and `src/index.css` to enable Tailwind styling.
Reference this web application's frontend in `CloudKB.AppHost` (as an `AddNpmApp` resource mapped to `src/CloudKB.Web`).

### Step 2: Implement JWT Token Store & Authentication Mock

Create a lightweight auth shell or mock login component:
1. Allow the user to input a `Tenant ID` (e.g., `tenant-01`).
2. Generate or fetch a locally-signed JWT token representing that tenant (you can integrate the `dotnet user-jwts` credentials or mock endpoints from the gateway).
3. Save this token in `localStorage` or React Context and append it as `Authorization: Bearer <token>` to all API requests.

### Step 3: Establish Long-Lived Notification Channel (EventSource)

Create a custom React hook `useNotifications` that:
1. Connects to `http://localhost:5000/api/notifications/stream`.
2. Since standard browser `EventSource` does not support custom headers, install `event-source-polyfill` or append the token as a query parameter if supported, OR configure the Yarp gateway to accept token cookies.
   * **Recommended package:** `npm install event-source-polyfill`
   * **Usage:** 
     ```typescript
     const eventSource = new EventSourcePolyfill('http://localhost:5000/api/notifications/stream', {
       headers: {
         'Authorization': `Bearer ${token}`
       }
     });
     ```
3. Register event listeners for SSE events matching `sse-protocol.md` Section 2:
   - `IndexProcessing` -> Update UI to show "Compiling markdown sections..." with progress indicator.
   - `IndexCompleted` -> Display a success Toast showing the count of compiled sections (`event.metadata.sectionsCompiled`).
   - `IndexFailed` -> Display an error Alert with details.
4. Automatically handle reconnection on network drop.

### Step 4: Implement Drag-and-Drop Markdown File Uploader

Create a dashboard panel for uploading knowledge-base documents:
1. A file-drop area accepting `.md` files only.
2. When files are dropped/selected, compile them into a `FormData` object.
3. Send a `POST http://localhost:5000/api/index` request via `fetch` with the `Authorization` header.
4. On receiving `202 Accepted`, display the returned `TaskId` and notify the user that compilation has been enqueued.

### Step 5: Implement Streaming grounded Chat UI

Create a chat container for question-answering:
1. Input field for query submissions.
2. Submit sends a `POST http://localhost:5000/api/chat` with JSON body `{"query": "..."}`.
3. Consume the response body as a stream (using `ReadableStream` reader) to parse incoming SSE lines:
   ```typescript
   const response = await fetch('http://localhost:5000/api/chat', {
     method: 'POST',
     headers: {
       'Content-Type': 'application/json',
       'Authorization': `Bearer ${token}`
     },
     body: JSON.stringify({ query })
   });
   
   const reader = response.body?.getReader();
   // Read stream chunks line-by-line, parse "data: {...}" JSON segments
   ```
4. **SSE Parsing Logic**:
   - First frame contains **Sources** metadata (citelist). Store this list in state.
   - Middle frames contain **Token text**. Append these tokens to the active message content in real time (simulating typewriter effect).
   - Final frame contains `isFinal: true` and terminal citations. Close the connection.
5. If early-exit threshold is hit, render the refusal text cleanly.

### Step 6: Render Citations with File Navigation

When displaying chat messages containing citations (e.g. `[refund_policy.md#refund-timeline]`):
1. Parse the text markdown in the chat bubble.
2. Detect citation formats and render them as interactive links/badges.
3. Clicking a citation badge highlights the reference section in the source metadata drawer.

### Step 7: Implement the Commit History Wall

Create a `CommitHistory` component to visualize index mutation logs:
1. Fetch logs from `GET http://localhost:5000/api/notifications/logs` with the `Authorization` header.
2. Render a chronological feed showing the changelog for each file revision.
3. Apply color-coded styling depending on `actionType`:
   - `ADDED` -> Green border/text, display details of added sections.
   - `MODIFIED` -> Yellow/Orange border/text, show edited section stats.
   - `DELETED` -> Red border/text, show deleted section headings.
4. Auto-refresh this history wall when the `useNotifications` hook receives an `IndexCompleted` event.

---

## Verification

- [ ] Web App runs locally via `npm run dev` and integrates with Aspire dashboard
- [ ] Dragging `refund_policy.md` and uploading returns `202 Accepted`
- [ ] A success toast pops up with compiled counts without reloading the page when compilation finishes
- [ ] Uploading an updated file updates the **Commit History Wall** showing exact added/modified/deleted badges
- [ ] Sending query "How long do refunds take?" displays tokens scrolling in real-time
- [ ] Sources used (e.g. `refund_policy.md`) appear under the answered block immediately

---

## Output Artifacts

| Artifact | Description |
| :------- | :---------- |
| `src/CloudKB.Web/package.json` | Web application configurations and package dependencies |
| `src/CloudKB.Web/src/App.tsx` | Main dashboard layout (uploader, notification listener, history wall, and chat interface) |
| `src/CloudKB.Web/src/hooks/useNotifications.ts` | EventSource management hook for Yarp notification connection |
| `src/CloudKB.Web/src/components/ChatBox.tsx` | Grounded chat bubble renderer parsing stream chunks and citations |
| `src/CloudKB.Web/src/components/CommitHistory.tsx` | Visual changelog history feed showing ADDED/MODIFIED/DELETED indicators |
