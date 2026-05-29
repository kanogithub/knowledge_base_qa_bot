using Amazon.S3;
using CloudKB.Infrastructure;
using CloudKB.Worker.Indexer.Consumers;
using CloudKB.Worker.Indexer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

// Add service defaults
builder.AddServiceDefaults();

// Database Context
builder.Services.AddDbContext<CloudKbDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("cloudkb")));

// Redis Multiplexer
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("redis") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connectionString);
});

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
builder.Services.AddSingleton<RedisEventPublisher>();
builder.Services.AddScoped<CompilationPipeline>();

// Register Hosted consumer
builder.Services.AddHostedService<IndexCompilationConsumer>();

var host = builder.Build();
host.Run();
