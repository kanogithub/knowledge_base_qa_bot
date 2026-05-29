# Spec-Driven Development (SDD) Standard Operating Procedure (SOP)

This document outlines the end-to-end workflow for **Spec-Driven Development (SDD)**, focusing on the seamless collaboration between **Human Developers** and **AI Agents**. It covers the lifecycle from requirements analysis to Proof of Concept (POC) delivery, heavily emphasizing state persistence, automated code generation, and Human-in-the-Loop (HITL) safety nets.

---

## The SDD Philosophy & AI Collaboration

```
  [ Human Vision ] ──(Iterative Q&A & Discovery)──> [ Conceptual Intent (engineering/) ]
                                                                 │
                                                       (Specs Derivation)
                                                                 ▼
  [ External Brain (Checklists & Artifacts) ] <──> [ Technical Contracts (specs/) ]
                                                                 │
                                                       (Drift Checks & Code-Gen)
                                                                 ▼
  [ Verification (CI/CD) ] <────────────────────── [ Implementation Logic (src/) ]
```

1. **Specs are the Source of Truth**: Code, clients, and test runners are derived from specifications under `specs/`.
2. **Context Persistence (The External Brain)**: AI context windows are finite. The master checklist (`specs/tasks/sdd_checklist.md`) acts as the resilient state tracker. When a session restarts, the Agent reads this checklist to resume work.
3. **Artifact-Driven Communication**: Agents communicate via high-signal Markdown artifacts (`implementation_plan.md`, `walkthrough.md`) rather than noisy chat logs.
4. **Fail Fast at Design-Time**: Finding inconsistencies in YAML schemas is cheap. Fixing architectural drift in code is expensive.

---

## Pre-Phase 0: Discovery & Context Bootstrapping

Crucial for brownfield (existing) projects or resuming work after a context reset.

### Step-by-Step Workflow
1. **Context Initialization**:
   * **Human** prompts the Agent: *"Resume work based on `specs/tasks/sdd_checklist.md`"* or *"Analyze the current workspace for a new feature."*
2. **Codebase Research**:
   * **Agent** spawns isolated `research` subagents to scan existing directories, read `.csproj`/`package.json` files, and understand the current topology without cluttering the main Agent's context window.
3. **Alignment**:
   * **Human & Agent** engage in Q&A to resolve ambiguities regarding the existing architecture before proposing changes.

---

## Phase 1: Conceptual Design & Requirements Decomposition

Define **what** the system does and **why** before writing technical contracts.

### Step-by-Step Workflow
1. **Requirements Intake**:
   * **Human** provides product requirements and desired user flows.
2. **Architecture Blueprinting**:
   * **Agent** drafts `PROJECT.md` outlining system architecture, microservice topology diagrams (using Mermaid), and external service dependencies.
3. **Epic & Feature Decomposition**:
   * **Agent** breaks down requirements into core Epics (under `engineering/Epics/`).
   * **Agent** creates Feature files (`Feature X.md`) defining user stories and verification indicators.
4. **Implementation Plan Formulation**:
   * **Agent** generates an `implementation_plan.md` artifact summarizing the proposed architecture and explicitly highlighting any open questions or breaking changes.

> [!IMPORTANT]
> **HITL GATEWAY #1 (Conceptual Approval)**
> The Human Developer must review the `implementation_plan.md` and conceptual documents. AI Agents MUST pause execution until the human approves the plan.

---

## Phase 2: Technical Specifications & Acceptance Scenarios

Translate human-readable intent into machine-readable specs under `specs/`.

### Step-by-Step Workflow
1. **Schema Authoring**:
   * **Agent** writes/extends API contracts (`openapi.yaml`, `asyncapi.yaml`, `db-schema.yaml`).
2. **BDD Acceptance Scenarios**:
   * **Agent** writes Gherkin feature files (`specs/features/feature-X.feature`) detailing exact inputs and expected behaviors.
3. **Task & Checklist Generation**:
   * **Agent** generates step-by-step Task files (`specs/tasks/milestone-*/task-X.md`).
   * **Agent** registers tasks in the master state tracker `specs/tasks/sdd_checklist.md`.

> [!TIP]
> **Spec-First Mocking**
> Immediately spin up mock API services (e.g. Prism for OpenAPI) based on the specs. This allows Frontend Agents and Backend Agents to work concurrently.

> [!IMPORTANT]
> **HITL GATEWAY #2 (Contract Verification)**
> Human Developer reviews OpenAPI endpoints, AsyncAPI payloads, and BDD scenario coverage. Specs must be syntactically valid and represent the approved design.

---

## Phase 3: Automated Code Generation & Stubbing

Eliminate boilerplate using tooling.

### Step-by-Step Workflow
1. **Server & Client Stubbing**:
   * **Agent** or **CI Pipeline** runs schema generators (e.g., OpenAPI to ASP.NET Minimal APIs, `openapi-typescript` for React clients).
2. **Database Migrations**:
   * **Agent** scaffolds Entity Framework (or SQL) migrations based on `db-schema.yaml`.
3. **Test Scaffolding**:
   * **Agent** generates spec execution test classes (e.g., SpecFlow step definitions) from the `.feature` files.

---

## Phase 4: Agent Execution & Implementation

Implement core business logic step-by-step using the task guides.

### Step-by-Step Workflow
1. **State Update**:
   * **Agent** updates `specs/tasks/sdd_checklist.md` to mark the current task as `[-] In Progress`.
2. **Incremental Execution**:
   * **Agent** follows the specific task file step-by-step.
   * **Subagent Delegation**: For complex or isolated sub-tasks (e.g., writing a complex SQL query or a tricky CSS layout), the main Agent delegates work to a specialized subagent.
3. **Strict Local Testing**:
   * **Agent** executes unit and local integration tests after each step to prevent regression.
4. **Completion Summary**:
   * **Agent** marks the task as `[x] Completed` in the checklist.
   * **Agent** generates a `walkthrough.md` artifact visually or textually summarizing what was built, complete with test results or UI screenshots.

---

## Phase 5: Verification, Drift Detection & Feedback Loop

Validate code against specifications and enforce quality gates.

```
       [ Run BDD Tests ]
               │
       ┌───────┴───────┐
       ▼               ▼
    [ PASS ]        [ FAIL ]
       │               │
  [ Push Code ]        ▼
                 [ Check Drift ] ───(Drift Detected?)
                       │                      │
                       ▼ YES                  ▼ NO
             [ Align Specs First ]    [ Fix Logic Code ]
```

### Step-by-Step Workflow
1. **Automated BDD Execution**:
   * **Agent** runs the full spec verification test suites (`dotnet test`).
2. **Spec Drift Gatekeeping**:
   * CI builds execute schema compatibility checks (e.g., `dotnet ef migrations has-pending-model-changes`). If the code deviates from the specs, **the build fails**.
3. **Self-Healing Loop**:
   * If a verification check fails, the **Agent** reviews the test logs.
   * **CRITICAL RULE**: If the spec was fundamentally incorrect, the Agent **modifies the spec file first**, updates generated files, and then updates the logic. *Never patch code directly to bypass a spec.*

> [!IMPORTANT]
> **HITL GATEWAY #3 (POC Acceptance & UAT)**
> The Human Developer reviews the `walkthrough.md`, performs manual verification on UI dashboards, checks telemetry tracing, and signs off on the completed milestone.

---

## Human-AI Roles & Artifact Matrix

| Phase | Core Activity | AI Agent Responsibility | Human Responsibility | Key Artifacts |
| :--- | :--- | :--- | :--- | :--- |
| **0. Discovery** | Context Loading | Spawn research subagents; read master checklist. | Guide Agent to entry points. | `sdd_checklist.md` |
| **1. Design** | Architecture | Draft topology; Break down Epics/Features; Propose plan. | Clarify business rules; Approve Plan. | `PROJECT.md`, `implementation_plan.md` |
| **2. Contracts** | Specifications | Write YAML schemas, Gherkin features, Task markdown files. | Review API contracts & edge cases. | `openapi.yaml`, `features/*.feature` |
| **3. Code-Gen** | Scaffolding | Run CLI generators; setup mock servers. | Review generated stubs. | Auto-generated DTOs / Migrations |
| **4. Coding** | Implementation | Execute tasks; Delegate to subagents; Write logic. | Monitor progress. | Source code, `walkthrough.md` |
| **5. Verification**| QA & Self-Healing | Run tests; Fix drift by updating specs first. | Final UAT sign-off. | Test Logs, CI Status |
