@epic-2 @feature-5 @gateway @chat @http2
Feature: Multi-Tenant Chat Gateway Routing and High-Concurrency Transport
  As the Yarp API Gateway handling the read pipeline
  I must authenticate chat requests, inject tenant context, and optimise transport
  So that short-lived QA streams are low-latency under high QPS

  Background:
    Given the API Gateway is running with HTTP/2 enabled
    And the downstream Chat QA Service is available

  # ─── Chat Request Routing ──────────────────────────────

  Scenario: Valid chat request is routed to downstream Chat Service
    Given a user "tenant-01" with a valid JWT token
    When the user sends "POST /api/chat" with body:
      """json
      { "query": "如何配置環境變數？" }
      """
    Then the gateway should forward the request to the Chat Service
    And the forwarded request should contain header "X-User-Id" = "tenant-01"
    And the response Content-Type should be "text/event-stream"

  Scenario: Chat request without JWT is rejected at gateway
    When an unauthenticated user sends "POST /api/chat" with a query
    Then the gateway should return HTTP 401
    And the request should NOT reach the Chat Service

  # ─── Query Validation ──────────────────────────────────

  Scenario: Empty query is rejected
    Given a user "tenant-01" with a valid JWT token
    When the user sends "POST /api/chat" with body:
      """json
      { "query": "" }
      """
    Then the gateway should return HTTP 400

  Scenario: Query exceeding 2000 characters is rejected
    Given a user "tenant-01" with a valid JWT token
    When the user sends "POST /api/chat" with a 2500-character query
    Then the API should return HTTP 400

  # ─── High-Concurrency SSE Transport ────────────────────

  Scenario: Multiple concurrent chat streams from different tenants
    Given 10 tenants each send a "POST /api/chat" request simultaneously
    Then all 10 should receive SSE token streams
    And all streams should use HTTP/2 multiplexing on shared connections
    And average first-token latency should be under 500 milliseconds
