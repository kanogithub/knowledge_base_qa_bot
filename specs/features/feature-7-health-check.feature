@epic-2 @feature-7 @health @gateway @liveness
Feature: System-Wide Health Check Endpoint
  As the API Gateway and downstream services
  I must expose a standardized health check endpoint
  So that monitoring tools and orchestrators can verify liveness

  Scenario: Gateway liveness check succeeds
    When a monitoring tool sends "GET /api/health" to the gateway
    Then the gateway should return HTTP 200
    And the response Content-Type should be "application/json"
    And the response body should be:
      """json
      {
        "status": "ok"
      }
      """

  Scenario: Downstream microservices respond with healthy status
    When a health probe sends "GET /api/health" to the local Indexing Service
    Or a health probe sends "GET /api/health" to the local Chat Service
    Or a health probe sends "GET /api/health" to the local Notification Service
    Then each service should return HTTP 200
    And the response body should contain "status" = "Healthy" or "status" = "ok"
