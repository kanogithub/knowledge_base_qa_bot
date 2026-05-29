## 📄 Feature 7: System-Wide Health Check Endpoint

* **Feature Group:** System Reliability & Monitoring Layer
* **Implementation Components:** `CloudKB.Gateway` + All ApiServices (`CloudKB.ApiService.Chat`, `CloudKB.ApiService.Indexing`, `CloudKB.ApiService.Notification`)

### 1. Feature Overview

Provides a lightweight, standardized liveness probe endpoint `/health` exposed via the API Gateway and implemented across all microservices. This allows external monitoring tools, container orchestrators (like Kubernetes, Docker, or .NET Aspire), and health probes to check if the services are up and responding to requests.

### 2. Core Technical Specifications

* **Gateway Routing & Mapping:** The API Gateway exposes a public `GET /health` endpoint that returns a simple liveness response.
* **HTTP Protocol & Payload:**
  - **Method:** `GET`
  - **Path:** `/health`
  - **Response Status:** `200 OK`
  - **Response Headers:** `Content-Type: application/json`
  - **Response Body:**
    ```json
    {
      "status": "ok"
    }
    ```
* **Downstream Integration:** Each individual API microservice implements local health endpoints (using standard ASP.NET Core Health Checks middleware) to report its own status.
