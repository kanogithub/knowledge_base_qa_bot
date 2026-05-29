@epic-1 @feature-1 @gateway @security
Feature: Multi-Tenant Identity and Security Boundary Governance
  As the Yarp API Gateway
  I must validate JWT tokens and inject tenant context
  So that all downstream services operate in strict tenant isolation

  Background:
    Given the API Gateway is running with JWT validation enabled
    And HTTP/2 protocol is enforced on the Kestrel endpoint

  # ─── JWT Validation ─────────────────────────────────────

  Scenario: Valid JWT token passes gateway authentication
    Given a user "tenant-01" has a valid JWT token
    When the user sends "POST /api/index" with the JWT in the Authorization header
    Then the gateway should return a response with status code 202
    And the downstream request should contain header "X-User-Id" with value "tenant-01"

  Scenario: Missing JWT token is rejected
    When a user sends "POST /api/index" without an Authorization header
    Then the gateway should return HTTP 401
    And the response body should contain a ProblemDetails object with title "Unauthorized"

  Scenario: Expired JWT token is rejected
    Given a user "tenant-01" has an expired JWT token
    When the user sends "POST /api/chat" with the expired JWT
    Then the gateway should return HTTP 401

  Scenario: Malformed JWT token is rejected
    When a user sends "GET /api/notifications/stream" with Authorization header "Bearer not-a-jwt"
    Then the gateway should return HTTP 401

  # ─── Tenant Context Injection ───────────────────────────

  Scenario: Gateway injects X-User-Id from JWT claims
    Given a user with JWT containing claim "user_id" = "tenant-42"
    When the user sends "POST /api/chat" with a valid query
    Then the downstream Chat Service should receive header "X-User-Id" = "tenant-42"
    And the downstream request should NOT contain the original Authorization header

  # ─── HTTP/2 Multiplexing ───────────────────────────────

  Scenario: Multiple SSE streams multiplex over a single HTTP/2 connection
    Given a user "tenant-01" has an active notification SSE stream
    When the same user opens a second SSE stream for chat
    Then both streams should share the same TCP connection
    And neither stream should block the other
