using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.API.Services;

public sealed class PersonalAnalyticsService : IPersonalAnalyticsService
{
    private const int HeatmapWeekCount = 52;
    private const int HeatmapDaysPerWeek = 7;
    private const int HeatmapDayCount = HeatmapWeekCount * HeatmapDaysPerWeek;

    private readonly ApplicationDbContext _dbContext;

    public PersonalAnalyticsService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PersonalAnalyticsSummary> GetPersonalSummaryAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        var now = DateTime.UtcNow;
        var today = StartOfDay(now);
        var currentWeekStart = StartOfIsoWeek(today);
        var heatmapStart = currentWeekStart.AddDays(-((HeatmapWeekCount - 1) * HeatmapDaysPerWeek));

        var workspaces = await _dbContext.FolderProjects
            .AsNoTracking()
            .Where(folder => folder.UploadedBy == userId)
            .OrderByDescending(folder => folder.UpdatedAt)
            .ToListAsync(cancellationToken);

        var documents = await _dbContext.Documents
            .AsNoTracking()
            .Include(document => document.Questions)
            .Where(document => document.UploadedBy == userId)
            .OrderByDescending(document => document.UpdatedAt)
            .ToListAsync(cancellationToken);

        var documentIds = documents.Select(document => document.Id).ToHashSet();
        var attempts = await _dbContext.LearningAttempts
            .AsNoTracking()
            .Where(attempt => attempt.UserId == userId && documentIds.Contains(attempt.DocumentId))
            .ToListAsync(cancellationToken);

        var progresses = await _dbContext.LearningProgresses
            .AsNoTracking()
            .Where(progress => progress.UserId == userId && documentIds.Contains(progress.DocumentId))
            .ToListAsync(cancellationToken);

        var tests = await _dbContext.LearningTestResults
            .AsNoTracking()
            .Where(result => result.UserId == userId
                && documentIds.Contains(result.DocumentId)
                && result.Status == LearningTestResultStatus.Completed)
            .ToListAsync(cancellationToken);

        var workspaceIds = workspaces.Select(folder => folder.Id).ToHashSet();
        var decks = await _dbContext.SlideDecks
            .AsNoTracking()
            .Include(deck => deck.Items)
            .Where(deck => (deck.DocumentId.HasValue && documentIds.Contains(deck.DocumentId.Value))
                || (deck.FolderProjectId.HasValue && workspaceIds.Contains(deck.FolderProjectId.Value)))
            .ToListAsync(cancellationToken);

        var analyticsEvents = await _dbContext.AnalyticsEvents
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.OccurredAt >= heatmapStart)
            .ToListAsync(cancellationToken);

        var latestWorkspace = workspaces.FirstOrDefault();
        var latestWorkspaceDeck = latestWorkspace == null
            ? null
            : decks
                .Where(deck => IsDeckInWorkspace(deck, latestWorkspace.Id, documents))
                .OrderByDescending(deck => deck.UpdatedAt)
                .ThenByDescending(deck => deck.CompletedAt)
                .FirstOrDefault();
        var hasDeck = decks.Count > 0;

        var completedSources = documents.Where(document => document.Status == DocumentStatus.Completed).ToList();
        var readySources = completedSources.Where(document => document.Questions.Count(question => !question.IsArchived) > 0).ToList();
        var questionCount = documents.Sum(document => document.Questions.Count(question => !question.IsArchived));
        var correctAttemptCount = attempts.Count(attempt => attempt.IsCorrect);
        var accuracy = attempts.Count > 0 ? (double)correctAttemptCount / attempts.Count * 100d : 0d;
        var studySeconds = CalculateStudySeconds(attempts, tests);
        var activitySignals = BuildActivitySignals(documents, attempts, tests, decks, analyticsEvents);
        var heatmap = BuildHeatmap(activitySignals, heatmapStart, today);
        var averageMastery = progresses.Count > 0 ? progresses.Average(item => item.MasteryScore) : 0d;
        var averageMemory = progresses.Count > 0 ? progresses.Average(item => item.MemoryScore) : 0d;

        var metrics = new PersonalAnalyticsMetrics
        {
            SourceCount = documents.Count,
            CompletedSourceCount = completedSources.Count,
            ReadySourceCount = readySources.Count,
            QuestionCount = questionCount,
            AttemptCount = attempts.Count,
            CorrectAttemptCount = correctAttemptCount,
            TestCount = tests.Count,
            StudySeconds = studySeconds,
            CurrentStreakDays = heatmap.CurrentStreakDays,
            ActiveDays = heatmap.ActiveDays,
            AccuracyPercent = RoundScore(accuracy),
            ReadinessPercent = CalculateReadiness(completedSources.Count, readySources.Count, questionCount, progresses, hasDeck),
            AverageMasteryScore = RoundScore(averageMastery),
            AverageMemoryScore = RoundScore(averageMemory),
            WeakCount = progresses.Count(progress => progress.Level == LearningLevel.Weak),
            MasteredCount = progresses.Count(progress => progress.Level == LearningLevel.Mastered)
        };

        return new PersonalAnalyticsSummary
        {
            UserId = userId,
            Workspace = latestWorkspace == null ? null : BuildWorkspace(latestWorkspace, latestWorkspaceDeck),
            Sources = documents.Select(BuildSource).ToList(),
            Metrics = metrics,
            Skills = BuildSkills(metrics, hasDeck).ToList(),
            Heatmap = heatmap,
            Activity = BuildActivity(documents, attempts, tests, decks).ToList(),
            Checklist = BuildChecklist(metrics, hasDeck).ToList(),
            ActionsContext = new PersonalAnalyticsActionsContext
            {
                WorkspaceId = latestWorkspace?.Id,
                LatestSourceId = documents.FirstOrDefault()?.Id,
                LatestCompletedSourceId = completedSources.OrderByDescending(item => item.UpdatedAt).FirstOrDefault()?.Id,
                LatestReadySourceId = readySources.OrderByDescending(item => item.UpdatedAt).FirstOrDefault()?.Id,
                HasDeck = hasDeck
            }
        };
    }

    private static bool IsDeckInWorkspace(SlideDeck deck, int workspaceId, IReadOnlyList<Document> documents)
    {
        if (deck.FolderProjectId == workspaceId)
        {
            return true;
        }

        return deck.DocumentId.HasValue
            && documents.Any(document => document.Id == deck.DocumentId.Value && document.FolderProjectId == workspaceId);
    }

    private static PersonalAnalyticsWorkspace BuildWorkspace(FolderProject workspace, SlideDeck? latestDeck)
        => new()
        {
            Id = workspace.Id,
            Name = workspace.Name,
            Description = workspace.Description,
            CreatedAt = workspace.CreatedAt,
            UpdatedAt = workspace.UpdatedAt,
            LatestDeck = latestDeck == null
                ? null
                : new PersonalAnalyticsDeck
                {
                    Id = latestDeck.Id,
                    DocumentId = latestDeck.DocumentId,
                    FolderProjectId = latestDeck.FolderProjectId,
                    Status = latestDeck.Status.ToString(),
                    Title = latestDeck.Title,
                    SlideCount = latestDeck.Items?.Count ?? 0,
                    UpdatedAt = latestDeck.UpdatedAt,
                    CompletedAt = latestDeck.CompletedAt
                }
        };

    private static PersonalAnalyticsSource BuildSource(Document document)
        => new()
        {
            Id = document.Id,
            WorkspaceId = document.FolderProjectId,
            FileName = document.FileName,
            Status = document.Status.ToString(),
            QuestionsCount = document.Questions.Count(question => !question.IsArchived),
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt
        };

    private static long CalculateStudySeconds(IReadOnlyList<LearningAttempt> attempts, IReadOnlyList<LearningTestResult> tests)
    {
        var attemptSeconds = attempts
            .Where(attempt => attempt.ResponseTimeMs.HasValue)
            .Sum(attempt => Math.Clamp(attempt.ResponseTimeMs!.Value / 1000L, 0L, 300L));
        var testSeconds = tests.Sum(test => Math.Clamp(test.DurationMs / 1000L, 0L, 7200L));
        return attemptSeconds + testSeconds;
    }

    private static Dictionary<DateTime, int> BuildActivitySignals(
        IReadOnlyList<Document> documents,
        IReadOnlyList<LearningAttempt> attempts,
        IReadOnlyList<LearningTestResult> tests,
        IReadOnlyList<SlideDeck> decks,
        IReadOnlyList<AnalyticsEvent> analyticsEvents)
    {
        var signals = new Dictionary<DateTime, int>();

        foreach (var document in documents)
        {
            AddActivitySignal(signals, document.CreatedAt);
            AddActivitySignal(signals, document.UpdatedAt);
        }

        foreach (var attempt in attempts)
        {
            AddActivitySignal(signals, attempt.CreatedAt);
        }

        foreach (var test in tests)
        {
            AddActivitySignal(signals, test.SubmittedAt);
        }

        foreach (var deck in decks)
        {
            AddActivitySignal(signals, deck.CompletedAt ?? deck.UpdatedAt);
        }

        foreach (var analyticsEvent in analyticsEvents)
        {
            AddActivitySignal(signals, analyticsEvent.OccurredAt);
        }

        return signals;
    }

    private static PersonalAnalyticsHeatmap BuildHeatmap(IReadOnlyDictionary<DateTime, int> activitySignals, DateTime start, DateTime today)
    {
        var days = new List<PersonalAnalyticsHeatmapDay>(HeatmapDayCount);
        var activeDays = 0;
        var peakLevel = 0;

        for (var index = 0; index < HeatmapDayCount; index++)
        {
            var date = start.AddDays(index);
            var signalCount = activitySignals.TryGetValue(date, out var count) ? count : 0;
            var level = CalculateHeatmapLevel(signalCount);
            activeDays += level > 0 ? 1 : 0;
            peakLevel = Math.Max(peakLevel, level);

            days.Add(new PersonalAnalyticsHeatmapDay
            {
                Date = date.ToString("yyyy-MM-dd"),
                Level = level,
                SignalCount = signalCount
            });
        }

        return new PersonalAnalyticsHeatmap
        {
            Days = days,
            ActiveDays = activeDays,
            CurrentStreakDays = CalculateCurrentStreak(activitySignals, today),
            PeakLevel = peakLevel
        };
    }

    private static int CalculateCurrentStreak(IReadOnlyDictionary<DateTime, int> activitySignals, DateTime today)
    {
        var streak = 0;
        var cursor = today;
        while (activitySignals.TryGetValue(cursor, out var count) && count > 0)
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }

    private static void AddActivitySignal(IDictionary<DateTime, int> signals, DateTime value)
    {
        var date = StartOfDay(value);
        signals[date] = signals.TryGetValue(date, out var count) ? count + 1 : 1;
    }

    private static int CalculateHeatmapLevel(int signalCount)
    {
        if (signalCount <= 0)
        {
            return 0;
        }

        if (signalCount == 1)
        {
            return 1;
        }

        if (signalCount == 2)
        {
            return 2;
        }

        return signalCount <= 4 ? 3 : 4;
    }

    private static double CalculateReadiness(
        int completedSourceCount,
        int readySourceCount,
        int questionCount,
        IReadOnlyList<LearningProgress> progresses,
        bool hasDeck)
    {
        if (completedSourceCount == 0 && questionCount == 0 && progresses.Count == 0 && !hasDeck)
        {
            return 0d;
        }

        var averageMastery = progresses.Count > 0 ? progresses.Average(item => item.MasteryScore) : 0d;
        var sourceScore = Math.Min(25d, completedSourceCount * 8d);
        var questionScore = Math.Min(25d, readySourceCount * 8d + questionCount * 0.8d);
        var masteryScore = Math.Min(40d, averageMastery * 0.4d);
        var deckScore = hasDeck ? 10d : 0d;
        return RoundScore(Math.Clamp(sourceScore + questionScore + masteryScore + deckScore, 0d, 100d));
    }

    private static IEnumerable<PersonalAnalyticsSkill> BuildSkills(PersonalAnalyticsMetrics metrics, bool hasDeck)
    {
        yield return CreateSkill("recall", metrics.AverageMemoryScore);
        yield return CreateSkill("concepts", metrics.AverageMasteryScore);
        yield return CreateSkill("questionBank", Math.Min(100d, metrics.QuestionCount * 4d + metrics.ReadySourceCount * 12d));
        yield return CreateSkill("slides", hasDeck ? 82d : metrics.CompletedSourceCount > 0 ? 35d : 0d);
        yield return CreateSkill("consistency", Math.Min(100d, metrics.ActiveDays * 3d + metrics.CurrentStreakDays * 8d));
    }

    private static PersonalAnalyticsSkill CreateSkill(string key, double value)
        => new()
        {
            Key = key,
            Value = (int)Math.Round(Math.Clamp(value, 0d, 100d), MidpointRounding.AwayFromZero)
        };

    private static IEnumerable<PersonalAnalyticsActivity> BuildActivity(
        IReadOnlyList<Document> documents,
        IReadOnlyList<LearningAttempt> attempts,
        IReadOnlyList<LearningTestResult> tests,
        IReadOnlyList<SlideDeck> decks)
    {
        var items = new List<PersonalAnalyticsActivity>();

        items.AddRange(documents.Select(document => new PersonalAnalyticsActivity
        {
            Key = $"source-{document.Id}",
            Kind = "source",
            Title = document.FileName,
            Status = document.Status.ToString(),
            DocumentId = document.Id,
            OccurredAt = document.UpdatedAt
        }));

        items.AddRange(attempts
            .OrderByDescending(attempt => attempt.CreatedAt)
            .Take(10)
            .Select(attempt => new PersonalAnalyticsActivity
            {
                Key = $"attempt-{attempt.Id}",
                Kind = "study",
                Title = attempt.Mode.ToString(),
                Status = attempt.IsCorrect ? "correct" : "incorrect",
                DocumentId = attempt.DocumentId,
                OccurredAt = attempt.CreatedAt
            }));

        items.AddRange(tests.Select(test => new PersonalAnalyticsActivity
        {
            Key = $"test-{test.Id}",
            Kind = "test",
            Title = test.TestType.ToString(),
            Status = $"{test.Score:0.#}",
            DocumentId = test.DocumentId,
            OccurredAt = test.SubmittedAt
        }));

        items.AddRange(decks.Select(deck => new PersonalAnalyticsActivity
        {
            Key = $"deck-{deck.Id}",
            Kind = "deck",
            Title = deck.Title ?? "Slide deck",
            Status = deck.Status.ToString(),
            DocumentId = deck.DocumentId,
            OccurredAt = deck.CompletedAt ?? deck.UpdatedAt
        }));

        return items
            .OrderByDescending(item => item.OccurredAt)
            .Take(8);
    }

    private static IEnumerable<PersonalAnalyticsChecklistItem> BuildChecklist(PersonalAnalyticsMetrics metrics, bool hasDeck)
    {
        yield return new PersonalAnalyticsChecklistItem
        {
            Key = "upload",
            State = metrics.SourceCount > 0 ? "ready" : "next"
        };
        yield return new PersonalAnalyticsChecklistItem
        {
            Key = "questions",
            State = metrics.ReadySourceCount > 0 ? "ready" : metrics.CompletedSourceCount > 0 ? "next" : "pending"
        };
        yield return new PersonalAnalyticsChecklistItem
        {
            Key = "study",
            State = metrics.AttemptCount > 0 || metrics.TestCount > 0 ? "ready" : metrics.ReadySourceCount > 0 ? "next" : "pending"
        };
        yield return new PersonalAnalyticsChecklistItem
        {
            Key = "slides",
            State = hasDeck ? "ready" : metrics.CompletedSourceCount > 0 ? "later" : "pending"
        };
    }

    private static DateTime StartOfDay(DateTime value)
        => value.Date;

    private static DateTime StartOfIsoWeek(DateTime value)
    {
        var day = value.DayOfWeek;
        var mondayOffset = day == DayOfWeek.Sunday ? -6 : (int)DayOfWeek.Monday - (int)day;
        return value.Date.AddDays(mondayOffset);
    }

    private static double RoundScore(double value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
