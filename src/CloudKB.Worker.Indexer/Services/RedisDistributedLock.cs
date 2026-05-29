using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace CloudKB.Worker.Indexer.Services;

public class LockTimeoutException : Exception
{
    public LockTimeoutException(string message) : base(message) { }
}

public class RedisDistributedLock : IAsyncDisposable
{
    private readonly IDatabase _redis;
    private readonly string _lockKey;
    private readonly string _lockValue;

    private RedisDistributedLock(IDatabase redis, string lockKey, string lockValue)
    {
        _redis = redis;
        _lockKey = lockKey;
        _lockValue = lockValue;
    }

    public static async Task<RedisDistributedLock> AcquireAsync(
        IDatabase redis, string tenantId, TimeSpan ttl, CancellationToken ct)
    {
        var lockKey = $"lock:index:{tenantId}";
        var lockValue = Guid.NewGuid().ToString();

        // Retry with exponential backoff: 1s, 2s, 4s
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            if (await redis.StringSetAsync(lockKey, lockValue, ttl, When.NotExists))
            {
                return new RedisDistributedLock(redis, lockKey, lockValue);
            }

            var delayMs = (int)(Math.Pow(2, attempt) * 1000);
            await Task.Delay(delayMs, ct);
        }

        throw new LockTimeoutException($"Cannot acquire distributed lock for tenant {tenantId} on key {lockKey}.");
    }

    public async ValueTask DisposeAsync()
    {
        // Release the lock safely using a Lua script to compare value and delete key atomically
        var script = "if redis.call('get',KEYS[1]) == ARGV[1] then return redis.call('del',KEYS[1]) else return 0 end";
        await _redis.ScriptEvaluateAsync(script, new RedisKey[] { _lockKey }, new RedisValue[] { _lockValue });
    }
}
