using System;
using System.Linq;
using CloudKB.Infrastructure;
using CloudKB.ApiService.Notification.Services;
using CloudKB.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults
builder.AddServiceDefaults();

// Configure Kestrel to detect disconnections quickly (10 minutes)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(10);
});

// Database
builder.Services.AddDbContext<CloudKbDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("cloudkb")));

// Redis Multiplexer
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("redis") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connectionString);
});

// Custom Services
builder.Services.AddScoped<INotificationStreamService, NotificationStreamService>();

var app = builder.Build();

app.MapDefaultEndpoints();

// GET /api/notifications/stream
app.MapGet("/api/notifications/stream", async (
    HttpContext httpContext,
    INotificationStreamService streamService) =>
{
    var tenantId = httpContext.Request.Headers["X-User-Id"].ToString();
    
    if (string.IsNullOrWhiteSpace(tenantId))
        return Results.Problem("Missing X-User-Id header", statusCode: 401);
    
    // Set SSE response headers
    httpContext.Response.ContentType = "text/event-stream; charset=utf-8";
    httpContext.Response.Headers.CacheControl = "no-cache";
    httpContext.Response.Headers.Connection = "keep-alive";
    
    // Disable response buffering for real-time streaming
    var bufferingFeature = httpContext.Features.Get<IHttpResponseBodyFeature>();
    bufferingFeature?.DisableBuffering();
    
    // Immediately send an initial comment to flush headers in TestHost
    await httpContext.Response.WriteAsync(":\n\n", httpContext.RequestAborted);
    await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
    
    await streamService.StreamEventsAsync(tenantId, httpContext.Response, httpContext.RequestAborted);
    return Results.Empty;
});

// GET /api/notifications/logs
app.MapGet("/api/notifications/logs", async (
    HttpContext httpContext,
    CloudKbDbContext dbContext) =>
{
    var tenantId = httpContext.Request.Headers["X-User-Id"].ToString();
    
    if (string.IsNullOrWhiteSpace(tenantId))
        return Results.Problem("Missing X-User-Id header", statusCode: 401);
        
    var logs = await dbContext.IndexAuditLogs
        .Where(l => l.TenantId == tenantId)
        .OrderByDescending(l => l.LoggedAt)
        .Select(l => new IndexAuditLogResponse(
            l.Id,
            l.FileName,
            l.ActionType,
            l.SectionsAffected,
            l.CommitMessage,
            l.LoggedAt
        ))
        .ToListAsync();
        
    return Results.Ok(logs);
});

app.Run();

namespace CloudKB.ApiService.Notification
{
    public partial class Program { }
}
