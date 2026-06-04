using ELearnGamePlatform.API.Contracts;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;

namespace ELearnGamePlatform.API.Services;

public sealed class WorkspacePayloadService : IWorkspacePayloadService
{
    private readonly IDocumentProcessingJobStore _documentJobStore;
    private readonly IDocumentGenerationReadinessService _generationReadinessService;
    private readonly IWorkspaceService _workspaceService;

    public WorkspacePayloadService(
        IDocumentProcessingJobStore documentJobStore,
        IDocumentGenerationReadinessService generationReadinessService,
        IWorkspaceService workspaceService)
    {
        _documentJobStore = documentJobStore;
        _generationReadinessService = generationReadinessService;
        _workspaceService = workspaceService;
    }

    public WorkspacePayload BuildWorkspacePayload(FolderProject workspace)
    {
        var sources = (workspace.Documents ?? Array.Empty<Document>())
            .OrderBy(source => source.FolderSourceOrder)
            .ThenBy(source => source.CreatedAt)
            .ToList();
        var deck = (workspace.SlideDecks ?? Array.Empty<SlideDeck>())
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefault();
        var readySourceCount = sources.Count(source => source.Status == DocumentStatus.Completed);
        var selectedSourceCount = sources.Count(source => source.IncludeInFolderSlides);
        var isStale = IsDeckStale(deck, sources);

        return new WorkspacePayload
        {
            Id = workspace.Id,
            Name = workspace.Name,
            Description = workspace.Description,
            UploadedBy = workspace.UploadedBy,
            CreatedAt = workspace.CreatedAt,
            UpdatedAt = workspace.UpdatedAt,
            SourceCount = sources.Count,
            ReadySourceCount = readySourceCount,
            SelectedSourceCount = selectedSourceCount,
            IsDefault = workspace.Name == _workspaceService.DefaultWorkspaceName,
            LatestDeck = deck == null
                ? null
                : new WorkspaceDeckPayload
                {
                    Id = deck.Id,
                    FolderProjectId = deck.FolderProjectId,
                    Status = deck.Status.ToString(),
                    Title = deck.Title,
                    Subtitle = deck.Subtitle,
                    SlideCount = deck.Items?.Count ?? 0,
                    UpdatedAt = deck.UpdatedAt,
                    CompletedAt = deck.CompletedAt,
                    IsStale = isStale,
                },
        };
    }

    public async Task<SourcePayload> BuildSourcePayloadAsync(Document source, int questionsCount)
    {
        _documentJobStore.TryGetJob(source.Id, out var progressState);
        var metadata = source.GetProcessingMetadata();
        var generationReadiness = source.Status == DocumentStatus.Completed
            ? await _generationReadinessService.GetReadinessAsync(source)
            : null;

        return new SourcePayload
        {
            Id = source.Id,
            WorkspaceId = source.FolderProjectId,
            FolderProjectId = source.FolderProjectId,
            FileName = source.FileName,
            FileType = source.FileType,
            FilePath = source.FilePath,
            FileSize = source.FileSize,
            ExtractedText = source.ExtractedText,
            MainTopics = source.GetMainTopics(),
            KeyPoints = source.GetKeyPoints(),
            CoverageChunkCount = source.GetCoverageMap().Count,
            Summary = source.Summary,
            Language = source.Language,
            DocumentType = metadata.DocumentType,
            Title = metadata.Title,
            MainContentStartPage = metadata.MainContentStartPage,
            Structure = metadata.Structure,
            ExcludedContent = metadata.ExcludedContent,
            IsStructureReady = metadata.Structure?.Count > 0,
            StructureAnalysisStatus = source.Status == DocumentStatus.Completed
                ? "ready"
                : source.Status == DocumentStatus.Failed
                    ? "failed"
                    : "processing",
            Status = source.Status,
            StatusLabel = source.Status.ToString(),
            UploadedBy = source.UploadedBy,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            IncludeInWorkspaceSlides = source.IncludeInFolderSlides,
            IncludeInFolderSlides = source.IncludeInFolderSlides,
            WorkspaceSourceOrder = source.FolderSourceOrder,
            FolderSourceOrder = source.FolderSourceOrder,
            QuestionsCount = questionsCount,
            StudyReady = source.Status == DocumentStatus.Completed && questionsCount > 0,
            GenerationReadiness = generationReadiness,
            ProcessingProgress = JobProgressPayloadFactory.BuildDocument(progressState, source),
        };
    }

    public async Task<IReadOnlyList<SourcePayload>> BuildSourcePayloadsAsync(IEnumerable<Document> sources)
    {
        var payload = new List<SourcePayload>();
        foreach (var source in sources)
        {
            payload.Add(await BuildSourcePayloadAsync(source, source.Questions.Count));
        }

        return payload;
    }

    public async Task<DashboardHomePayload> BuildDashboardHomePayloadAsync(FolderProject workspace, IEnumerable<Document> sources)
    {
        var orderedSources = sources
            .OrderBy(source => source.FolderSourceOrder)
            .ThenBy(source => source.CreatedAt)
            .ToList();
        var sourcePayloads = await BuildSourcePayloadsAsync(orderedSources);
        var workspacePayload = BuildWorkspacePayload(workspace);

        return new DashboardHomePayload
        {
            Workspace = workspacePayload,
            Sources = sourcePayloads
                .OrderByDescending(source => source.UpdatedAt)
                .ThenByDescending(source => source.CreatedAt)
                .ToList(),
            Stats = new DashboardStatsPayload
            {
                SourceCount = sourcePayloads.Count,
                CompletedSourceCount = sourcePayloads.Count(source => source.Status == DocumentStatus.Completed),
                StudyReadySourceCount = sourcePayloads.Count(source => source.StudyReady),
                ProcessingSourceCount = sourcePayloads.Count(source =>
                    source.Status is DocumentStatus.Uploaded or DocumentStatus.Extracting or DocumentStatus.Analyzing),
                FailedSourceCount = sourcePayloads.Count(source => source.Status == DocumentStatus.Failed),
                SelectedSourceCount = sourcePayloads.Count(source => source.IncludeInWorkspaceSlides),
                HasDeck = workspacePayload.LatestDeck != null,
                DeckReady = workspacePayload.LatestDeck?.Status == SlideDeckStatus.Completed.ToString(),
                DeckStale = workspacePayload.LatestDeck?.IsStale ?? false,
            },
            GeneratedAt = DateTime.UtcNow,
        };
    }

    private static bool IsDeckStale(SlideDeck? deck, IEnumerable<Document> sources)
    {
        var latestDeckUpdatedAt = deck?.UpdatedAt ?? deck?.CompletedAt;
        return deck != null && latestDeckUpdatedAt.HasValue && sources.Any(source =>
            source.CreatedAt > latestDeckUpdatedAt.Value || source.UpdatedAt > latestDeckUpdatedAt.Value);
    }
}
