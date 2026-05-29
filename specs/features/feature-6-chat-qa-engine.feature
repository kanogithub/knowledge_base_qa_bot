@epic-2 @feature-6 @chat @bm25 @sse @grounded
Feature: Low-Latency In-Memory BM25 Retrieval and Short-Lived Chat SSE Stream
  As the Chat QA Service
  I must perform in-memory BM25 retrieval, enforce score thresholds, and stream grounded answers
  So that users get fast, accurate, citation-backed responses with zero hallucination

  Background:
    Given the Chat QA Service is running
    And Redis contains a cached TenantKbIndex for tenant "tenant-01"
    And PostgreSQL contains the corresponding TenantSection rows

  # ─── In-Memory BM25 Retrieval ──────────────────────────

  Scenario: Successful retrieval returns relevant sections
    Given tenant "tenant-01" has indexed the following sections:
      | sectionId                                   | heading          | tokens                                     |
      | tenant-01#refund_policy.md#refund-timeline   | Refund Timeline  | refund, timeline, business, days, process   |
      | tenant-01#account_help.md#change-email       | Change Email     | email, change, address, account, settings   |
      | tenant-01#shipping_faq.md#delivery-time      | Delivery Time    | shipping, delivery, time, business, days    |
    When the user asks "How long do refunds take?"
    Then the BM25 engine should score "refund-timeline" highest
    And the top-K sections should include "tenant-01#refund_policy.md#refund-timeline"

  Scenario: BM25 heading path boost improves accuracy
    Given a section with headingPath ["Refund Policy", "Refund Timeline"]
    When the query contains the word "refund"
    Then the heading boost should increase the BM25 score for that section
    And sections with matching heading paths should rank higher

  # ─── Weak Retrieval Early Exit ─────────────────────────

  Scenario: Weak retrieval early exit — no DB or LLM call
    Given the tenant "tenant-01" has an indexed knowledge base
    When the user asks "What is the weather today?"
    Then the highest BM25 score should be below threshold 0.5
    And the system should return HTTP 200
    And the SSE stream should immediately output a refusal message containing "無法從現有的知識庫中確認"
    And PostgreSQL should NOT be queried for section content
    And the OpenAI API should NOT be called

  Scenario: Early exit for malicious or nonsense queries
    Given the tenant "tenant-01" has an indexed knowledge base
    When the user asks "asdfjkl;qwerty12345"
    Then the BM25 score should be 0.0
    And the system should return the refusal message without calling any external service

  # ─── Grounded Answer Generation ─────────────────────────

  Scenario: Grounded answer with source citations
    Given tenant "tenant-01" has indexed sections about refund policies
    When the user asks "How long do refunds take?"
    Then the system should query PostgreSQL for the top-K section content
    And the system should inject a grounded system prompt constraining the LLM
    And the SSE stream should contain token chunks with text
    And the final chunk should have "isFinal" = true
    And the final chunk should contain "sources" with at least one SourceCitation
    And the citation should reference "refund_policy.md#refund-timeline"

  Scenario: Answer cites correct source for email question
    Given tenant "tenant-01" has indexed account help documentation
    When the user asks "Can I change my email address?"
    Then the SSE stream final chunk sources should include:
      | fileName         | heading       |
      | account_help.md  | Change Email  |

  # ─── Short-Lived SSE Lifecycle ─────────────────────────

  Scenario: SSE connection is closed after final token
    Given tenant "tenant-01" asks a valid grounded question
    When the LLM finishes generating the answer
    Then the last SSE event should have "isFinal" = true
    And the server should actively close the HTTP connection
    And no further events should be sent on this stream

  Scenario: SSE stream sends sources before answer tokens
    Given tenant "tenant-01" asks "How long do refunds take?"
    When the SSE stream begins
    Then the first SSE event(s) should contain source metadata
    And subsequent events should contain answer text tokens
    And the stream should end with the final chunk

  # ─── Chat Before Indexing ───────────────────────────────

  Scenario: Chat when no index exists returns informative message
    Given tenant "tenant-99" has NO cached TenantKbIndex in Redis
    When the user asks "How long do refunds take?"
    Then the system should return HTTP 200
    And the SSE stream should output a message indicating the knowledge base has not been indexed

  # ─── Multi-Tenant Data Isolation ────────────────────────

  Scenario: Tenant A cannot retrieve tenant B's knowledge
    Given tenant "tenant-01" has indexed "refund_policy.md"
    And tenant "tenant-02" has an empty index
    When tenant "tenant-02" asks "How long do refunds take?"
    Then the BM25 retrieval should return no results from tenant-01's data
    And the system should return the refusal message

  # ─── End-to-End Integration ─────────────────────────────

  Scenario Outline: End-to-end grounded QA for various questions
    Given tenant "tenant-01" has a fully compiled knowledge base
    When the user asks "<question>"
    Then the response should cite "<expectedFile>#<expectedHeading>"
    And the answer should be grounded in the cited section content

    Examples:
      | question                           | expectedFile       | expectedHeading     |
      | How long do refunds take?          | refund_policy.md   | refund-timeline     |
      | Can I change my email address?     | account_help.md    | change-email        |
      | What are the shipping options?     | shipping_faq.md    | delivery-time       |

  Scenario: Out-of-scope question returns honest refusal
    Given tenant "tenant-01" has a fully compiled knowledge base
    When the user asks "Which restaurants are nearby?"
    Then the system should NOT call the OpenAI API
    And the SSE stream should contain "無法從現有的知識庫中確認"
