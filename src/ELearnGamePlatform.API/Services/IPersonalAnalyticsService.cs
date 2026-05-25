namespace ELearnGamePlatform.API.Services;

public interface IPersonalAnalyticsService
{
    Task<PersonalAnalyticsSummary> GetPersonalSummaryAsync(
        string userId,
        CancellationToken cancellationToken = default);
}

public sealed class PersonalAnalyticsSummary
{
    public string UserId { get; set; } = string.Empty;
    public PersonalAnalyticsWorkspace? Workspace { get; set; }
    public PersonalAnalyticsMetrics Metrics { get; set; } = new();
    public IReadOnlyList<PersonalAnalyticsSkill> Skills { get; set; } = Array.Empty<PersonalAnalyticsSkill>();
    public PersonalAnalyticsHeatmap Heatmap { get; set; } = new();
    public IReadOnlyList<PersonalAnalyticsActivity> Activity { get; set; } = Array.Empty<PersonalAnalyticsActivity>();
    public IReadOnlyList<PersonalAnalyticsChecklistItem> Checklist { get; set; } = Array.Empty<PersonalAnalyticsChecklistItem>();
    public PersonalAnalyticsActionsContext ActionsContext { get; set; } = new();
    public IReadOnlyList<PersonalAnalyticsSource> Sources { get; set; } = Array.Empty<PersonalAnalyticsSource>();
}

public sealed class PersonalAnalyticsWorkspace
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public PersonalAnalyticsDeck? LatestDeck { get; set; }
}

public sealed class PersonalAnalyticsDeck
{
    public int Id { get; set; }
    public int? DocumentId { get; set; }
    public int? FolderProjectId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Title { get; set; }
    public int SlideCount { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class PersonalAnalyticsSource
{
    public int Id { get; set; }
    public int? WorkspaceId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int QuestionsCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class PersonalAnalyticsMetrics
{
    public int SourceCount { get; set; }
    public int CompletedSourceCount { get; set; }
    public int ReadySourceCount { get; set; }
    public int QuestionCount { get; set; }
    public int AttemptCount { get; set; }
    public int CorrectAttemptCount { get; set; }
    public int TestCount { get; set; }
    public long StudySeconds { get; set; }
    public int CurrentStreakDays { get; set; }
    public int ActiveDays { get; set; }
    public double AccuracyPercent { get; set; }
    public double ReadinessPercent { get; set; }
    public double AverageMasteryScore { get; set; }
    public double AverageMemoryScore { get; set; }
    public int WeakCount { get; set; }
    public int MasteredCount { get; set; }
}

public sealed class PersonalAnalyticsSkill
{
    public string Key { get; set; } = string.Empty;
    public int Value { get; set; }
}

public sealed class PersonalAnalyticsHeatmap
{
    public IReadOnlyList<PersonalAnalyticsHeatmapDay> Days { get; set; } = Array.Empty<PersonalAnalyticsHeatmapDay>();
    public int ActiveDays { get; set; }
    public int CurrentStreakDays { get; set; }
    public int PeakLevel { get; set; }
}

public sealed class PersonalAnalyticsHeatmapDay
{
    public string Date { get; set; } = string.Empty;
    public int Level { get; set; }
    public int SignalCount { get; set; }
}

public sealed class PersonalAnalyticsActivity
{
    public string Key { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? DocumentId { get; set; }
    public DateTime OccurredAt { get; set; }
}

public sealed class PersonalAnalyticsChecklistItem
{
    public string Key { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}

public sealed class PersonalAnalyticsActionsContext
{
    public int? WorkspaceId { get; set; }
    public int? LatestSourceId { get; set; }
    public int? LatestCompletedSourceId { get; set; }
    public int? LatestReadySourceId { get; set; }
    public bool HasDeck { get; set; }
}
