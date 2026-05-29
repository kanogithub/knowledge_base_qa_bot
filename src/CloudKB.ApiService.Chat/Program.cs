using CloudKB.ApiService.Chat.Services;
using CloudKB.Infrastructure;
using CloudKB.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults
builder.AddServiceDefaults();

// Database
builder.Services.AddDbContext<CloudKbDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("cloudkb")));

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("redis") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connectionString);
});

// Chat client configuration
builder.Services.AddSingleton<LlmClientFactory>();
builder.Services.AddSingleton<IChatClient>(sp => sp.GetRequiredService<LlmClientFactory>().CreateClient());


// Custom Services
builder.Services.AddScoped<RedisIndexLoader>();
builder.Services.AddScoped<IChatService, ChatService>();

var app = builder.Build();

// Map default endpoints
app.MapDefaultEndpoints();

// POST /api/chat
app.MapPost("/api/chat", async (
    HttpContext httpContext,
    ChatRequest request,
    IChatService chatService) =>
{
    var tenantId = httpContext.Request.Headers["X-User-Id"].ToString();
    
    if (string.IsNullOrWhiteSpace(tenantId))
        return Results.Problem("Missing X-User-Id header", statusCode: 401);
    
    if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Length > 2000)
        return Results.Problem("Invalid query", statusCode: 400);
    
    httpContext.Response.ContentType = "text/event-stream; charset=utf-8";
    httpContext.Response.Headers.CacheControl = "no-cache";
    httpContext.Response.Headers.Connection = "keep-alive";
    
    await chatService.StreamAnswerAsync(tenantId, request.Query, httpContext.Response, httpContext.RequestAborted);
    return Results.Empty;
});

app.Run();

namespace CloudKB.ApiService.Chat
{
    public partial class Program { }
}
