@epic-1 @feature-2 @indexing @async
Feature: Async Event-Driven File Ingestion Pipeline
  As the Indexing API service
  I must receive Markdown uploads, stream to S3, enqueue tasks, and return 202 instantly
  So that the write pipeline never blocks the frontend

  Background:
    Given the Indexing API service is running
    And RabbitMQ is available on the "cloudkb.indexing.compile" queue
    And MinIO/S3 storage is available

  # ─── File Upload & S3 Streaming ─────────────────────────

  Scenario: Single Markdown file upload streams to S3 and enqueues task
    Given the tenant "tenant-01" uploads file "refund_policy.md" with valid content
    When the user sends "POST /api/index" with the file as multipart/form-data
    Then the API should return HTTP 202 within 100 milliseconds
    And the response body should contain a "taskId" in UUID format
    And the response body should contain "message" = "Knowledge compilation job enqueued."
    And the file should be stored at S3 path "/tenant-01/raw/refund_policy.md"
    And a "CompileKnowledgeTask" message should be published to RabbitMQ

  Scenario: Multiple Markdown files upload in a single request
    Given the tenant "tenant-01" uploads files:
      | fileName          |
      | refund_policy.md  |
      | account_help.md   |
      | shipping_faq.md   |
    When the user sends "POST /api/index" with all files
    Then the API should return HTTP 202
    And all 3 files should be stored under S3 path "/tenant-01/raw/"
    And the RabbitMQ message should contain all 3 file names

  # ─── Non-Blocking Response ──────────────────────────────

  Scenario: API returns 202 before background compilation starts
    Given the tenant "tenant-01" uploads a large 5MB Markdown file
    When the user sends "POST /api/index"
    Then the API should return HTTP 202 within 100 milliseconds
    And the Index Worker should NOT have started processing yet

  # ─── Error Handling ─────────────────────────────────────

  Scenario: Upload with no files returns 400
    When the tenant "tenant-01" sends "POST /api/index" with an empty multipart body
    Then the API should return HTTP 400
    And the response should be a ProblemDetails with detail containing "files"

  Scenario: Upload with non-Markdown file returns 400
    Given the tenant "tenant-01" uploads file "photo.png"
    When the user sends "POST /api/index" with the file
    Then the API should return HTTP 400
    And the response should indicate only .md files are accepted

  # ─── Tenant Isolation ───────────────────────────────────

  Scenario: Files from different tenants are stored in isolated S3 paths
    Given tenant "tenant-01" uploads "policy.md"
    And tenant "tenant-02" uploads "policy.md"
    When both uploads complete
    Then S3 should contain "/tenant-01/raw/policy.md"
    And S3 should contain "/tenant-02/raw/policy.md"
    And the files should have different content
