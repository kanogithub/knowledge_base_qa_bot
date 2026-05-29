using System;
using System.Collections.Generic;

namespace CloudKB.Infrastructure;

public class TenantSection
{
    public string Id { get; set; } = null!; // Composite PK: {tenant_id}#{file_name}#{slugified_heading}
    public string TenantId { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string Heading { get; set; } = null!;
    public List<string> HeadingPath { get; set; } = new();
    public string Content { get; set; } = null!;
    public List<string> Tokens { get; set; } = new();
    public int TokenCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class IndexCompilationJob
{
    public Guid TaskId { get; set; } // PK
    public string TenantId { get; set; } = null!;
    public string Status { get; set; } = "Pending"; // Pending, Processing, Completed, Failed
    public string S3StoragePath { get; set; } = null!;
    public List<string> FileNames { get; set; } = new();
    public int? SectionsCompiled { get; set; }
    public int? FilesProcessed { get; set; }
    public int? CompileDurationMs { get; set; }
    public string? ErrorCode { get; set; } // LOCK_TIMEOUT, S3_READ_FAILURE, PARSE_ERROR, DB_WRITE_FAILURE
    public string? ErrorDetail { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class TenantFile
{
    public Guid Id { get; set; } = Guid.NewGuid(); // PK
    public string TenantId { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string S3Key { get; set; } = null!;
    public long FileSizeBytes { get; set; }
    public string ContentHash { get; set; } = null!;
    public bool IsIndexed { get; set; }
    public DateTime? LastIndexedAt { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

public class TenantFileState
{
    public string Id { get; set; } = null!; // Composite PK: {tenant_id}#{file_name}
    public string TenantId { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public DateTime LastIndexedAt { get; set; } = DateTime.UtcNow;
}

public class IndexAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid(); // PK
    public string TenantId { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string ActionType { get; set; } = null!; // ADDED, MODIFIED, DELETED
    public int SectionsAffected { get; set; }
    public string CommitMessage { get; set; } = null!;
    public DateTime LoggedAt { get; set; } = DateTime.UtcNow;
}
