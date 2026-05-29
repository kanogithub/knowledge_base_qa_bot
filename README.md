# Project Cloud-KB: Distributed Multi-Tenant Knowledge Base System

Project Cloud-KB is a cloud-native, distributed RAG (Retrieval-Augmented Generation) knowledge base QA assistant. Built on the **.NET 10** and **.NET Aspire 10** ecosystem, it features an asynchronous knowledge compilation pipeline (using PostgreSQL, Redis, RabbitMQ, and MinIO) and a modern React grounded chatbot that presents BM25 retrieval scores on cited sources.

---

## Prerequisites (From Scratch)

You must install the following tools before running this project.

### 1. Docker Desktop
This project leverages **.NET Aspire** to orchestrate dependencies (Postgres, Redis, RabbitMQ, and MinIO). Aspire requires a container runtime.
* **Windows**: Download and install [Docker Desktop for Windows](https://www.docker.com/products/docker-desktop/). Ensure WSL 2 backend is enabled.
* **macOS**: Download and install [Docker Desktop for Mac](https://www.docker.com/products/docker-desktop/). Select either the Apple Silicon or Intel chip version based on your Mac hardware.
* *Note: Ensure Docker is running in the background.*

### 2. .NET 10 SDK & Aspire Workload
* Download and install the [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
* Once installed, open your terminal (PowerShell/CMD on Windows, Terminal on Mac) and run the following command to install the Aspire workloads:
  ```bash
  dotnet workload install aspire
  ```

### 3. Node.js (v18+) & npm
The frontend is built using React with Vite.
* **Windows**: Install via the [Node.js Installer](https://nodejs.org/) or [nvm-windows](https://github.com/coreybutler/nvm-windows).
* **macOS**: Install via the [Node.js Installer](https://nodejs.org/), Homebrew (`brew install node`), or [nvm](https://github.com/nvm-sh/nvm).

---

## Configuration Guide

Before running the project, you need to configure the AI API Key and optional BM25 search parameters.

### 1. LLM API Key Configuration (OpenAI or Gemini)
The RAG Chat engine uses Gemini or OpenAI to generate answers. The priority of providers is set under `LlmProviders:Priority` inside `src/CloudKB.ApiService.Chat/appsettings.json`.

You can set your API Key in **two ways**:

#### Method A: Using .NET User Secrets (Recommended & Safest)
Using User Secrets keeps your keys safely stored outside the Git repository. Open your terminal at the project root and run:
* **For Gemini (Default)**:
  ```bash
  dotnet user-secrets set "LlmProviders:Gemini:ApiKey" "YOUR_GEMINI_API_KEY" --project src/CloudKB.ApiService.Chat
  ```
* **For OpenAI**:
  ```bash
  dotnet user-secrets set "LlmProviders:OpenAI:ApiKey" "YOUR_OPENAI_API_KEY" --project src/CloudKB.ApiService.Chat
  ```

#### Method B: Modifying `appsettings.json`
Directly open [appsettings.json](file:///c:/[Other%20Learning%2520Sources]/[build-moat]/live-sessions-reflection/knowledge_base_qa_bot/src/CloudKB.ApiService.Chat/appsettings.json) and enter your key:
```json
  "LlmProviders": {
    "Priority": [ "Gemini", "OpenAI" ],
    "Gemini": {
      "ApiKey": "YOUR_GEMINI_API_KEY",
      "ModelName": "gemini-2.5-flash",
      "Endpoint": "https://generativelanguage.googleapis.com/v1beta/openai/"
    },
    "OpenAI": {
      "ApiKey": "YOUR_OPENAI_API_KEY",
      "ModelName": "gpt-4o-mini",
      "Endpoint": "https://api.openai.com/v1/"
    }
  }
```

---

### 2. BM25 & Retrieval Parameters Configuration
You can customize the BM25 scoring behavior inside the `"BM25"` section of `src/CloudKB.ApiService.Chat/appsettings.json` (or override them using User Secrets).

* **`RetrievalScoreThreshold`** (Default: `0.5`):
  * **Critical Setting**: If the calculated BM25 relevance score for all indexed document sections falls below this threshold, the Chat engine exits early and replies: *"我無法從現有的知識庫中確認此訊息。"* (I cannot confirm this information from the existing knowledge base).
  * *Tip: Set this to `0.0` during testing if you want to force grounding references regardless of similarity scores.*
* **`K1`** (Default: `1.2`) & **`B`** (Default: `0.75`): Standard BM25 term frequency saturation and document length normalization scaling factors.
* **`HeadingBoost`** (Default: `1.5`): A multiplier applied to the score when search terms match document header titles (headings).
* **`TopK`** (Default: `3`): The maximum number of highest-scoring relevant sections passed to the LLM context.

---

## Getting Started (Development Mode)

### Step 1: Install Frontend Dependencies & Build
Before launching the services, you must restore the frontend packages and compile the React application so that the Gateway API Service can serve them.

1. Open your terminal and navigate to the React project directory:
   ```bash
   cd src/CloudKB.Web
   ```
2. Install npm dependencies:
   ```bash
   npm install
   ```
3. Compile the production assets (which outputs directly to the API Gateway's `wwwroot/` static file folder):
   ```bash
   npm run build
   ```

### Step 2: Run the Project (Using .NET Aspire Orchestration)
You do **not** need to manually download, configure, or run Postgres, Redis, RabbitMQ, or MinIO. .NET Aspire will automatically download container images, start them on Docker, hook up environment settings/connection strings, and launch all backend services.

1. Navigate back to the workspace root directory.
2. Run the orchestrator AppHost project:
   * **Windows / macOS**:
     ```bash
     dotnet run --project src/CloudKB.AppHost
     ```
3. Once running, look at the terminal output. It will provide a link to the **Aspire Dashboard** (e.g. `http://localhost:17222/` or similar).

### Step 3: Access and Explore
1. Open the Aspire Dashboard URL in your web browser.
2. The Dashboard lists all running services:
   * **apiservice-chat**: RAG Chat engine.
   * **apiservice-indexing**: Document processing and Redis BM25 engine.
   * **apiservice-notification**: SSE Notification streamer.
   * **gateway**: The API Gateway proxying routes to services and serving the React web console.
   * **worker-indexer**: Background queue listener.
3. Click the **gateway** endpoint link (e.g., `http://localhost:5000` or the random https port shown in the Dashboard).
4. You will see the **Cloud-KB Portal** login screen.

---

## How to Test and Interact

### 1. User Registration & Login
* You can sign in using a built-in mock account:
  * **Username / Tenant ID**: `tenant-01`
  * **Password**: `password`
* Alternatively, click **Create Tenant Account** to register a new tenant workspace and login.

### 2. Upload Files
* Drag and drop or select `.md` markdown files.
* You will see the file status progress through **Queued** $\rightarrow$ **Indexing** $\rightarrow$ **Indexed** in real-time, backed by SSE notification updates.
* You can click the **Trash icon** to delete files, purging them from DB, S3 Storage, and triggering a Redis BM25 re-compilation.

### 3. Ask Questions (RAG Chat)
* Ask questions based on the uploaded files.
* The chatbot will stream answers token-by-token.
* **Citations & Scores**:
  * Cited segments will show inline citation links (e.g., `[1]`, `[2]`).
  * Under the message, a list of **Sources** buttons displays each document heading alongside its **BM25 Retrieval Score** (e.g., `(Score: 3.52)`).
  * Click on any inline citation or source button to slide up the details drawer displaying the `BM25 Retrieval Score` to four decimal places.
