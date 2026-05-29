@epic-1 @feature-3 @worker @compilation @bm25
Feature: Multi-Tenant Markdown Knowledge Compilation and Explicit Index Maintenance
  As the Index Worker background service
  I must consume RabbitMQ tasks, parse Markdown, compute BM25 stats, and refresh the index
  So that the tenant's knowledge base is compiled and ready for fast retrieval

  Background:
    Given the Index Worker service is running
    And PostgreSQL database "cloudkb" is available
    And Redis is available for distributed locking and index caching

  # ─── Distributed Lock ───────────────────────────────────

  Scenario: Worker acquires distributed lock before compilation
    Given a "CompileKnowledgeTask" message for tenant "tenant-01" is in the queue
    When the worker picks up the task
    Then Redis key "lock:index:tenant-01" should be set with TTL 300 seconds
    And the worker should proceed with compilation

  Scenario: Concurrent compilation for same tenant is serialised by lock
    Given tenant "tenant-01" has a compilation task in progress (lock held)
    When a second "CompileKnowledgeTask" for "tenant-01" arrives
    Then the second task should wait until the lock is released
    And the second task should NOT corrupt the first task's BM25 statistics

  Scenario: Lock is released after compilation completes
    Given the worker is compiling for tenant "tenant-01"
    When compilation finishes successfully
    Then Redis key "lock:index:tenant-01" should be deleted

  Scenario: Lock is released even if compilation fails
    Given the worker is compiling for tenant "tenant-01"
    When compilation encounters a parse error
    Then Redis key "lock:index:tenant-01" should still be deleted
    And an "IndexFailed" event should be published to Redis Pub/Sub

  # ─── Markdown Parsing & Section Splitting ──────────────

  Scenario: Markdown file is split into sections by headings
    Given tenant "tenant-01" has file "refund_policy.md" in S3 with content:
      """
      # Refund Policy

      We want you to be happy with your purchase.

      ## Refund Timeline

      Refunds are processed within 5-7 business days.

      ## Eligibility

      Items must be returned within 30 days.
      """
    When the worker compiles this file
    Then PostgreSQL should contain 3 TenantSection rows for tenant "tenant-01"
    And section "tenant-01#refund_policy.md#refund-policy" should exist with heading "Refund Policy"
    And section "tenant-01#refund_policy.md#refund-timeline" should exist with:
      | field        | value                                            |
      | heading      | Refund Timeline                                  |
      | headingPath  | ["Refund Policy", "Refund Timeline"]             |
      | content      | Refunds are processed within 5-7 business days.  |

  Scenario: Heading path breadcrumb is correctly computed for nested headings
    Given a Markdown file with headings: H1 "Guide" > H2 "Setup" > H3 "Linux"
    When the worker compiles this file
    Then the "Linux" section should have headingPath ["Guide", "Setup", "Linux"]

  # ─── BM25 Statistics & Index Cache ─────────────────────

  Scenario: BM25 statistics are correctly computed and cached
    Given tenant "tenant-01" has 3 compiled sections with token counts [10, 20, 30]
    When the worker finishes compiling
    Then the cached TenantKbIndex in Redis key "kb:index:tenant-01" should contain:
      | field                  | value   |
      | totalDocuments         | 3       |
      | averageDocumentLength  | 20.0    |
    And each section in the index should have termFrequencies map
    And the index should NOT contain raw section content

  Scenario: Fast-Pass Skip on Identical Hash
    Given tenant "tenant-01" has file "policy.md" already indexed with ContentHash "abc123hash"
    When a new compilation task for "policy.md" arrives with same ContentHash "abc123hash"
    Then the worker should skip Markdown splitting and PostgreSQL writes
    And the worker should keep the existing Redis index cache
    And the IndexCompilationJob status should be updated to "Completed"
    And no new IndexAuditLog should be created

  Scenario: Incremental Section Diffing on Modified File
    Given tenant "tenant-01" has file "policy.md" in database with sections:
      | sectionId                        | content        |
      | tenant-01#policy.md#introduction | Old Intro      |
      | tenant-01#policy.md#setup        | Setup Content  |
    When a modified "policy.md" is compiled with:
      | slugifiedHeading | content        | state    |
      | introduction     | New Intro      | MODIFIED |
      | setup            | Setup Content  | UNCHANGED|
      | advanced-config  | Advanced Setup | ADDED    |
      | (missing "setup" in database, but missing in new file is DELETED) |
    And the new file does NOT contain the "setup" heading (representing deletion)
    Then PostgreSQL sections for "policy.md" should be updated:
      | sectionId                        | content        | status   |
      | tenant-01#policy.md#introduction | New Intro      | Updated  |
      | tenant-01#policy.md#advanced-config| Advanced Setup | Inserted |
      | tenant-01#policy.md#setup        | (deleted)      | Deleted  |
    And the Redis cached index statistics should be updated

  Scenario: Audit Log Generation after incremental compile
    Given the worker completes compiling a modified file "policy.md" for tenant "tenant-01"
    When the job is committed to database
    Then database "index_audit_logs" should contain records for tenant "tenant-01":
      | fileName  | actionType | sectionsAffected | commitMessage                         |
      | policy.md | ADDED      | 1                | Added Heading: # Advanced Setup       |
      | policy.md | MODIFIED   | 1                | Modified Heading: # Introduction      |
      | policy.md | DELETED    | 1                | Deleted Heading: # Setup              |

  # ─── Bulk Operations ────────────────────────────────────

  Scenario: Large file with 50 sections uses bulk insert
    Given tenant "tenant-01" uploads a file with 50 Markdown headings
    When the worker compiles this file
    Then all 50 TenantSection rows should be written to PostgreSQL
    And the database operation should use EF Core batched save changes

  # ─── Dead Letter Queue ─────────────────────────────────

  Scenario: Failed task is routed to DLQ after 3 retries
    Given a "CompileKnowledgeTask" that consistently fails with S3_READ_FAILURE
    When the worker has retried 3 times
    Then the message should be moved to "cloudkb.indexing.compile.dlq"
    And the IndexCompilationJob status should be "Failed"
