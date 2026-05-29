using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using CloudKB.Infrastructure;
using CloudKB.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace CloudKB.Worker.Indexer.Services;

public class CompilationPipeline
{
    private readonly IStorageService _storageService;
    private readonly CloudKbDbContext _dbContext;

    private readonly IConnectionMultiplexer _redis;
    private readonly RedisEventPublisher _eventPublisher;
    private readonly IConfiguration _config;

    public CompilationPipeline(
        IStorageService storageService,
        CloudKbDbContext dbContext,
        IConnectionMultiplexer redis,
        RedisEventPublisher eventPublisher,
        IConfiguration config)
    {
        _storageService = storageService;
        _dbContext = dbContext;
        _redis = redis;
        _eventPublisher = eventPublisher;
        _config = config;
    }

    public async Task ExecuteAsync(CompileKnowledgeTaskPayload task, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var tenantId = task.TenantId;
        var taskIdStr = task.TaskId.ToString();
        
        // 1. Update IndexCompilationJob: status = "Processing", startedAt = now
        var job = await _dbContext.IndexCompilationJobs.FirstOrDefaultAsync(j => j.TaskId == task.TaskId, ct);
        if (job != null)
        {
            job.Status = "Processing";
            job.StartedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
        }

        RedisDistributedLock? redisLock = null;
        bool anyCompiled = false;
        int totalSectionsCompiled = 0;
        int totalFilesProcessed = 0;

        try
        {
            // 2. Acquire distributed lock
            var redisDb = _redis.GetDatabase();
            redisLock = await RedisDistributedLock.AcquireAsync(redisDb, tenantId, TimeSpan.FromSeconds(300), ct);

            // 3. Publish IndexProgressEvent
            await _eventPublisher.PublishProgressAsync(tenantId, taskIdStr, "正在啟動增量檢查與知識編譯...");

            foreach (var fileName in task.FileNames)
            {
                totalFilesProcessed++;

                // 4a. Get content_hash from TenantFile in PostgreSQL (uploaded in Task 2)
                var tenantFile = await _dbContext.TenantFiles
                    .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.FileName == fileName, ct);
                
                if (tenantFile == null)
                {
                    continue; // File registry record not found, skip
                }

                var newHash = tenantFile.ContentHash;

                // 4b. Fetch TenantFileState record from database
                var fileStateKey = $"{tenantId}#{fileName}";
                var fileState = await _dbContext.TenantFileStates.FindAsync(new object[] { fileStateKey }, ct);

                // 4c. [🔍 Stage 1: Fast-Pass Skip Check]
                if (fileState != null && fileState.ContentHash == newHash)
                {
                    Console.WriteLine($"File {fileName} hash matches. Skipping incremental split.");
                    continue;
                }

                anyCompiled = true;

                // 4d. If hash differs or no previous state exists: Download file from storage
                string markdownContent = await _storageService.DownloadAsync(tenantId, fileName, ct);

                // 4e. Parse Markdown into sections
                var parsedSections = MarkdownParser.Parse(tenantId, fileName, markdownContent);
                totalSectionsCompiled += parsedSections.Count;

                // 4f. [✂️ Stage 2: Section Diffing]
                var oldDbSections = await _dbContext.TenantSections
                    .Where(s => s.TenantId == tenantId && s.FileName == fileName)
                    .ToListAsync(ct);

                // ADDED: in parsedSections but not in oldDbSections
                var toAdd = new List<TenantSection>();
                foreach (var p in parsedSections)
                {
                    if (!oldDbSections.Any(o => o.Id == p.SectionId))
                    {
                        toAdd.Add(new TenantSection
                        {
                            Id = p.SectionId,
                            TenantId = tenantId,
                            FileName = fileName,
                            Heading = p.Heading,
                            HeadingPath = p.HeadingPath,
                            Content = p.Content,
                            Tokens = p.Tokens.ToList(),
                            TokenCount = p.TokenCount,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });

                        _dbContext.IndexAuditLogs.Add(new IndexAuditLog
                        {
                            TenantId = tenantId,
                            FileName = fileName,
                            ActionType = "ADDED",
                            SectionsAffected = 1,
                            CommitMessage = $"Added Heading: # {p.Heading}"
                        });
                    }
                }
                await _dbContext.TenantSections.AddRangeAsync(toAdd, ct);

                // MODIFIED: in both, but Content or Tokens differ
                foreach (var p in parsedSections)
                {
                    var old = oldDbSections.FirstOrDefault(o => o.Id == p.SectionId);
                    if (old != null)
                    {
                        if (old.Content != p.Content || old.TokenCount != p.TokenCount || !old.Tokens.SequenceEqual(p.Tokens))
                        {
                            old.Content = p.Content;
                            old.Tokens = p.Tokens.ToList();
                            old.TokenCount = p.TokenCount;
                            old.Heading = p.Heading;
                            old.HeadingPath = p.HeadingPath;
                            old.UpdatedAt = DateTime.UtcNow;

                            _dbContext.IndexAuditLogs.Add(new IndexAuditLog
                            {
                                TenantId = tenantId,
                                FileName = fileName,
                                ActionType = "MODIFIED",
                                SectionsAffected = 1,
                                CommitMessage = $"Modified Heading: # {p.Heading}"
                            });
                        }
                    }
                }

                // DELETED: in oldDbSections but not in parsedSections
                var toDelete = new List<TenantSection>();
                foreach (var old in oldDbSections)
                {
                    if (!parsedSections.Any(n => n.SectionId == old.Id))
                    {
                        toDelete.Add(old);

                        _dbContext.IndexAuditLogs.Add(new IndexAuditLog
                        {
                            TenantId = tenantId,
                            FileName = fileName,
                            ActionType = "DELETED",
                            SectionsAffected = 1,
                            CommitMessage = $"Deleted Heading: # {old.Heading}"
                        });
                    }
                }
                _dbContext.TenantSections.RemoveRange(toDelete);

                // 4h. Upsert TenantFileState record
                if (fileState != null)
                {
                    fileState.ContentHash = newHash;
                    fileState.LastIndexedAt = DateTime.UtcNow;
                }
                else
                {
                    _dbContext.TenantFileStates.Add(new TenantFileState
                    {
                        Id = fileStateKey,
                        TenantId = tenantId,
                        FileName = fileName,
                        ContentHash = newHash,
                        LastIndexedAt = DateTime.UtcNow
                    });
                }

                // 4i. Update TenantFile record
                tenantFile.IsIndexed = true;
                tenantFile.LastIndexedAt = DateTime.UtcNow;
            }

            // Save all section modifications to PostgreSQL
            await _dbContext.SaveChangesAsync(ct);

            // 5. [🔄 Stage 3: BM25 Re-aggregation & Cache Update]
            if (anyCompiled)
            {
                await _eventPublisher.PublishProgressAsync(tenantId, taskIdStr, "正在重新計算 BM25 統計指標與快取更新...");

                var allSections = await _dbContext.TenantSections
                    .Where(s => s.TenantId == tenantId)
                    .ToListAsync(ct);

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

                    var redisKey = $"kb:index:{tenantId}";
                    var json = JsonSerializer.Serialize(kbIndex, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    await redisDb.StringSetAsync(redisKey, json);
                    Console.WriteLine($"Successfully updated cached index for tenant {tenantId} in Redis.");
                }
                else
                {
                    // Clean up cache if no sections left
                    await redisDb.KeyDeleteAsync($"kb:index:{tenantId}");
                }
            }

            // 7. Update IndexCompilationJob: status = "Completed"
            if (job != null)
            {
                job.Status = "Completed";
                job.CompletedAt = DateTime.UtcNow;
                job.SectionsCompiled = totalSectionsCompiled;
                job.FilesProcessed = totalFilesProcessed;
                job.CompileDurationMs = (int)stopwatch.ElapsedMilliseconds;
                await _dbContext.SaveChangesAsync(ct);
            }

            // 8. Publish IndexCompletedEvent
            await _eventPublisher.PublishCompletedAsync(
                tenantId,
                taskIdStr,
                "您的知識庫已編譯完成！",
                totalSectionsCompiled,
                totalFilesProcessed
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Compilation failed: {ex.Message}\n{ex.StackTrace}");

            // Update IndexCompilationJob to Failed
            if (job != null)
            {
                job.Status = "Failed";
                job.CompletedAt = DateTime.UtcNow;
                job.ErrorCode = ex is LockTimeoutException ? "LOCK_TIMEOUT" : "PARSE_ERROR";
                job.ErrorDetail = ex.Message;
                try
                {
                    await _dbContext.SaveChangesAsync(ct);
                }
                catch { /* Ignore double fault */ }
            }

            // Publish IndexFailedEvent
            await _eventPublisher.PublishFailedAsync(
                tenantId,
                taskIdStr,
                "知識庫編譯失敗，請稍後重試。",
                ex is LockTimeoutException ? "LOCK_TIMEOUT" : "PARSE_ERROR",
                ex.Message
            );

            throw; // Re-throw for RabbitMQ DLQ processing
        }
        finally
        {
            // 6. Release distributed lock
            if (redisLock != null)
            {
                await redisLock.DisposeAsync();
            }
        }
    }
}
