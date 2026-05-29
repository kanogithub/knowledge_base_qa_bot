using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CloudKB.Infrastructure;
using CloudKB.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CloudKB.ApiService.Indexing.Services;

public interface IIndexingService
{
    Task<IndexAcceptedResponse> IngestAsync(string tenantId, IFormFileCollection files, CancellationToken ct);
}

public class IndexingService : IIndexingService
{
    private readonly IStorageService _storageService;
    private readonly RabbitMqPublisher _publisher;
    private readonly CloudKbDbContext _dbContext;

    public IndexingService(
        IStorageService storageService,
        RabbitMqPublisher publisher,
        CloudKbDbContext dbContext)
    {
        _storageService = storageService;
        _publisher = publisher;
        _dbContext = dbContext;
    }

    public async Task<IndexAcceptedResponse> IngestAsync(string tenantId, IFormFileCollection files, CancellationToken ct)
    {
        var taskId = Guid.NewGuid();
        var fileNames = new List<string>();

        foreach (var file in files)
        {
            fileNames.Add(file.FileName);
            
            // 1. Calculate SHA256 of file stream
            string contentHash;
            using (var stream = file.OpenReadStream())
            {
                using var sha256 = SHA256.Create();
                var hashBytes = await sha256.ComputeHashAsync(stream, ct);
                contentHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

                // Seek back to start for S3 upload
                if (stream.CanSeek)
                {
                    stream.Seek(0, SeekOrigin.Begin);
                }

                // 2. Stream upload to MinIO/S3
                await _storageService.UploadAsync(tenantId, file.FileName, stream, ct);
            }

            // 3. Upsert TenantFile tracking record
            var existingFile = await _dbContext.TenantFiles
                .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.FileName == file.FileName, ct);

            if (existingFile != null)
            {
                existingFile.ContentHash = contentHash;
                existingFile.FileSizeBytes = file.Length;
                existingFile.S3Key = $"/{tenantId}/raw/{file.FileName}";
                existingFile.IsIndexed = false;
                existingFile.LastIndexedAt = null;
                existingFile.UploadedAt = DateTime.UtcNow;
            }
            else
            {
                _dbContext.TenantFiles.Add(new TenantFile
                {
                    TenantId = tenantId,
                    FileName = file.FileName,
                    S3Key = $"/{tenantId}/raw/{file.FileName}",
                    FileSizeBytes = file.Length,
                    ContentHash = contentHash,
                    IsIndexed = false,
                    LastIndexedAt = null,
                    UploadedAt = DateTime.UtcNow
                });
            }
        }

        // 4. Create IndexCompilationJob record
        var job = new IndexCompilationJob
        {
            TaskId = taskId,
            TenantId = tenantId,
            Status = "Pending",
            S3StoragePath = $"/{tenantId}/raw/",
            FileNames = fileNames,
            RequestedAt = DateTime.UtcNow
        };
        _dbContext.IndexCompilationJobs.Add(job);

        // Save changes to PostgreSQL
        await _dbContext.SaveChangesAsync(ct);

        // 5. Publish CompileKnowledgeTask payload to RabbitMQ
        var payload = new CompileKnowledgeTaskPayload(
            TaskId: taskId,
            TenantId: tenantId,
            S3StoragePath: $"/{tenantId}/raw/",
            FileNames: fileNames,
            RequestedAt: DateTime.UtcNow
        );
        await _publisher.PublishCompileTaskAsync(payload, ct);

        // 6. Return response
        return new IndexAcceptedResponse(taskId, "Knowledge compilation job enqueued.");
    }
}
