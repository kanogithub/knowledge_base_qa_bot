# Project Cloud-KB: Distributed Multi-Tenant Markdown Knowledge Base System (.NET 10 Enterprise)

This project is a cloud-native, distributed, multi-tenant AI retrieval and question-answering system (RAG) built on the **.NET 10 / .NET Aspire 10** ecosystem. The architecture implements Andrej Karpathy's **LLM Wiki Pattern (compile knowledge at write-time, explicit index navigation)**. It adopts native **Server-Sent Events (SSE) streaming**, decoupled via **RabbitMQ task queues** and **Redis Pub/Sub** to achieve high-throughput, low-latency multi-tenant knowledge compilation and grounded QA microservices.

---

## 1. High-Level Design Concept (HLD)

Before examining the microservice topology, below is the conceptual diagram of the asynchronous task processing architecture:

```mermaid
graph LR
    %% Node style definitions
    classDef default fill:#f8f9fa,stroke:#ced4da,stroke-width:1px;
    classDef component fill:#ffffff,stroke:#adb5bd,stroke-width:2px;
    classDef storage fill:#ffffff,stroke:#adb5bd,stroke-width:2px;

    %% Node Declarations
    Client["Client"]:::component
    Gateway["API Gateway"]:::component
    
    %% Ingestion / Write Path
    IndexingServer["Indexing Server"]:::component
    Queue[("Request Queue")]:::storage
    
    %% Chat / Read Path
    ChatServer["Chat Server"]:::component
    LLM["LLM Service"]:::component
    
    %% Notification Hub
    NotificationHub["Notification Hub<br>(SSE Relay)"]:::component
    
    %% Shared Storage / Cache
    ResultStore[("Result Store<br>(Index Cache & Pub/Sub)")]:::storage
    DocDB[("Document DB<br>(PostgreSQL)")]:::storage
    
    subgraph Workers ["Background Worker Cluster"]
        Worker["Worker"]:::component
    end

    %% Data Flow Connections (Write Path)
    Client <--> Gateway
    Gateway <--> IndexingServer
    IndexingServer --> Queue
    Queue --> Worker
    Worker --> ResultStore
    Worker --> DocDB
    
    %% Notification Loop (Real-time Feedback)
    Worker -->|"Publish Completed"| ResultStore
    ResultStore -->|"Subscribe"| NotificationHub
    NotificationHub -->|"Relay SSE"| Gateway
    Gateway -->|"Stream"| Client
    
    %% Data Flow Connections (Read Path)
    Gateway <--> ChatServer
    ChatServer <--> |"Load Index"| ResultStore
    ChatServer <--> |"Fetch Sections"| DocDB
    ChatServer <--> |"Generate QA"| LLM

    %% Adjust styling
    style Workers fill:none,stroke:#e9ecef,stroke-dasharray: 5 5
```

---

## 2. Microservice Architecture (System Architecture)

This project uses **.NET Aspire 10** as the distributed application orchestrator (App Host) to coordinate services, telemetry, and connection strings. The microservice topology is detailed below:

```mermaid
---
config:
  layout: elk
---
graph TB
    subgraph Frontend_Layer ["Frontend User Layer"]
        React["React Frontend<br>Native EventSource API"]
    end

    subgraph Gateway_Layer ["Gateway & Routing Layer"]
        Gateway["Yarp API Gateway<br>HTTP Stream Proxy / HTTP/2"]
    end

    subgraph Compute_Layer ["Distributed Microservices Layer"]
        IndexingAPI["KB Indexing API<br>Fast Ingest / S3 Stream"]
        IndexWorker["KB Index Worker<br>Background Services"]
        ChatSvc["Chat QA Service<br>Short-Lived SSE Stream"]
        NotificationSvc["Notification SSE Service<br>Long-Lived SSE Stream"]
    end

    subgraph Storage_Cache_Layer ["Storage & Caching Layer"]
        Queue["RabbitMQ<br>Task Queue"]
        Redis[("Redis Cluster<br>Pub/Sub & Index Cache & Lock")]
        Postgres[("PostgreSQL / EF 10<br>Structured Metadata")]
        S3[("AWS S3 / MinIO<br>Raw Markdown")]
    end
    
    React <-->|"1. HTTP / text/event-stream"| Gateway
    Gateway -->|"GET /api/notifications/stream"| NotificationSvc
    Gateway -->|"POST /api/chat"| ChatSvc
    Gateway -->|"POST /api/index"| IndexingAPI
    IndexingAPI -->|"2a. Stream Write MD"| S3
    IndexingAPI -->|"2b. Push IngestTask Message"| Queue
    IndexingAPI -.->|"2c. HTTP 202 Accepted"| Gateway
    Queue -->|"3. Subscribe & Consume"| IndexWorker
    IndexWorker -->|"4a. Acquire Lock"| Redis
    IndexWorker -->|"4b. Batch Insert Metadata"| Postgres
    IndexWorker -->|"4c. Update & Refresh Cache"| Redis
    IndexWorker -->|"4d. Release Lock"| Redis
    IndexWorker -->|"5. Publish 'IndexUpdated'"| Redis
    Redis -->|"6. Subscribe & Catch Event"| NotificationSvc
    NotificationSvc -.->|"7. Push SSE Event"| Gateway
    Gateway -.->|"8. EventSource.onmessage"| React
    ChatSvc -->|"O(1) Fast Load Index"| Redis
    ChatSvc -->|"Fetch Top-K Sections"| Postgres
    ChatSvc <-->|"Microsoft.Extensions.AI"| OpenAI["OpenAI / LLM API"]
```

---

## 3. Component Walkthrough (.NET Aspire 10 Ecosystem)

### 3.1 Infrastructure & Orchestration (AppHost)

* **`CloudKB.AppHost`**: The central orchestrator. Configures service discovery, injects environment variables, manages container dependencies (Redis, PostgreSQL, RabbitMQ, MinIO), and establishes named data volumes to achieve persistence.
* **`CloudKB.ServiceDefaults`**: Configures global OpenTelemetry (Metrics, Tracing, Logging), health checks, and .NET 10 resiliency policies (such as Polly circuit breakers and retry logic).

### 3.2 Microservice Compute Layer

* **`CloudKB.Gateway` (Yarp)**: The反向代理 (reverse proxy) gateway. Validates JWT tokens and injects user identity as an `X-User-Id` header to downstream services. It supports HTTP/2 and HTTP/3 multiplexing, allowing multiple client-side SSE streams to share a single TCP connection.
* **`CloudKB.ApiService.Indexing`**: A lightweight file ingestion controller. Receives Markdown files, streams them to MinIO/S3 storage, pushes a tasks payload to RabbitMQ, and immediately responds with `202 Accepted` to free incoming threads.
* **`CloudKB.Worker.Indexer`**: A background indexing worker (`BackgroundService`). Asynchronously consumes RabbitMQ tasks, parses Markdown headings, calculates BM25 term frequencies, and updates Postgres and Redis under a distributed lock (`lock:index:{tenantId}`).
* **`CloudKB.ApiService.Notification`**: The **Global Long-Lived Notification Service**. Maintains a persistent SSE connection (`GET /api/notifications/stream`) with logged-in clients. It subscribes to Redis Pub/Sub channels and streams index compilation progress alerts to the client.
* **`CloudKB.ApiService.Chat`**: The **Short-Lived Chat Service**. Handles RAG QA requests. It fetches the lightweight explicit index from Redis to calculate C# In-Memory BM25 scores. If query correlation passes the threshold, it retrieves the section texts from Postgres, compiles a grounded prompt, streams typewriter tokens using `Microsoft.Extensions.AI` and `IAsyncEnumerable<T>`, and closes the HTTP connection once final citations are sent.

### 3.3 Storage & Caching Layer

* **Blob Storage (MinIO / S3)**: Stores raw Markdown files under the path structure `/{user_id}/raw/*.md`.
* **PostgreSQL (Entity Framework Core 10)**: Stores structured metadata, including `TenantSection` text fragments, heading breadcrumbs, file states, and indexing logs.
* **Redis Cluster**:
  - **`kb:index:{user_id}`**: Caches compiled tenant BM25 explicit indexes (JSON format). If a cache miss occurs, the Chat service automatically reloads records from Postgres to restore this cache.
  - **`lock:index:{user_id}`**: A distributed lock preventing write race-conditions during concurrent uploads.
  - **Pub/Sub Channel (`ch:notifications:{user_id}`)**: Message broker channel relaying indexing progress and completion events from workers to notification services.

---

## 4. Core Data Models

### 4.1 Structured Section Entity (PostgreSQL / EF 10)

```csharp
public class TenantSection
{
    public string Id { get; set; } = null!; // Composite PK: {user_id}#{filename}#{slugified-heading}
    public string TenantId { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string Heading { get; set; } = null!;
    public List<string> HeadingPath { get; set; } = new();
    public string Content { get; set; } = null!;
    public List<string> Tokens { get; set; } = new();
    public int TokenCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

### 4.2 SSE Event Contract Formats (JSON Payloads)

```csharp
// Global notification event channel (GET /api/notifications/stream)
public record NotificationEvent(
    string EventType,        // e.g., "IndexProcessing", "IndexCompleted"
    string TaskId,           // Task ID
    string Message,          // Alert message
    object? Metadata         // Additional metadata (e.g. sectionsCompiled count)
);

// Chat streaming event channel (POST /api/chat)
public record ChatStreamChunk(
    string Text,             // Generated token text
    bool IsFinal,            // True if this is the final chunk
    List<SourceCitation>? Sources // Citation list when IsFinal == true
);
```

---

## 5. Distributed Operations Workflows

### 5.1 Async Ingestion & Global SSE Notification Stream

```mermaid
sequenceDiagram
    autonumber
    actor User as Client (React Web)
    participant GW as API Gateway (Yarp)
    participant NOTI as Notification SSE Svc
    participant IDX as Indexing API
    participant MQ as RabbitMQ Queue
    participant Worker as Index Worker
    participant Redis as Redis Pub/Sub
    participant DB as PostgreSQL (EF 10)

    %% Establish stream on login
    User->>GW: 1. GET /api/notifications/stream (Listen for global events)
    GW->>NOTI: Forward Connection (Keep connection alive, send keep-alive comment every 30s)
    NOTI->>Redis: 2. Subscribe (SUBSCRIBE ch:notifications:tenant-01)
    
    %% Ingest request
    User->>GW: 3. POST /api/index (Upload Markdown files)
    GW->>IDX: Forward Request (Includes X-User-Id Header)
    Note over IDX: Stream write raw files to MinIO/S3
    IDX->>MQ: 4. Enqueue IngestTask (CompileKnowledgeTask)
    IDX-->>User: 5. Respond HTTP 202 Accepted (Returns TaskId)
    
    %% Async Processing & notifications
    MQ->>Worker: 6. Consume task message
    Worker->>Redis: 7. Acquire lock (lock:index:tenant-01)
    
    Worker->>Redis: 8. Publish event (PUBLISH ch:notifications:tenant-01 'Processing')
    Redis-->>NOTI: 9. Trigger subscriber event
    NOTI-->>User: 10. 【SSE Stream Relay】 UI displays "Compiling markdown sections..."
    
    Note over Worker: Execute Markdown parsing & BM25 metrics aggregation
    Worker->>DB: 11. Batch insert TenantSections & AuditLogs
    Worker->>Redis: 12. Refresh explicit index cache (kb:index:tenant-01)
    Worker->>Redis: 13. Release lock
    
    Worker->>Redis: 14. Publish event (PUBLISH ch:notifications:tenant-01 'IndexCompleted')
    Redis-->>NOTI: 15. Trigger subscriber event
    NOTI-->>User: 16. 【SSE Stream Relay】 UI Toast: "Knowledge base compiled successfully!"
```

### 5.2 Grounded QA Chat & Short-Lived SSE Responses

```mermaid
sequenceDiagram
    autonumber
    actor User as Client (React Web)
    participant GW as API Gateway (Yarp)
    participant Chat as Chat QA Service
    participant Redis as Redis Cache
    participant DB as PostgreSQL (EF 10)
    participant LLM as OpenAI / Gemini (Microsoft.Extensions.AI)

    User->>GW: 1. POST /api/chat (Query: "How long do refunds take?")
    GW->>Chat: Forward Request (Includes X-User-Id Header)
    
    Chat->>Redis: 2. Read explicit index cache (kb:index:tenant-01)
    Redis-->>Chat: Return TenantKbIndex (JSON)
    
    Note over Chat: Run C# In-Memory BM25 matching.<br/>If score falls below threshold, exit early & refuse.
    
    Chat->>DB: 3. Query Raw Content for Top-K Section IDs
    DB-->>Chat: Return matching sections text
    
    Note over Chat: Format Grounded System Prompt
    Chat->>LLM: 4. Stream response (Stream = true)
    
    LLM-->>Chat: 5. Yield token ("Refunds")
    Chat-->>User: 6. 【SSE Stream Relay】 chunk: "Refunds"
    LLM-->>Chat: 7. Yield token (" take")
    Chat-->>User: 8. 【SSE Stream Relay】 chunk: " take"
    
    Note over Chat: Loop until LLM completes generation
    Chat-->>User: 9. Yield Final Chunk (Contains citations metadata list)
    Note over Chat: 【Connection Closed】 Disconnect HTTP stream, free request thread
```

---

## 6. Technology Stack Selection

* **Orchestration & Service Mesh**: .NET Aspire 10 (Service discovery, distributed tracing, OpenTelemetry dashboard)
* **Reverse Proxy Gateway**: Yarp Reverse Proxy (Multiplexed HTTP/2 streaming proxy)
* **Backend Framework**: ASP.NET Core 10 (Minimal APIs, Worker Service, Native AOT compatible)
* **Real-time Streaming**: Server-Sent Events (SSE), C# `IAsyncEnumerable<T>`
* **Task Queues**: RabbitMQ (Asynchronous background task runner decoupled via events)
* **Caching & Broker Middleware**: StackExchange.Redis (Index key-value cache, distributed locking, Pub/Sub channels)
* **AI Unified Abstraction**: Microsoft.Extensions.AI
* **Database & ORM**: Entity Framework Core 10 (PostgreSQL database provider, Bulk operations optimized)
* **Frontend Web Application**: React (Vite + TypeScript), native browser `EventSource` API, Tailwind CSS
