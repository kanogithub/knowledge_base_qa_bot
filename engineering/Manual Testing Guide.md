# CloudKB Manual Testing Guide

This guide describes how to manually test the API endpoints of the CloudKB knowledge base system, specifically focusing on Markdown file ingestion, real-time notification streaming, audit logging, and QA chat streaming.

## 1. Prerequisites and Setup

1. **Start the Application Host**:
   Open the solution in Visual Studio and set `CloudKB.AppHost` as the startup project, or run it from the command line:
   ```bash
   dotnet run --project src/CloudKB.AppHost/CloudKB.AppHost.csproj
   ```
2. **Access the Aspire Dashboard**:
   Open the Aspire Dashboard in your browser (typically hosted at `http://localhost:15000` or the port output in your terminal).
3. **Locate the Gateway Address**:
   Find the resource named `gateway` on the dashboard. Note its external endpoint address.
   Throughout this document, replace `<GATEWAY_URL>` with this address (e.g., `http://localhost:5274`).

---

## 2. Authentication (JWT Token)

The Gateway enforces JWT Bearer authentication. For testing purposes under tenant ID `tenant-01`, use the following pre-generated token (valid for 1 year, signed with the test secret key):

```text
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VyX2lkIjoidGVuYW50LTAxIiwianRpIjoiYWUwZjRmM2ItMTljNC00MTdjLThiMWYtY2JhYjg2MTIxMGVjIiwibmJmIjoxNzc5OTAxMDE1LCJleHAiOjE4MTE0MzcwMTUsImlhdCI6MTc3OTkwMTAxNSwiaXNzIjoiY2xvdWRrYi1hdXRoIiwiYXVkIjoiY2xvdWRrYi1hcGkifQ.BEshe0-1mLH-FXRmPggMc9ScFMiYWC7fDyBcukTrWBc
```

In all subsequent commands, replace `<TOKEN>` with the above string.

---

## 3. Testing Step-by-Step

### Step A: Subscribe to SSE Notification Stream
Open a new terminal window to monitor indexing progress in real-time. Keep this command running.

* **Request**:
  ```bash
  curl -N -H "Authorization: Bearer <TOKEN>" <GATEWAY_URL>/api/notifications/stream
  ```
* **Expected Initial Output**:
  ```text
  :
  ```
  *(A colon `:` comment is sent immediately to establish the connection and keep it alive)*.

---

### Step B: Upload a Markdown File
Prepare a sample Markdown file and send a `multipart/form-data` request to the ingestion endpoint.

1. **Create a test file `faq.md`**:
   ```markdown
   # Refund Policy
   Refunds are processed within 5 business days.

   # Support Hours
   Support is available 24/7.
   ```
2. **Upload the file**:
   ```bash
   curl -X POST -H "Authorization: Bearer <TOKEN>" \
        -F "files=@faq.md" \
        <GATEWAY_URL>/api/index
   ```
* **Expected Response (202 Accepted)**:
  ```json
  {
    "taskId": "a97a8c4f-3765-4f32-8400-b6f1947bde93",
    "message": "Knowledge compilation job enqueued."
  }
  ```
* **Observe Notifications**:
  Check your running SSE stream terminal (Step A). You should see real-time updates as the indexer processes the file:
  ```json
  data: {"eventType":"IndexProcessing","taskId":"a97a8c4f-3765-4f32-8400-b6f1947bde93","message":"Compilation started"}

  data: {"eventType":"IndexProcessing","taskId":"a97a8c4f-3765-4f32-8400-b6f1947bde93","message":"Compilation completed successfully"}
  ```

---

### Step C: Verify Audit Logs
Verify that the ingestion changes were written into the database audit trail.

* **Request**:
  ```bash
  curl -H "Authorization: Bearer <TOKEN>" <GATEWAY_URL>/api/notifications/logs
  ```
* **Expected Response (200 OK)**:
  ```json
  [
    {
      "id": "76ea4922-83b6-455b-9c29-3733cd15cc5a",
      "fileName": "faq.md",
      "actionType": "ADDED",
      "sectionsAffected": 2,
      "commitMessage": "Added 'Refund Policy', 'Support Hours'",
      "loggedAt": "2026-05-28T03:00:00Z"
    }
  ]
  ```

---

### Step D: Ask Questions (Grounded QA Chat)
Once notifications report compilation is complete, you can query the grounded knowledge base.

* **Request**:
  ```bash
  curl -N -X POST -H "Authorization: Bearer <TOKEN>" \
       -H "Content-Type: application/json" \
       -d '{"query": "How long do refunds take?"}' \
       <GATEWAY_URL>/api/chat
  ```
* **Expected Response (text/event-stream)**:
  The response streams back citations (sources) followed by incremental text chunks from the configured LLM client:
  ```text
  data: {"sources":[{"fileName":"faq.md","heading":"Refund Policy"}],"content":"","isFinal":false}

  data: {"sources":[],"content":"Refunds ","isFinal":false}

  data: {"sources":[],"content":"are ","isFinal":false}

  data: {"sources":[],"content":"processed ","isFinal":false}

  data: {"sources":[],"content":"within ","isFinal":false}

  data: {"sources":[],"content":"5 ","isFinal":false}

  data: {"sources":[],"content":"business ","isFinal":false}

  data: {"sources":[],"content":"days.","isFinal":false}

  data: {"sources":[],"content":"","isFinal":true}
  ```

#### Grounding & Rejection Verification
To test query filtering/refusal when asking questions unrelated to your knowledge base:
* **Request**:
  ```bash
  curl -N -X POST -H "Authorization: Bearer <TOKEN>" \
       -H "Content-Type: application/json" \
       -d '{"query": "What is the capital of France?"}' \
       <GATEWAY_URL>/api/chat
  ```
* **Expected Response**:
  The system detects weak query-to-document correlation and exits early with a refusal message without calling the LLM:
  ```text
  data: {"sources":[],"content":"無法從現有的知識庫中確認，請重新提問。","isFinal":true}
  ```
