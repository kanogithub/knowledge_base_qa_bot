using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using StackExchange.Redis;

namespace CloudKB.ApiService.Notification.Services;

public class NotificationStreamService : INotificationStreamService
{
    private readonly IConnectionMultiplexer _redis;

    public NotificationStreamService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task StreamEventsAsync(string tenantId, HttpResponse response, CancellationToken ct)
    {
        Console.WriteLine($"[StreamService] StreamEventsAsync started for tenant {tenantId}");
        var subscriber = _redis.GetSubscriber();
        var channel = RedisChannel.Literal($"ch:notifications:{tenantId}");
        
        // Channel to pass events from the Redis callback to the SSE writer
        var eventQueue = Channel.CreateUnbounded<string>();
        
        // Subscribe to the tenant's Redis Pub/Sub channel
        await subscriber.SubscribeAsync(channel, (ch, message) =>
        {
            Console.WriteLine($"[StreamService] Redis Callback invoked. HasValue: {message.HasValue}, message: {message}");
            if (message.HasValue)
            {
                var success = eventQueue.Writer.TryWrite(message.ToString());
                Console.WriteLine($"[StreamService] TryWrite to eventQueue success: {success}");
            }
        });
        
        try
        {
            await WriteEventsLoopAsync(response, eventQueue.Reader, ct);
        }
        finally
        {
            Console.WriteLine("[StreamService] StreamEventsAsync finally block, unsubscribing...");
            // Clean up subscription when client disconnects
            await subscriber.UnsubscribeAsync(channel);
        }
    }

    private async Task WriteEventsLoopAsync(
        HttpResponse response, 
        ChannelReader<string> eventReader, 
        CancellationToken ct)
    {
        var keepAliveInterval = TimeSpan.FromSeconds(30);
        Console.WriteLine("[StreamService] WriteEventsLoopAsync started");
        
        while (!ct.IsCancellationRequested)
        {
            // Wait for either an event or a keep-alive timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(keepAliveInterval);
            
            try
            {
                Console.WriteLine("[StreamService] Waiting to read from eventQueue...");
                if (await eventReader.WaitToReadAsync(timeoutCts.Token))
                {
                    Console.WriteLine("[StreamService] WaitToReadAsync returned true");
                    while (eventReader.TryRead(out var rawJson))
                    {
                        Console.WriteLine($"[StreamService] Reading item: {rawJson}");
                        await WriteEventFrameAsync(response, rawJson, ct);
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine("[StreamService] WaitToReadAsync timed out, writing keep-alive ping");
                // Timeout — send keep-alive ping
                await WriteKeepAliveAsync(response, ct);
            }
        }
    }

    private static async Task WriteEventFrameAsync(HttpResponse response, string rawJson, CancellationToken ct)
    {
        // Parse the JSON to extract eventType for the SSE event field
        using var doc = JsonDocument.Parse(rawJson);
        var eventType = doc.RootElement.GetProperty("eventType").GetString();
        
        // Write SSE frame:
        //   event: IndexProcessing
        //   data: {"taskId":"...","message":"..."}
        //   \n
        await response.WriteAsync($"event: {eventType}\n", ct);
        await response.WriteAsync($"data: {rawJson}\n", ct);
        await response.WriteAsync("\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static async Task WriteKeepAliveAsync(HttpResponse response, CancellationToken ct)
    {
        // SSE comment line — ignored by EventSource clients but keeps connection alive
        await response.WriteAsync(":ping\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
