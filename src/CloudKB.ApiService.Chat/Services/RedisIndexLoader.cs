using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CloudKB.Infrastructure;
using CloudKB.SharedKernel;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace CloudKB.ApiService.Chat.Services;

public class RedisIndexLoader
{
    private readonly IConnectionMultiplexer _redis;
    private readonly CloudKbDbContext _dbContext;

    public RedisIndexLoader(IConnectionMultiplexer redis, CloudKbDbContext dbContext)
    {
        _redis = redis;
        _dbContext = dbContext;
    }

    public async Task<TenantKbIndex?> LoadAsync(string tenantId)
    {
        var db = _redis.GetDatabase();
        var redisKey = $"kb:index:{tenantId}";
        var value = await db.StringGetAsync(redisKey);

        if (value.HasValue)
        {
            return JsonSerializer.Deserialize<TenantKbIndex>(value.ToString(), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }

        // Cache miss: Load from PostgreSQL and rebuild cache
        var allSections = await _dbContext.TenantSections
            .Where(s => s.TenantId == tenantId)
            .ToListAsync();

        if (allSections.Count == 0)
        {
            return null;
        }

        var avgdl = allSections.Average(s => s.TokenCount);

        var kbIndex = new TenantKbIndex(
            TenantId: tenantId,
            TotalDocuments: allSections.Count,
            AverageDocumentLength: avgdl,
            LastUpdatedAt: DateTime.UtcNow,
            Sections: allSections.Select(s => new IndexedSectionMeta(
                SectionId: s.Id,
                FileName: s.FileName,
                Heading: s.Heading,
                HeadingPath: s.HeadingPath,
                TokenCount: s.TokenCount,
                TermFrequencies: s.Tokens
                    .GroupBy(t => t)
                    .ToDictionary(g => g.Key, g => g.Count())
            )).ToList()
        );

        // Save back to Redis cache
        var json = JsonSerializer.Serialize(kbIndex, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await db.StringSetAsync(redisKey, json);

        return kbIndex;
    }
}
