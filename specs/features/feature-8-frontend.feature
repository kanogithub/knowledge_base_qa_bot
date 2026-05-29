@epic-3 @feature-8 @frontend @ui
Feature: Integrated Knowledge Base Dashboard & Chat Interface
  As a registered tenant user
  I want to log in, manage my files (click-to-upload or drag-and-drop), receive processing updates, and chat with a grounded AI assistant
  So that I can securely administer my documents and retrieve cited answers

  Background:
    Given the Gateway and downstream microservices are running

  Scenario: Tenant login succeeds with correct credentials
    Given the user is on the login page of the SPA
    When the user enters username "tenant-01" and password "password"
    And clicks the "Login" button
    Then the user should be redirected to the dashboard layout
    And the top-right corner should display the user's name and a "Log Out" button
    And the browser local storage should store a valid JWT token under "token"

  Scenario: Tenant login fails with incorrect credentials
    Given the user is on the login page of the SPA
    When the user enters username "tenant-01" and password "wrong-password"
    And clicks the "Login" button
    Then the user should see an error message "Invalid credentials"
    And the user should remain on the login page
    And the browser local storage should not contain a "token"

  Scenario: Tenant logs out and clears authentication state
    Given the user is logged in as "tenant-01" and is viewing the dashboard
    When the user clicks the "Log Out" button in the header
    Then the user should be redirected to the login page
    And the browser local storage "token" key should be cleared

  Scenario: Tenant registration succeeds with new credentials
    Given the user is on the login page of the SPA
    When the user clicks the "Sign Up" toggle link
    Then the card should switch to the registration form
    When the user enters username "new-tenant" and password "secure-password"
    And clicks the "Register" button
    Then the user should see a success Toast message "User registered successfully"
    And the card should toggle back to the login form
    And the username input should be pre-filled with "new-tenant"

  Scenario: Tenant registration fails when username already exists
    Given the user is on the login page of the SPA
    And username "tenant-01" is already registered
    When the user clicks the "Sign Up" toggle link
    And enters username "tenant-01" and password "password"
    And clicks the "Register" button
    Then the user should see an error message "Username already exists"
    And the user should remain on the registration form

  Scenario: Tenant registration fails validation with short password
    Given the user is on the login page of the SPA
    When the user clicks the "Sign Up" toggle link
    And enters username "new-tenant" and password "123"
    And clicks the "Register" button
    Then the user should see an error message "Password must be at least 6 characters"
    And the user should remain on the registration form

  Scenario: Multiple files upload via clicking file selection button
    Given the user is logged in as "tenant-01"
    When the user clicks the "Upload Files" input button
    And selects "refund_policy.md" and "tos.md"
    Then the upload component should validate that both files have ".md" extension
    And compile a multipart request for both "refund_policy.md" and "tos.md"
    And upload them to "/api/index" with the tenant's Bearer token
    And the user should see a Toast notification tracking the upload task

  Scenario: File upload via drag-and-drop filtering invalid file extensions
    Given the user is logged in as "tenant-01"
    When the user drags and drops "valid_guide.md" and "invalid_image.png" onto the dropzone
    Then the dropzone component should ignore "invalid_image.png" and display a warning toast
    And compile a multipart request only for "valid_guide.md"
    And upload it to "/api/index" with the tenant's Bearer token
    And the user should see a Toast notification tracking the upload task

  Scenario: User views list of previously uploaded files
    Given the user is logged in as "tenant-01"
    And the database contains "refund_policy.md" (1024 bytes, indexed) and "tos.md" (2048 bytes, pending) for "tenant-01"
    When the user views the left panel files list
    Then the user should see "refund_policy.md" and "tos.md" in a table
    And "refund_policy.md" should display a green status badge of "Indexed"
    And "tos.md" should display a yellow status badge of "Queued"
    And the file table should show correct file sizes and upload timestamps

  Scenario: User receives compilation completion notification and files list refreshes
    Given the user is logged in as "tenant-01"
    And the user has a live SSE connection to "/api/notifications/stream"
    And "tos.md" was previously uploaded and is "Queued"
    When the index worker completes compilation for "tos.md"
    And the SSE stream receives a "IndexCompleted" event for "tos.md"
    Then the user should see a success Toast message indicating "tos.md" has been compiled
    And the files list table should automatically refresh
    And "tos.md" status badge should transition from "Queued" to "Indexed"

  Scenario: User streams a grounded chat response with Markdown rendering and clicks a citation
    Given the user is logged in as "tenant-01"
    When the user submits the question "What is the refund timeline?" in the text area
    Then the user's query should appear in the chat history
    And the chat window should display a typing indicator
    And the assistant response should stream token-by-token using SSE from "/api/chat"
    And the assistant message should render HTML formatting via Markdown parsing (e.g. bold, bullet points)
    And the assistant response should contain citation links in the format "[refund_policy.md#refund-timeline]"
    And the message sources list should display "(Score: X.XX)" next to the cited file name
    When the user clicks the "refund_policy.md#refund-timeline" citation link
    Then a details drawer should slide in showing the cited section filename, heading details, and "BM25 Retrieval Score: X.XXXX"

  Scenario: Tenant deletes an uploaded file successfully
    Given the user is logged in as "tenant-01"
    And the files list contains "refundable.md" which is "Indexed"
    When the user clicks the "Delete" button for "refundable.md" in the files list table
    And confirms the deletion action
    Then a DELETE request should be sent to "/api/index/refundable.md" with the tenant's Bearer token
    And the user should see a success Toast message "File refundable.md deleted successfully"
    And the files list table should automatically refresh and no longer contain "refundable.md"


