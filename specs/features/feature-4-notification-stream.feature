@epic-1 @feature-4 @notification @sse @pubsub
Feature: Redis Pub/Sub Long-Lived SSE Notification Stream
  As the Notification SSE service
  I must subscribe to Redis Pub/Sub and push real-time events to the frontend
  So that users see live progress during background knowledge compilation

  Background:
    Given the Notification SSE service is running
    And Redis Pub/Sub is available

  # ─── SSE Connection Lifecycle ──────────────────────────

  Scenario: User establishes a long-lived SSE notification channel
    Given the tenant "tenant-01" is authenticated
    When the user sends "GET /api/notifications/stream"
    Then the response should have Content-Type "text/event-stream"
    And the response should have Cache-Control "no-cache"
    And the HTTP connection should remain open
    And the service should subscribe to Redis channel "ch:notifications:tenant-01"

  Scenario: Keep-alive heartbeat prevents connection timeout
    Given tenant "tenant-01" has an active notification SSE stream
    When 30 seconds pass with no events
    Then the server should send an SSE comment ":ping" as a keep-alive
    And the HTTP connection should remain open

  # ─── Event Relay ────────────────────────────────────────

  Scenario: Processing progress event is relayed to frontend
    Given tenant "tenant-01" has an active notification SSE stream
    When the Index Worker publishes to Redis channel "ch:notifications:tenant-01":
      """json
      {
        "eventType": "IndexProcessing",
        "taskId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
        "message": "正在切分 Markdown Section..."
      }
      """
    Then the SSE stream should emit an event with:
      | field     | value                                         |
      | event     | IndexProcessing                               |
      | data.taskId | a1b2c3d4-e5f6-7890-abcd-ef1234567890        |
      | data.message | 正在切分 Markdown Section...                |

  Scenario: Compilation completed event is relayed to frontend
    Given tenant "tenant-01" has an active notification SSE stream
    When the Index Worker publishes an "IndexCompleted" event with:
      | field              | value  |
      | sectionsCompiled   | 42     |
      | filesProcessed     | 5      |
    Then the SSE stream should emit an event with eventType "IndexCompleted"
    And the event data should contain message "您的知識庫已編譯完成！"
    And the event metadata should show sectionsCompiled = 42

  Scenario: Compilation failed event is relayed to frontend
    Given tenant "tenant-01" has an active notification SSE stream
    When the Index Worker publishes an "IndexFailed" event
    Then the SSE stream should emit an event with eventType "IndexFailed"
    And the event data should contain an errorCode

  # ─── Multi-Tenant Isolation ─────────────────────────────

  Scenario: Notifications are isolated between tenants
    Given tenant "tenant-01" has an active notification SSE stream
    And tenant "tenant-02" has an active notification SSE stream
    When the Index Worker publishes a "IndexCompleted" event for tenant "tenant-02"
    Then tenant "tenant-02" should receive the completion event
    And tenant "tenant-01" should NOT receive any event

  # ─── Reconnection ──────────────────────────────────────

  Scenario: Frontend reconnects after network interruption
    Given tenant "tenant-01" had an active SSE stream that was disconnected
    When the frontend reconnects with "GET /api/notifications/stream"
    Then a new Redis subscription for "ch:notifications:tenant-01" should be created
    And the SSE stream should resume receiving new events
