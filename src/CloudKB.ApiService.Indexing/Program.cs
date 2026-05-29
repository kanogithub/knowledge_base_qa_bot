using Amazon.S3;
using CloudKB.ApiService.Indexing.Services;
using CloudKB.Infrastructure;
using CloudKB.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults
builder.AddServiceDefaults();

// Database
builder.Services.AddDbContext<CloudKbDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("cloudkb")));

// RabbitMQ Connection
builder.Services.AddSingleton<IConnection>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("rabbitmq") ?? "amqp://guest:guest@localhost:5672/";
    var factory = new ConnectionFactory
    {
        Uri = new Uri(connectionString),
        DispatchConsumersAsync = true
    };

    var retryCount = 10;
    var delay = TimeSpan.FromSeconds(2);
    for (int i = 0; i < retryCount; i++)
    {
        try
        {
            return factory.CreateConnection();
        }
        catch (Exception) when (i < retryCount - 1)
        {
            Console.WriteLine($"RabbitMQ not ready yet. Retrying in {delay.TotalSeconds}s... (Attempt {i + 1}/{retryCount})");
            Thread.Sleep(delay);
        }
    }
    return factory.CreateConnection();
});


// Storage Configuration
var storageProvider = builder.Configuration["Storage:Provider"] ?? "Local";
if (storageProvider.Equals("AWS", StringComparison.OrdinalIgnoreCase) || storageProvider.Equals("S3", StringComparison.OrdinalIgnoreCase))
{
    // AWS S3 client
    builder.Services.AddSingleton<IAmazonS3>(sp =>
    {
        var endpoint = builder.Configuration.GetConnectionString("minio") ?? "http://localhost:9000";
        var s3Config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true
        };
        return new AmazonS3Client("cloudkb_admin", "cloudkb_secret_password", s3Config);
    });
    builder.Services.AddSingleton<IStorageService, S3StorageService>();
}
else
{
    builder.Services.AddSingleton<IStorageService, LocalStorageService>();
}

// Custom Services
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddScoped<IIndexingService, IndexingService>();

var app = builder.Build();

app.MapDefaultEndpoints();

// POST /api/index
app.MapPost("/api/index", async (
    HttpContext httpContext,
    IIndexingService indexingService,
    CancellationToken ct) =>
{
    var tenantId = httpContext.Request.Headers["X-User-Id"].ToString();
    if (string.IsNullOrWhiteSpace(tenantId))
        return Results.Problem("Missing X-User-Id header", statusCode: 401);

    if (!httpContext.Request.HasFormContentType)
        return Results.Problem("Request must be multipart/form-data", statusCode: 400);

    IFormFileCollection files;
    try
    {
        files = httpContext.Request.Form.Files;
    }
    catch (Exception)
    {
        return Results.Problem("Invalid form data", statusCode: 400);
    }

    if (files == null || files.Count == 0)
        return Results.Problem("No files provided", statusCode: 400);

    // Validate all files are .md
    if (files.Any(f => !f.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        return Results.Problem("Only .md files are accepted", statusCode: 400);

    var result = await indexingService.IngestAsync(tenantId, files, ct);
    return Results.Accepted(value: result); // HTTP 202
})
.DisableAntiforgery();


// Apply Migrations on Startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CloudKbDbContext>();
    if (dbContext.Database.IsRelational())
    {
        await dbContext.Database.MigrateAsync();
    }
}

app.Run();

namespace CloudKB.ApiService.Indexing
{
    public partial class Program { }
}
