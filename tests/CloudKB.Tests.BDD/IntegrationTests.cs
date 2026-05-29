using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using CloudKB.Infrastructure;
using CloudKB.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using CloudKB.Worker.Indexer.Services;
using NSubstitute;
using RabbitMQ.Client;
using StackExchange.Redis;

using Xunit;

namespace CloudKB.Tests.BDD;

public class ChatApiIntegrationTests : IClassFixture<WebApplicationFactory<CloudKB.ApiService.Chat.Program>>
{
    private readonly WebApplicationFactory<CloudKB.ApiService.Chat.Program> _factory;

    public ChatApiIntegrationTests(WebApplicationFactory<CloudKB.ApiService.Chat.Program> factory)
    {
        // Set up the InMemory database and Redis mock
        _factory = factory.WithWebHostBuilder(builder =>

        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                // Remove SQL Server/Postgres DbContext and EF Core internal services
                var descriptors = services.Where(d =>
                    d.ServiceType == typeof(CloudKbDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<CloudKbDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") == true)
                    .ToList();

                foreach (var descriptor in descriptors)
                {
                    services.Remove(descriptor);
                }

                // Add InMemory DbContext
                services.AddDbContext<CloudKbDbContext>(options =>
                {
                    options.UseInMemoryDatabase("CloudKbTestDb");
                });


                // Mock Redis Multiplexer
                var mockRedis = Substitute.For<IConnectionMultiplexer>();
                var mockDb = Substitute.For<IDatabase>();

                // Configure standard Redis index mock
                var indexJson = GetMockTenantIndexJson();
                mockDb.StringGetAsync("kb:index:tenant-01").Returns(new RedisValue(indexJson));
                mockDb.StringGetAsync("kb:index:tenant-99").Returns(RedisValue.Null);

                mockRedis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(mockDb);
                services.AddSingleton(mockRedis);

                // Mock Chat Client
                services.AddChatClient(new FakeChatClient());
            });
        });
    }

    [Fact]
    public async Task Health_ShouldReturnHealthy()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", content);
    }

    [Fact]
    public async Task PostChat_WithValidQuery_ShouldReturnSourceCitationsAndTokens()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "tenant-01");

        // Seed Postgres InMemory
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CloudKbDbContext>();
            if (!db.TenantSections.Any(s => s.Id == "tenant-01#refund.md#timeline"))
            {
                db.TenantSections.Add(new TenantSection
                {
                    Id = "tenant-01#refund.md#timeline",
                    TenantId = "tenant-01",
                    FileName = "refund.md",
                    Heading = "Timeline",
                    HeadingPath = new List<string> { "Refund Policy", "Timeline" },
                    Content = "Refunds are processed within 5 business days.",
                    Tokens = new List<string> { "refunds", "processed", "within", "business", "days" },
                    TokenCount = 5
                });
                await db.SaveChangesAsync();
            }
        }

        var request = new ChatRequest("How long do refunds take?");

        // Act
        var response = await client.PostAsJsonAsync("/api/chat", request);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        var content = await response.Content.ReadAsStringAsync();
        
        // Assert SSE frames format and content
        Assert.Contains("data: {", content);
        Assert.Contains("\"sources\":", content);
        Assert.Contains("refund.md", content);
        Assert.Contains("Timeline", content);
        Assert.Contains("This", content); // From FakeChatClient tokens
        Assert.Contains("\"isFinal\":true", content);
    }

    [Fact]
    public async Task PostChat_WithWeakRetrievalQuery_ShouldEarlyExitWithRefusal()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "tenant-01");

        var request = new ChatRequest("What is the weather today?");

        // Act
        var response = await client.PostAsJsonAsync("/api/chat", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        
        Assert.Contains("無法從現有的知識庫中確認", content);
        Assert.Contains("\"isFinal\":true", content);
        // Ensure no LLM content was output (FakeChatClient outputs "This is a simulated response")
        Assert.DoesNotContain("simulated response", content);
    }

    [Fact]
    public async Task PostChat_WhenIndexDoesNotExist_ShouldReturnNotIndexedMessage()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "tenant-99"); // tenant-99 has no Redis index mock

        var request = new ChatRequest("Hello");

        // Act
        var response = await client.PostAsJsonAsync("/api/chat", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("knowledge base has not been indexed", content);
    }

    private string GetMockTenantIndexJson()
    {
        var kbIndex = new TenantKbIndex(
            TenantId: "tenant-01",
            TotalDocuments: 10,
            AverageDocumentLength: 5.0,
            LastUpdatedAt: DateTime.UtcNow,

            Sections: new List<IndexedSectionMeta>
            {
                new IndexedSectionMeta(
                    SectionId: "tenant-01#refund.md#timeline",
                    FileName: "refund.md",
                    Heading: "Timeline",
                    HeadingPath: new List<string> { "Refund Policy", "Timeline" },
                    TokenCount: 5,
                    TermFrequencies: new Dictionary<string, int>
                    {
                        { "refunds", 1 }, { "processed", 1 }, { "within", 1 }, { "business", 1 }, { "days", 1 }
                    }
                )
            }
        );

        return JsonSerializer.Serialize(kbIndex, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}

public class GatewayIntegrationTests : IClassFixture<WebApplicationFactory<CloudKB.Gateway.Program>>
{
    private readonly WebApplicationFactory<CloudKB.Gateway.Program> _factory;

    public GatewayIntegrationTests(WebApplicationFactory<CloudKB.Gateway.Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                // Remove SQL Server/Postgres DbContext and EF Core internal services
                var descriptors = services.Where(d =>
                    d.ServiceType == typeof(CloudKbDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<CloudKbDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") == true)
                    .ToList();

                foreach (var descriptor in descriptors)
                {
                    services.Remove(descriptor);
                }

                // Add InMemory DbContext
                services.AddDbContext<CloudKbDbContext>(options =>
                {
                    options.UseInMemoryDatabase("CloudKbGatewayTestDb");
                });
            });
        });
    }

    [Fact]
    public async Task Health_ShouldReturnOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("ok", json?["status"]);
    }

    [Fact]
    public async Task PostChat_WithoutToken_ShouldReturn41Unauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new ChatRequest("Hello");

        // Act
        var response = await client.PostAsJsonAsync("/api/chat", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostChat_WithMalformedToken_ShouldReturn41Unauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt-token");
        var request = new ChatRequest("Hello");

        // Act
        var response = await client.PostAsJsonAsync("/api/chat", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostChat_WithValidToken_ShouldBeAcceptedByAuthAndAttemptToRoute()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = JwtMockGenerator.GenerateToken("tenant-01");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = new ChatRequest("Hello");

        // Act
        var response = await client.PostAsJsonAsync("/api/chat", request);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostIndex_WithoutToken_ShouldReturn41Unauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var content = new MultipartFormDataContent();

        // Act
        var response = await client.PostAsync("/api/index", content);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostIndex_WithValidToken_ShouldBeAcceptedByAuth()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = JwtMockGenerator.GenerateToken("tenant-01");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var content = new MultipartFormDataContent();

        // Act
        var response = await client.PostAsync("/api/index", content);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetNotificationStream_WithoutToken_ShouldReturn41Unauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/notifications/stream");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetNotificationStream_WithValidToken_ShouldBeAcceptedByAuth()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = JwtMockGenerator.GenerateToken("tenant-01");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/notifications/stream");

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostLogin_WithValidCredentials_ShouldReturnJwtToken()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new { Username = "tenant-01", Password = "password" };

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(json);
        Assert.True(json.ContainsKey("token"));
        Assert.False(string.IsNullOrWhiteSpace(json["token"]));
    }

    [Fact]
    public async Task PostLogin_WithInvalidCredentials_ShouldReturn401Unauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new { Username = "tenant-01", Password = "wrong-password" };

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostRegister_WithValidCredentials_ShouldCreateUserAndAllowLogin()
    {
        // Arrange
        var client = _factory.CreateClient();
        var username = $"user_{Guid.NewGuid():N}";
        var registerRequest = new { Username = username, Password = "securepassword123" };

        // Act - Register
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", registerRequest);

        // Assert - Register
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var registerJson = await registerResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(registerJson);
        Assert.Equal("User registered successfully.", registerJson["message"]);

        // Act - Login
        var loginRequest = new { Username = username, Password = "securepassword123" };
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert - Login
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginJson = await loginResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(loginJson);
        Assert.True(loginJson.ContainsKey("token"));
        Assert.False(string.IsNullOrWhiteSpace(loginJson["token"]));
    }

    [Fact]
    public async Task PostRegister_WithTooShortPassword_ShouldReturn400BadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        var username = $"user_{Guid.NewGuid():N}";
        var registerRequest = new { Username = username, Password = "123" };

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/register", registerRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostRegister_WithDuplicateUsername_ShouldReturn409Conflict()
    {
        // Arrange
        var client = _factory.CreateClient();
        var username = $"user_{Guid.NewGuid():N}";
        var registerRequest = new { Username = username, Password = "securepassword123" };

        // Act 1 - First register
        var response1 = await client.PostAsJsonAsync("/api/auth/register", registerRequest);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        // Act 2 - Second register with same username
        var response2 = await client.PostAsJsonAsync("/api/auth/register", registerRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);
    }
}

public class IndexingApiIntegrationTests : IClassFixture<WebApplicationFactory<CloudKB.ApiService.Indexing.Program>>
{
    private readonly WebApplicationFactory<CloudKB.ApiService.Indexing.Program> _factory;
    private readonly IStorageService _mockStorage = Substitute.For<IStorageService>();
    private readonly IConnection _mockRabbitConnection = Substitute.For<IConnection>();
    private readonly IModel _mockRabbitChannel = Substitute.For<IModel>();

    public IndexingApiIntegrationTests(WebApplicationFactory<CloudKB.ApiService.Indexing.Program> factory)
    {
        _mockRabbitConnection.CreateModel().Returns(_mockRabbitChannel);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                // Remove existing DbContext
                var descriptors = services.Where(d =>
                    d.ServiceType == typeof(CloudKbDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<CloudKbDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") == true)
                    .ToList();

                foreach (var descriptor in descriptors)
                {
                    services.Remove(descriptor);
                }

                // Add InMemory DbContext
                services.AddDbContext<CloudKbDbContext>(options =>
                {
                    options.UseInMemoryDatabase("CloudKbIndexingTestDb");
                });

                // Inject mock Storage, RabbitMQ connection, and Redis multiplexer
                services.AddSingleton(_mockStorage);
                services.AddSingleton(_mockRabbitConnection);

                var mockRedis = Substitute.For<IConnectionMultiplexer>();
                var mockDb = Substitute.For<IDatabase>();
                mockRedis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(mockDb);
                services.AddSingleton(mockRedis);
            });
        });
    }

    [Fact]
    public async Task Health_ShouldReturnHealthy()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", content);
    }

    [Fact]
    public async Task PostIndex_WithValidMarkdownFiles_ShouldUploadToS3PublishToRabbitMQAndReturn202()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "tenant-01");

        var multipartContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("# Ingest test\nHello world"));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/markdown");
        multipartContent.Add(fileContent, "files", "refund_policy.md");

        // Act
        var response = await client.PostAsync("/api/index", multipartContent);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<IndexAcceptedResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.TaskId);
        Assert.Equal("Knowledge compilation job enqueued.", result.Message);

        // Verify Storage upload call
        await _mockStorage.Received(1).UploadAsync(
            "tenant-01",
            "refund_policy.md",
            Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());

        // Verify RabbitMQ publish call
        _mockRabbitChannel.Received(1).BasicPublish(
            exchange: "cloudkb.indexing",
            routingKey: "compile.tenant-01",
            mandatory: Arg.Any<bool>(),
            basicProperties: Arg.Any<IBasicProperties>(),
            body: Arg.Any<ReadOnlyMemory<byte>>());

        // Verify Database records exist
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudKbDbContext>();

        var tenantFile = await db.TenantFiles.FirstOrDefaultAsync(f => f.TenantId == "tenant-01" && f.FileName == "refund_policy.md");
        Assert.NotNull(tenantFile);
        Assert.False(tenantFile.IsIndexed);
        Assert.Equal("/tenant-01/raw/refund_policy.md", tenantFile.S3Key);

        var compilationJob = await db.IndexCompilationJobs.FirstOrDefaultAsync(j => j.TaskId == result.TaskId);
        Assert.NotNull(compilationJob);
        Assert.Equal("Pending", compilationJob.Status);
        Assert.Contains("refund_policy.md", compilationJob.FileNames);
    }

    [Fact]
    public async Task PostIndex_WithNonMarkdownFiles_ShouldReturn400BadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "tenant-01");

        var multipartContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("not markdown"));
        multipartContent.Add(fileContent, "files", "photo.png");

        // Act
        var response = await client.PostAsync("/api/index", multipartContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostIndex_WithoutFiles_ShouldReturn400BadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "tenant-01");

        var multipartContent = new MultipartFormDataContent();

        // Act
        var response = await client.PostAsync("/api/index", multipartContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetFiles_ShouldReturnTenantFiles()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "tenant-01");

        // Seed some files in EF DbContext
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CloudKbDbContext>();
            db.TenantFiles.Add(new TenantFile
            {
                TenantId = "tenant-01",
                FileName = "test_doc_a.md",
                S3Key = "/tenant-01/raw/test_doc_a.md",
                FileSizeBytes = 100,
                ContentHash = "hash1",
                IsIndexed = true,
                UploadedAt = DateTime.UtcNow
            });
            db.TenantFiles.Add(new TenantFile
            {
                TenantId = "tenant-02",
                FileName = "test_doc_b.md",
                S3Key = "/tenant-02/raw/test_doc_b.md",
                FileSizeBytes = 200,
                ContentHash = "hash2",
                IsIndexed = true,
                UploadedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync("/api/index/files");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var filesList = await response.Content.ReadFromJsonAsync<List<Dictionary<string, object>>>();
        Assert.NotNull(filesList);
        
        // Should only return tenant-01 file!
        Assert.Single(filesList);
        var jsonEl = (JsonElement)filesList[0]["fileName"];
        Assert.Equal("test_doc_a.md", jsonEl.GetString());
    }

    [Fact]
    public async Task DeleteFile_WithNonExistentFile_ShouldReturn404NotFound()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "tenant-01");

        // Act
        var response = await client.DeleteAsync("/api/index/nonexistent.md");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFile_WithValidFile_ShouldPurgeFromDbAndStorageAndCache()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "tenant-01");

        // Seed a file in the DB first
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CloudKbDbContext>();
            
            // Clean up any existing records for test consistency
            db.TenantSections.RemoveRange(db.TenantSections.Where(s => s.TenantId == "tenant-01"));
            db.TenantFiles.RemoveRange(db.TenantFiles.Where(f => f.TenantId == "tenant-01"));
            db.TenantFileStates.RemoveRange(db.TenantFileStates.Where(fs => fs.TenantId == "tenant-01"));
            await db.SaveChangesAsync();

            db.TenantFiles.Add(new TenantFile
            {
                TenantId = "tenant-01",
                FileName = "deletable.md",
                S3Key = "/tenant-01/raw/deletable.md",
                FileSizeBytes = 100,
                ContentHash = "abc",
                IsIndexed = true
            });

            db.TenantFileStates.Add(new TenantFileState
            {
                Id = "tenant-01#deletable.md",
                TenantId = "tenant-01",
                FileName = "deletable.md",
                ContentHash = "abc"
            });

            db.TenantSections.Add(new TenantSection
            {
                Id = "tenant-01#deletable.md#test",
                TenantId = "tenant-01",
                FileName = "deletable.md",
                Heading = "Test",
                Content = "This is a test section to delete.",
                TokenCount = 7,
                Tokens = new List<string> { "this", "is", "a", "test", "section", "to", "delete" }
            });

            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.DeleteAsync("/api/index/deletable.md");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(json);
        Assert.Equal("File deletable.md deleted successfully.", json["message"]);

        // Verify Storage delete call
        await _mockStorage.Received(1).DeleteAsync(
            "tenant-01",
            "deletable.md",
            Arg.Any<CancellationToken>());

        // Verify database is cleared
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CloudKbDbContext>();

            var existsInFiles = await db.TenantFiles.AnyAsync(f => f.TenantId == "tenant-01" && f.FileName == "deletable.md");
            Assert.False(existsInFiles);

            var existsInStates = await db.TenantFileStates.AnyAsync(fs => fs.TenantId == "tenant-01" && fs.FileName == "deletable.md");
            Assert.False(existsInStates);

            var existsInSections = await db.TenantSections.AnyAsync(s => s.TenantId == "tenant-01" && s.FileName == "deletable.md");
            Assert.False(existsInSections);

            var auditLog = await db.IndexAuditLogs.FirstOrDefaultAsync(l => l.TenantId == "tenant-01" && l.FileName == "deletable.md" && l.ActionType == "DELETED");
            Assert.NotNull(auditLog);
            Assert.Equal(1, auditLog.SectionsAffected);
            Assert.Equal("Deleted knowledge base file: deletable.md.", auditLog.CommitMessage);
        }
    }
}

public class WorkerIntegrationTests
{
    private readonly CloudKbDbContext _db;
    private readonly IStorageService _mockStorage = Substitute.For<IStorageService>();
    private readonly IConnectionMultiplexer _mockRedis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _mockRedisDb = Substitute.For<IDatabase>();
    private readonly RedisEventPublisher _mockEventPublisher;
    private readonly IConfiguration _config;

    public WorkerIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<CloudKbDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CloudKbDbContext(options);

        _mockRedis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_mockRedisDb);
        var mockSubscriber = Substitute.For<ISubscriber>();
        _mockRedis.GetSubscriber(Arg.Any<object>()).Returns(mockSubscriber);

        _mockRedisDb.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(), When.NotExists)
            .Returns(true);
        _mockRedisDb.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((long)1));

        _mockEventPublisher = new RedisEventPublisher(_mockRedis);

        var inMemorySettings = new Dictionary<string, string> {
            {"Storage:BucketName", "knowledge-base"}
        };
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    [Fact]
    public async Task ExecuteAsync_WithNewMarkdownFile_ShouldParseSectionsStoreInPostgresAndCacheInRedis()
    {
        // Arrange
        var tenantId = "tenant-01";
        var fileName = "refund_policy.md";
        var taskId = Guid.NewGuid();

        var content = "# Refund Timeline\nRefunds are processed in 5 days.";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
        var contentHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        _db.TenantFiles.Add(new TenantFile
        {
            TenantId = tenantId,
            FileName = fileName,
            ContentHash = contentHash,
            S3Key = $"/{tenantId}/raw/{fileName}",
            FileSizeBytes = 100,
            IsIndexed = false
        });

        _db.IndexCompilationJobs.Add(new IndexCompilationJob
        {
            TaskId = taskId,
            TenantId = tenantId,
            Status = "Pending",
            S3StoragePath = $"/{tenantId}/raw/",
            FileNames = new List<string> { fileName }
        });
        await _db.SaveChangesAsync();

        _mockStorage.DownloadAsync(tenantId, fileName, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(content));

        var pipeline = new CompilationPipeline(_mockStorage, _db, _mockRedis, _mockEventPublisher, _config);
        var payload = new CompileKnowledgeTaskPayload(taskId, tenantId, $"/{tenantId}/raw/", new List<string> { fileName }, DateTime.UtcNow);

        // Act
        await pipeline.ExecuteAsync(payload, CancellationToken.None);

        // Assert
        var sections = await _db.TenantSections.Where(s => s.TenantId == tenantId).ToListAsync();
        Assert.Single(sections);
        Assert.Equal("tenant-01#refund_policy.md#refund-timeline", sections[0].Id);
        Assert.Equal("Refund Timeline", sections[0].Heading);

        var file = await _db.TenantFiles.FirstAsync(f => f.TenantId == tenantId && f.FileName == fileName);
        Assert.True(file.IsIndexed);

        var fileState = await _db.TenantFileStates.FindAsync($"{tenantId}#{fileName}");
        Assert.NotNull(fileState);
        Assert.Equal(contentHash, fileState.ContentHash);

        var job = await _db.IndexCompilationJobs.FirstAsync(j => j.TaskId == taskId);
        Assert.Equal("Completed", job.Status);

        await _mockRedisDb.Received(1).StringSetAsync(
            "kb:index:tenant-01",
            Arg.Is<RedisValue>(v => v.ToString().Contains("refund-timeline")));
    }

    [Fact]
    public async Task ExecuteAsync_WithIncrementalChanges_ShouldApplyDiffsCorrectly()
    {
        var tenantId = "tenant-01";
        var fileName = "faq.md";
        
        _db.TenantSections.Add(new TenantSection
        {
            Id = $"{tenantId}#{fileName}#question-a",
            TenantId = tenantId,
            FileName = fileName,
            Heading = "Question A",
            HeadingPath = new List<string> { "Question A" },
            Content = "Original text.",
            Tokens = new List<string> { "original", "text" },
            TokenCount = 2
        });
        _db.TenantFileStates.Add(new TenantFileState
        {
            Id = $"{tenantId}#{fileName}",
            TenantId = tenantId,
            FileName = fileName,
            ContentHash = "old-hash"
        });
        await _db.SaveChangesAsync();

        var newContent = "# Question A\nModified text.\n\n# Question B\nNew section.";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(newContent));
        var newHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        _db.TenantFiles.Add(new TenantFile
        {
            TenantId = tenantId,
            FileName = fileName,
            ContentHash = newHash,
            S3Key = $"/{tenantId}/raw/{fileName}",
            IsIndexed = false
        });

        var taskId = Guid.NewGuid();
        _db.IndexCompilationJobs.Add(new IndexCompilationJob
        {
            TaskId = taskId,
            TenantId = tenantId,
            Status = "Pending",
            S3StoragePath = $"/{tenantId}/raw/",
            FileNames = new List<string> { fileName }
        });
        await _db.SaveChangesAsync();

        _mockStorage.DownloadAsync(tenantId, fileName, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(newContent));

        var pipeline = new CompilationPipeline(_mockStorage, _db, _mockRedis, _mockEventPublisher, _config);
        var payload = new CompileKnowledgeTaskPayload(taskId, tenantId, $"/{tenantId}/raw/", new List<string> { fileName }, DateTime.UtcNow);

        // Act
        await pipeline.ExecuteAsync(payload, CancellationToken.None);

        // Assert
        var sections = await _db.TenantSections.Where(s => s.TenantId == tenantId).OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(2, sections.Count);
        
        Assert.Equal($"{tenantId}#{fileName}#question-a", sections[0].Id);
        Assert.Equal("Modified text.", sections[0].Content);

        Assert.Equal($"{tenantId}#{fileName}#question-b", sections[1].Id);
        Assert.Equal("New section.", sections[1].Content);

        var logs = await _db.IndexAuditLogs.Where(l => l.TenantId == tenantId).ToListAsync();
        Assert.Contains(logs, l => l.ActionType == "ADDED" && l.CommitMessage.Contains("Question B"));
        Assert.Contains(logs, l => l.ActionType == "MODIFIED" && l.CommitMessage.Contains("Question A"));
    }
}

public class NotificationApiIntegrationTests : IClassFixture<WebApplicationFactory<CloudKB.ApiService.Notification.Program>>
{
    private readonly WebApplicationFactory<CloudKB.ApiService.Notification.Program> _factory;
    private readonly IConnectionMultiplexer _mockRedis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _mockRedisDb = Substitute.For<IDatabase>();
    private readonly ISubscriber _mockSubscriber = Substitute.For<ISubscriber>();

    public NotificationApiIntegrationTests(WebApplicationFactory<CloudKB.ApiService.Notification.Program> factory)
    {
        _mockRedis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_mockRedisDb);
        _mockRedis.GetSubscriber(Arg.Any<object>()).Returns(_mockSubscriber);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                var dbDescriptors = services.Where(d =>
                    d.ServiceType == typeof(CloudKbDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<CloudKbDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") == true)
                    .ToList();

                foreach (var descriptor in dbDescriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<CloudKbDbContext>(options =>
                {
                    options.UseInMemoryDatabase("NotificationApiTestDb");
                });

                var redisDescriptors = services.Where(d =>
                    d.ServiceType == typeof(IConnectionMultiplexer))
                    .ToList();

                foreach (var descriptor in redisDescriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddSingleton<IConnectionMultiplexer>(_mockRedis);
            });
        });
    }

    [Fact]
    public async Task Health_ShouldReturnHealthy()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", content);
    }

    [Fact]
    public async Task GetLogs_ShouldReturnAuditLogsFromDatabase()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "tenant-01");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CloudKbDbContext>();
            db.IndexAuditLogs.Add(new IndexAuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-01",
                FileName = "test.md",
                ActionType = "ADDED",
                SectionsAffected = 3,
                CommitMessage = "Added 3 sections",
                LoggedAt = DateTime.UtcNow
            });
            db.IndexAuditLogs.Add(new IndexAuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-02",
                FileName = "test.md",
                ActionType = "ADDED",
                SectionsAffected = 1,
                CommitMessage = "Should not return this",
                LoggedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync("/api/notifications/logs");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var logs = await response.Content.ReadFromJsonAsync<List<IndexAuditLogResponse>>();
        Assert.NotNull(logs);
        Assert.Single(logs);
        Assert.Equal("Added 3 sections", logs[0].CommitMessage);
    }

    [Fact]
    public async Task GetLogs_WithoutUserIdHeader_ShouldReturn401Unauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/notifications/logs");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetStream_WithoutUserIdHeader_ShouldReturn401Unauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/notifications/stream");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetStream_ShouldReceiveEventsFromRedisPubSub()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", "tenant-01");

        Action<RedisChannel, RedisValue>? redisCallback = null;

        await _mockSubscriber.SubscribeAsync(
            Arg.Is<RedisChannel>(c => c.ToString() == "ch:notifications:tenant-01"),
            Arg.Do<Action<RedisChannel, RedisValue>>(callback => redisCallback = callback));

        // Act
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/stream");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        await Task.Delay(150);

        Assert.NotNull(redisCallback);

        var eventPayload = "{\"eventType\":\"IndexProcessing\",\"taskId\":\"test-task-id\",\"message\":\"Compilation started\"}";
        redisCallback(RedisChannel.Literal("ch:notifications:tenant-01"), eventPayload);

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        // Read initial connection established comment
        var initLine1 = await reader.ReadLineAsync();
        var initLine2 = await reader.ReadLineAsync();

        var line1 = await reader.ReadLineAsync();
        var line2 = await reader.ReadLineAsync();
        var line3 = await reader.ReadLineAsync();

        // Assert
        Assert.Equal(":", initLine1);
        Assert.Equal("", initLine2);
        Assert.Equal("event: IndexProcessing", line1);
        Assert.Equal($"data: {eventPayload}", line2);
        Assert.Equal("", line3);
    }
}



