using System.Text.Json;
using System.Threading.Tasks;
using CloudKB.SharedKernel;
using StackExchange.Redis;

namespace CloudKB.Worker.Indexer.Services;

public class RedisEventPublisher
{
    private readonly IConnectionMultiplexer _redis;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RedisEventPublisher(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task PublishProgressAsync(string tenantId, string taskId, string message)
    {
        var db = _redis.GetDatabase();
        var channel = $"ch:notifications:{tenantId}";
        var payload = new IndexProgressPayload(
            EventType: "IndexProcessing",
            TaskId: taskId,
            Message: message
        );
        var json = JsonSerializer.Serialize(payload, Options);
        await _redis.GetSubscriber().PublishAsync(RedisChannel.Literal(channel), json);
    }

    public async Task PublishCompletedAsync(string tenantId, string taskId, string message, int sectionsCompiled, int filesProcessed)
    {
        var channel = $"ch:notifications:{tenantId}";
        var metadata = new IndexCompletedMetadata(
            SectionsCompiled: sectionsCompiled,
            FilesProcessed: filesProcessed
        );
        var payload = new IndexCompletedPayload(
            EventType: "IndexCompleted",
            TaskId: taskId,
            Message: message,
            Metadata: metadata
        );
        var json = JsonSerializer.Serialize(payload, Options);
        await _redis.GetSubscriber().PublishAsync(RedisChannel.Literal(channel), json);
    }

    public async Task PublishFailedAsync(string tenantId, string taskId, string message, string errorCode, string errorDetail)
    {
        var channel = $"ch:notifications:{tenantId}";
        var payload = new IndexFailedPayload(
            EventType: "IndexFailed",
            TaskId: taskId,
            Message: message,
            ErrorCode: errorCode,
            ErrorDetail: errorDetail
        );
        var json = JsonSerializer.Serialize(payload, Options);
        await _redis.GetSubscriber().PublishAsync(RedisChannel.Literal(channel), json);
    }
}
