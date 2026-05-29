using System;
using System.Collections.Generic;

namespace CloudKB.SharedKernel;

// Synchronous Request/Response DTOs
public record ChatRequest(string Query);
public record ChatStreamChunk(string Text, bool IsFinal, List<SourceCitation>? Sources);
public record SourceCitation(string SectionId, string FileName, string Heading, List<string>? HeadingPath, double? Score, string? Snippet);
public record NotificationEvent(string EventType, string TaskId, string Message, object? Metadata);
public record IndexAcceptedResponse(Guid TaskId, string Message);
public record IndexAuditLogResponse(Guid Id, string FileName, string ActionType, int SectionsAffected, string CommitMessage, DateTime LoggedAt);
public record HealthResponse(string Status);

// Messaging DTOs (AsyncAPI payloads)
public record CompileKnowledgeTaskPayload(
    Guid TaskId,
    string TenantId,
    string S3StoragePath,
    List<string> FileNames,
    DateTime RequestedAt
);

public record IndexProgressPayload(
    string EventType,
    string TaskId,
    string Message
);

public record IndexCompletedPayload(
    string EventType,
    string TaskId,
    string Message,
    IndexCompletedMetadata Metadata
);

public record IndexCompletedMetadata(
    int SectionsCompiled,
    int FilesProcessed
);

public record IndexFailedPayload(
    string EventType,
    string TaskId,
    string Message,
    string ErrorCode,
    string ErrorDetail
);

// Redis Index Cache DTOs
public record TenantKbIndex(
    string TenantId,
    int TotalDocuments,
    double AverageDocumentLength,
    DateTime LastUpdatedAt,
    List<IndexedSectionMeta> Sections
);

public record IndexedSectionMeta(
    string SectionId,
    string FileName,
    string Heading,
    List<string> HeadingPath,
    int TokenCount,
    Dictionary<string, int> TermFrequencies
);

public record Bm25Options(double K1, double B, double HeadingBoost, double RetrievalScoreThreshold, int TopK);
public record ScoredSection(string SectionId, double Score);
