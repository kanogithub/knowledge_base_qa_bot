using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CloudKB.SharedKernel;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace CloudKB.Infrastructure;

public class SeedDataService
{
    private readonly CloudKbDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;

    public SeedDataService(CloudKbDbContext dbContext, IConnectionMultiplexer redis)
    {
        _dbContext = dbContext;
        _redis = redis;
    }

    public async Task SeedAsync(string tenantId, string markdownDocsFolder)
    {
        if (!Directory.Exists(markdownDocsFolder))
        {
            Console.WriteLine($"Seeding folder not found: {markdownDocsFolder}");
            return;
        }

        var files = Directory.GetFiles(markdownDocsFolder, "*.md");
        if (files.Length == 0)
        {
            Console.WriteLine("No markdown files found to seed.");
            return;
        }

        Console.WriteLine($"Found {files.Length} files to seed for tenant {tenantId}.");

        var allSections = new List<TenantSection>();

        // Clear existing sections and state for this tenant
        await _dbContext.TenantSections.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync();
        await _dbContext.TenantFiles.Where(f => f.TenantId == tenantId).ExecuteDeleteAsync();
        await _dbContext.TenantFileStates.Where(fs => fs.TenantId == tenantId).ExecuteDeleteAsync();
        await _dbContext.IndexAuditLogs.Where(al => al.TenantId == tenantId).ExecuteDeleteAsync();

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var content = await File.ReadAllTextAsync(file);

            // Compute file hash
            var contentHash = ComputeSha256(content);
            var fileSizeBytes = new FileInfo(file).Length;

            // 1. Parse sections using MarkdownParser
            var parsed = MarkdownParser.Parse(tenantId, fileName, content);

            foreach (var p in parsed)
            {
                allSections.Add(new TenantSection
                {
                    Id = p.SectionId,
                    TenantId = p.TenantId,
                    FileName = p.FileName,
                    Heading = p.Heading,
                    HeadingPath = p.HeadingPath,
                    Content = p.Content,
                    Tokens = p.Tokens.ToList(),
                    TokenCount = p.TokenCount,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            // 2. Insert File Registry Records
            _dbContext.TenantFiles.Add(new TenantFile
            {
                TenantId = tenantId,
                FileName = fileName,
                S3Key = $"/{tenantId}/raw/{fileName}",
                FileSizeBytes = fileSizeBytes,
                ContentHash = contentHash,
                IsIndexed = true,
                LastIndexedAt = DateTime.UtcNow
            });

            // 3. Save File State Snapshot
            _dbContext.TenantFileStates.Add(new TenantFileState
            {
                Id = $"{tenantId}#{fileName}",
                TenantId = tenantId,
                FileName = fileName,
                ContentHash = contentHash,
                LastIndexedAt = DateTime.UtcNow
            });

            // 4. Log "ADDED" audit changelog
            _dbContext.IndexAuditLogs.Add(new IndexAuditLog
            {
                TenantId = tenantId,
                FileName = fileName,
                ActionType = "ADDED",
                SectionsAffected = parsed.Count,
                CommitMessage = $"Initially seeded knowledge base file: {fileName} with {parsed.Count} sections."
            });
        }

        // Batch save to PostgreSQL
        await _dbContext.TenantSections.AddRangeAsync(allSections);
        await _dbContext.SaveChangesAsync();

        // 5. Compute BM25 statistics and cache TenantKbIndex to Redis
        if (allSections.Count > 0)
        {
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

            var db = _redis.GetDatabase();
            var redisKey = $"kb:index:{tenantId}";
            var json = JsonSerializer.Serialize(kbIndex);

            await db.StringSetAsync(redisKey, json);
            Console.WriteLine($"Successfully cached BM25 index in Redis under key: {redisKey}");
        }
    }

    private static string ComputeSha256(string text)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
