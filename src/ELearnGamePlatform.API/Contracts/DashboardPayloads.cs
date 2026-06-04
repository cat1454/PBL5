using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.API.Contracts;

public sealed class DashboardHomePayload
{
    public WorkspacePayload Workspace { get; init; } = new();
    public IReadOnlyList<SourcePayload> Sources { get; init; } = Array.Empty<SourcePayload>();
    public DashboardStatsPayload Stats { get; init; } = new();
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

public sealed class DashboardStatsPayload
{
    public int SourceCount { get; init; }
    public int CompletedSourceCount { get; init; }
    public int StudyReadySourceCount { get; init; }
    public int ProcessingSourceCount { get; init; }
    public int FailedSourceCount { get; init; }
    public int SelectedSourceCount { get; init; }
    public bool HasDeck { get; init; }
    public bool DeckReady { get; init; }
    public bool DeckStale { get; init; }
}

public sealed class WorkspacePayload
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string UploadedBy { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public int SourceCount { get; init; }
    public int ReadySourceCount { get; init; }
    public int SelectedSourceCount { get; init; }
    public bool IsDefault { get; init; }
    public WorkspaceDeckPayload? LatestDeck { get; init; }
}

public sealed class WorkspaceDeckPayload
{
    public int Id { get; init; }
    public int? FolderProjectId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string? Subtitle { get; init; }
    public int SlideCount { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public bool IsStale { get; init; }
}

public sealed class SourcePayload
{
    public int Id { get; init; }
    public int? WorkspaceId { get; init; }
    public int? FolderProjectId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FileType { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string? ExtractedText { get; init; }
    public IReadOnlyList<string> MainTopics { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> KeyPoints { get; init; } = Array.Empty<string>();
    public int CoverageChunkCount { get; init; }
    public string? Summary { get; init; }
    public string? Language { get; init; }
    public string DocumentType { get; init; } = DocumentTypes.Unknown;
    public string? Title { get; init; }
    public int? MainContentStartPage { get; init; }
    public IReadOnlyList<DocumentSectionDescriptor> Structure { get; init; } = Array.Empty<DocumentSectionDescriptor>();
    public IReadOnlyList<ExcludedContentDescriptor> ExcludedContent { get; init; } = Array.Empty<ExcludedContentDescriptor>();
    public bool IsStructureReady { get; init; }
    public string StructureAnalysisStatus { get; init; } = "processing";
    public DocumentStatus Status { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public string UploadedBy { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public bool IncludeInWorkspaceSlides { get; init; }
    public bool IncludeInFolderSlides { get; init; }
    public int WorkspaceSourceOrder { get; init; }
    public int FolderSourceOrder { get; init; }
    public int QuestionsCount { get; init; }
    public bool StudyReady { get; init; }
    public DocumentGenerationReadiness? GenerationReadiness { get; init; }
    public JobProgressPayload ProcessingProgress { get; init; } = new();
}
