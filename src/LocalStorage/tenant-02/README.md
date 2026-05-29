# engineering/ — Project Design & Architecture Hub

This directory is the **high-level design authority** for the Cloud-KB project. It contains all human-readable architectural concepts, epic breakdowns, and feature specifications that define **what** the system does and **why**.

> **SDD Context:** In our [Spec-Driven Development](../specs/README.md) workflow, `engineering/` defines the **conceptual design layer** (human-readable), while `specs/` defines the **machine-readable contract layer** (OpenAPI, AsyncAPI, DB Schema, BDD). Code is generated from `specs/`, but `specs/` is derived from the intent documented here.

---

## Directory Structure

```
engineering/
├── README.md                      # ← You are here
├── SDD_SOP.md                     # Spec-Driven Development Workflow Guide
├── PROJECT.md                     # System-level architecture & tech stack
├── Manual Testing Guide.md        # Manual verification instructions
│
└── Epics/
    ├── Epic 1/
    │   ├── Epic 1 - The Write Pipeline.md    # Epic overview & goals
    │   └── Features/
    │       ├── Feature 1.md       # Multi-tenant gateway auth
    │       ├── Feature 2.md       # Async file ingestion (fire-and-forget)
    │       ├── Feature 3.md       # Markdown knowledge compilation (BM25)
    │       └── Feature 4.md       # Redis Pub/Sub notification SSE
    │
    ├── Epic 2/
    │   ├── Epic 2 - The Read Pipeline.md     # Epic overview & goals
    │   └── Features/
    │       ├── Feature 5.md       # Chat gateway routing & HTTP/2
    │       ├── Feature 6.md       # In-memory BM25 retrieval & short-lived SSE
    │       └── Feature 7.md       # System-wide health check endpoint
    │
    └── Epic 3/
        ├── Epic 3 - Front-End UI.md          # Epic overview & goals
        └── Features/
            └── Feature 8.md       # Integrated Dashboard & Grounded Chat UI
```

---

## Document Purpose Map

### SDD_SOP.md — The Workflow Standard

The standard operating procedure for Spec-Driven Development, detailing the phase-by-phase workflow for Human-AI collaboration, context management, and drift detection.

### PROJECT.md — System Architecture Blueprint

The top-level design document. Defines:

- **System architecture** — Mermaid diagrams showing the full microservice topology (Gateway → Indexing API → RabbitMQ → Worker → Redis → Chat Service)
- **Core data models** — `TenantSection`, `NotificationEvent`, `ChatStreamChunk` record definitions
- **Distributed workflows** — Sequence diagrams for the write pipeline (async compilation + SSE notification) and read pipeline (BM25 retrieval + grounded SSE streaming)
- **Tech stack standard** — .NET Aspire 10, Yarp, EF Core 10, Redis, RabbitMQ, React + Vite

### Manual Testing Guide.md — Verification Instructions

Contains step-by-step cURL commands and WebSocket/SSE client usage guidelines to test the system manually end-to-end.

### Epics/ — Feature Decomposition

The project is split into **3 Epics** covering the write, read, and presentation paths of the system:

| Epic | Name | Focus | Features |
|------|------|-------|----------|
| **Epic 1** | The Write Pipeline | Ingest, compile, and notify | F1 – F4 |
| **Epic 2** | The Read Pipeline | Retrieve, ground, and stream | F5 – F7 |
| **Epic 3** | Front-End UI | Client interface & streaming Chat UI | F8 |

Each Epic directory contains:
- An **Epic overview** document describing the core goal and scope
- A **Features/** subdirectory with individual feature specs, each defining:
  - Functional description
  - Implementation component mapping
  - Core technical specifications and acceptance indicators

### docs/ — Sample Knowledge Base

Three Markdown files (`account_help.md`, `refund_policy.md`, `shipping_faq.md`) located in the root [docs/](../docs/) directory serve as the canonical test corpus. These files are used throughout:

- **Development:** Local `POST /api/index` testing
- **BDD Specs:** Grounded Q&A scenario expectations (e.g., "How long do refunds take?" → cites `refund_policy.md#refund-timeline`)
- **Integration Tests:** End-to-end pipeline validation

---

## Recommended Reading Order

```
1. SDD_SOP.md                         → Understand the Human-AI development workflow
2. PROJECT.md                         → Understand the full system vision
3. Epic 1 - The Write Pipeline.md     → Understand async ingestion flow
4. Features 1 → 4                     → Drill into write pipeline details
5. Epic 2 - The Read Pipeline.md      → Understand retrieval & QA flow
6. Features 5 → 7                     → Drill into read pipeline details
7. Epic 3 - Front-End UI.md           → Understand the frontend interface spec
8. Feature 8                          → Drill into frontend dashboard and chat UI details
9. ../specs/README.md                 → See how designs become machine-readable contracts
```

---

## Relationship to Other Project Directories

| Directory | Role | Driven By |
|-----------|------|-----------|
| `engineering/` | **Conceptual design** — what & why | Human intent |
| `docs/` | **Sample knowledge base** — test files for indexing | Dev/test data |
| `specs/` | **Machine-readable contracts** — how (precisely) | Derived from `engineering/` |
| `scaffold/` | **Code scaffolding** — starter templates | Generated from `specs/` |
