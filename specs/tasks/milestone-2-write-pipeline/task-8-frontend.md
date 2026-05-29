# Task 8: Frontend Interface — React Dashboard, Auth Login, Ingestion & Grounded Chat UI

## Goal

Build the `CloudKB.Web` frontend, a single-page React application using Vite, TypeScript, and Tailwind CSS v3. The frontend is hosted directly from the API Gateway (`CloudKB.Gateway`) static files directory (`wwwroot/`). It enables users to authenticate using credentials, drag-and-drop Markdown documents, list uploaded files, and ask grounded questions in a split pane layout with streaming tokens and Markdown citations.

---

## SDD Spec References

Read these spec files **before writing any code**:

| Spec File | What to Extract |
| :-------- | :-------------- |
| [openapi.yaml](../../openapi.yaml) | Path models and payloads for `/api/auth/login` (authentication), `/api/index` (upload), `/api/index/files` (files list), and `/api/chat` (chat streaming) |
| [feature-8-frontend.feature](../../features/feature-8-frontend.feature) | Acceptance criteria for login layout, Drag-and-Drop files upload, file listing, and grounded chat window |

---

## Implementation Steps

### Step 1: Scaffold the Vite React Application

Under `src/`, scaffold a new React + TS project.

**Commands:**
```bash
# From workspace root
cd src/CloudKB.Web
npm install
npm install -D tailwindcss@3 postcss autoprefixer
npx tailwindcss init -p
npm install event-source-polyfill lucide-react markdown-to-jsx
```

Configure `tailwind.config.js` to scan files in `src/CloudKB.Web` and configure `src/index.css` to enable Tailwind styling.
In `vite.config.ts`, set the build output directory to the Gateway's `wwwroot/` folder:
```typescript
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../../src/CloudKB.Gateway/wwwroot',
    emptyOutDir: true
  }
})
```

---

### Step 2: Implement JWT Login Form & Local Storage

Create a login view in the React app:
1. Render a clean card interface with Username and Password fields.
2. Submit executes a `POST /api/auth/login` request.
3. Save the returned `token` in `localStorage` and redirect to the dashboard layout.
4. On the dashboard, display a "Log Out" button at the top-right which clears `localStorage` and redirects to login.
5. Setup HTTP client fetch headers to automatically append `Authorization: Bearer <token>` for all API requests.

---

### Step 3: Implement Left Pane — File Uploader & File List

Create the left column (File Management panel):
1. **Drag-and-Drop zone**: Accept only `.md` files, ignoring other extensions. Dragging files onto the zone displays the upload queue.
2. **File input button**: For normal selection upload.
3. Uploading triggers `POST /api/index` multipart/form-data. Upon `202 Accepted` response, show a Toast notification and wait for the compilation to finish.
4. **File Table List**: Fetches files from `GET /api/index/files` showing:
   - File Name
   - File Size (formatted)
   - Status Badge (Green "Indexed" if `isIndexed == true`, Yellow "Queued" otherwise)
   - Upload Date
5. Hook into the `/api/notifications/stream` channel (relay tenant token using `event-source-polyfill`). When the stream announces `IndexCompleted`, trigger a reload of the files list.

---

### Step 4: Implement Right Pane — Grounded conversational Chat UI

Create the right column (Chat panel):
1. Bottom contains text input.
2. Submit sends a `POST /api/chat` request and consumes response tokens using a `ReadableStream` reader.
3. **Markdown Rendering**: Render conversation messages using `markdown-to-jsx`.
4. User messages are shown in plain text, but assistant responses are parsed and rendered as Markdown.
5. Parse citation links like `[refund_policy.md#refund-timeline]` and render them as interactive links. Clicking opens a sidebar drawer displaying the cited content.

---

### Step 5: Implement Persistent Self-Registration

1. Toggle button on login card switches to Sign Up.
2. Submit executes a `POST /api/auth/register` validation call (password length >= 6) and registers user in the Gateway PostgreSQL registry.
3. Redirect back to login card with prefilled username and success Toast on creation.

---

### Step 6: Implement File Deletion & Cache Re-aggregation

1. Display trash/delete icon button on each file row.
2. Confirm delete triggers `DELETE /api/index/{fileName}` request.
3. Reload directory files list on success.

---

## Verification

- [ ] Static files build to `src/CloudKB.Gateway/wwwroot/` upon `npm run build`
- [ ] Login screen works with `tenant-01` / `password` credentials
- [ ] Drag-and-drop rejects `.png` files and accepts `.md` files
- [ ] Left pane displays the file table list of uploaded documents
- [ ] Sending query "How long do refunds take?" displays streaming Markdown tokens with source badges
- [ ] User self-registration persists credentials to PostgreSQL database and allows login
- [ ] File deletion deletes file from S3, DB, and triggers BM25 cache re-aggregation/deletion in Redis
- [ ] Integration tests pass in `IntegrationTests.cs`
