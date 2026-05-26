using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class PersonalAnalyticsServiceTests
{
    [Fact]
    public void AnalyticsEventId_MapsToLowercasePostgresColumn()
    {
        using var context = CreateDbContext();

        var entityType = context.Model.FindEntityType(typeof(AnalyticsEvent));
        var idProperty = entityType?.FindProperty(nameof(AnalyticsEvent.Id));
        var storeObject = StoreObjectIdentifier.Create(entityType!, StoreObjectType.Table);

        Assert.True(storeObject.HasValue);
        Assert.Equal("id", idProperty?.GetColumnName(storeObject.Value));
    }

    [Fact]
    public async Task GetPersonalSummaryAsync_ReturnsEmptySummaryForNewUser()
    {
        await using var context = CreateDbContext();
        var service = new PersonalAnalyticsService(context);

        var summary = await service.GetPersonalSummaryAsync("user-1");

        Assert.Equal("user-1", summary.UserId);
        Assert.Null(summary.Workspace);
        Assert.Equal(0, summary.Metrics.SourceCount);
        Assert.Equal(0, summary.Metrics.QuestionCount);
        Assert.Equal(0, summary.Metrics.ActiveDays);
        Assert.Equal(52 * 7, summary.Heatmap.Days.Count);
        Assert.All(summary.Heatmap.Days, day => Assert.Equal(0, day.Level));
        Assert.Empty(summary.Activity);
    }

    [Fact]
    public async Task GetPersonalSummaryAsync_UsesOnlyCurrentUsersLearningData()
    {
        await using var context = CreateDbContext();
        var now = DateTime.UtcNow;

        var workspace = new FolderProject
        {
            Id = 10,
            Name = "Default Workspace",
            UploadedBy = "user-1",
            CreatedAt = now.AddDays(-4),
            UpdatedAt = now.AddDays(-1)
        };

        var document = new Document
        {
            Id = 20,
            FileName = "biology.pdf",
            FileType = "PDF",
            FilePath = "biology.pdf",
            UploadedBy = "user-1",
            FolderProjectId = workspace.Id,
            Status = DocumentStatus.Completed,
            CreatedAt = now.AddDays(-4),
            UpdatedAt = now.AddDays(-2)
        };

        context.FolderProjects.AddRange(
            workspace,
            new FolderProject
            {
                Id = 11,
                Name = "Other Workspace",
                UploadedBy = "user-2",
                CreatedAt = now.AddDays(-4),
                UpdatedAt = now
            });

        context.Documents.AddRange(
            document,
            new Document
            {
                Id = 21,
                FileName = "other.pdf",
                FileType = "PDF",
                FilePath = "other.pdf",
                UploadedBy = "user-2",
                Status = DocumentStatus.Completed,
                CreatedAt = now,
                UpdatedAt = now
            });

        context.Questions.AddRange(
            CreateQuestion(30, document.Id),
            CreateQuestion(31, document.Id),
            CreateQuestion(32, 21));

        context.LearningAttempts.AddRange(
            new LearningAttempt
            {
                UserId = "user-1",
                DocumentId = document.Id,
                QuestionId = 30,
                Mode = LearningMode.Quiz,
                IsCorrect = true,
                ResponseTimeMs = 30000,
                CreatedAt = now.AddDays(-1)
            },
            new LearningAttempt
            {
                UserId = "user-2",
                DocumentId = 21,
                QuestionId = 32,
                Mode = LearningMode.Quiz,
                IsCorrect = false,
                ResponseTimeMs = 900000,
                CreatedAt = now
            });

        context.LearningProgresses.Add(new LearningProgress
        {
            UserId = "user-1",
            DocumentId = document.Id,
            QuestionId = 30,
            AttemptCount = 1,
            CorrectCount = 1,
            BestStreak = 1,
            CurrentStreak = 1,
            MemoryScore = 80,
            MasteryScore = 90,
            Level = LearningLevel.Mastered,
            LastReviewedAt = now.AddDays(-1),
            UpdatedAt = now.AddDays(-1)
        });

        context.LearningTestResults.AddRange(
            new LearningTestResult
            {
                UserId = "user-1",
                DocumentId = document.Id,
                TotalQuestions = 2,
                CorrectCount = 1,
                WrongCount = 1,
                Score = 50,
                DurationMs = 120000,
                Status = LearningTestResultStatus.Completed,
                StartedAt = now.AddDays(-1),
                SubmittedAt = now.AddDays(-1),
                CreatedAt = now.AddDays(-1)
            },
            new LearningTestResult
            {
                UserId = "user-2",
                DocumentId = 21,
                TotalQuestions = 1,
                CorrectCount = 0,
                WrongCount = 1,
                Score = 0,
                DurationMs = 900000,
                Status = LearningTestResultStatus.Completed,
                StartedAt = now,
                SubmittedAt = now,
                CreatedAt = now
            });

        context.SlideDecks.Add(new SlideDeck
        {
            Id = 40,
            FolderProjectId = workspace.Id,
            Status = SlideDeckStatus.Completed,
            Title = "Biology",
            CreatedAt = now.AddHours(-12),
            UpdatedAt = now.AddHours(-12),
            CompletedAt = now.AddHours(-12)
        });

        await context.SaveChangesAsync();
        var service = new PersonalAnalyticsService(context);

        var summary = await service.GetPersonalSummaryAsync("user-1");

        Assert.Equal(1, summary.Metrics.SourceCount);
        Assert.Equal(2, summary.Metrics.QuestionCount);
        Assert.Equal(1, summary.Metrics.AttemptCount);
        Assert.Equal(1, summary.Metrics.TestCount);
        Assert.True(summary.Metrics.StudySeconds >= 150);
        Assert.DoesNotContain(summary.Activity, item => item.Title.Contains("other", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(summary.Skills, skill => skill.Key == "slides" && skill.Value > 0);
        Assert.True(summary.Heatmap.ActiveDays > 0);
    }

    [Fact]
    public async Task GetPersonalSummaryAsync_ExcludesArchivedQuestionsFromReadySources()
    {
        await using var context = CreateDbContext();
        var now = DateTime.UtcNow;

        context.Documents.Add(new Document
        {
            Id = 50,
            FileName = "archived-only.pdf",
            FileType = "PDF",
            FilePath = "archived-only.pdf",
            UploadedBy = "user-1",
            Status = DocumentStatus.Completed,
            CreatedAt = now.AddDays(-2),
            UpdatedAt = now.AddDays(-1)
        });
        context.Questions.Add(CreateQuestion(51, 50, isArchived: true));

        await context.SaveChangesAsync();
        var service = new PersonalAnalyticsService(context);

        var summary = await service.GetPersonalSummaryAsync("user-1");

        Assert.Equal(1, summary.Metrics.CompletedSourceCount);
        Assert.Equal(0, summary.Metrics.ReadySourceCount);
        Assert.Equal(0, summary.Metrics.QuestionCount);
        Assert.Null(summary.ActionsContext.LatestReadySourceId);
        Assert.Equal(0, summary.Sources.Single().QuestionsCount);
    }

    [Fact]
    public async Task GetPersonalSummaryAsync_AttachesLatestDeckFromSelectedWorkspaceOnly()
    {
        await using var context = CreateDbContext();
        var now = DateTime.UtcNow;

        context.FolderProjects.AddRange(
            new FolderProject
            {
                Id = 60,
                Name = "Selected Workspace",
                UploadedBy = "user-1",
                CreatedAt = now.AddDays(-10),
                UpdatedAt = now
            },
            new FolderProject
            {
                Id = 61,
                Name = "Older Workspace",
                UploadedBy = "user-1",
                CreatedAt = now.AddDays(-12),
                UpdatedAt = now.AddDays(-1)
            });

        context.SlideDecks.AddRange(
            new SlideDeck
            {
                Id = 70,
                FolderProjectId = 60,
                Status = SlideDeckStatus.Completed,
                Title = "Selected deck",
                CreatedAt = now.AddDays(-3),
                UpdatedAt = now.AddDays(-3),
                CompletedAt = now.AddDays(-3)
            },
            new SlideDeck
            {
                Id = 71,
                FolderProjectId = 61,
                Status = SlideDeckStatus.Completed,
                Title = "Other workspace deck",
                CreatedAt = now.AddHours(-1),
                UpdatedAt = now.AddHours(-1),
                CompletedAt = now.AddHours(-1)
            });

        await context.SaveChangesAsync();
        var service = new PersonalAnalyticsService(context);

        var summary = await service.GetPersonalSummaryAsync("user-1");

        Assert.Equal(60, summary.Workspace?.Id);
        Assert.Equal(70, summary.Workspace?.LatestDeck?.Id);
        Assert.Equal(60, summary.Workspace?.LatestDeck?.FolderProjectId);
    }

    [Fact]
    public async Task GetPersonalSummaryAsync_HeatmapTracksPerDaySignalIntensity()
    {
        await using var context = CreateDbContext();
        var now = DateTime.UtcNow;

        context.Documents.Add(new Document
        {
            Id = 80,
            FileName = "activity.pdf",
            FileType = "PDF",
            FilePath = "activity.pdf",
            UploadedBy = "user-1",
            Status = DocumentStatus.Completed,
            CreatedAt = now,
            UpdatedAt = now
        });
        context.Questions.Add(CreateQuestion(81, 80));
        context.LearningAttempts.Add(new LearningAttempt
        {
            UserId = "user-1",
            DocumentId = 80,
            QuestionId = 81,
            Mode = LearningMode.Quiz,
            IsCorrect = true,
            CreatedAt = now
        });
        context.AnalyticsEvents.Add(new AnalyticsEvent
        {
            UserId = "user-1",
            Name = "analytics.opened",
            OccurredAt = now
        });

        await context.SaveChangesAsync();
        var service = new PersonalAnalyticsService(context);

        var summary = await service.GetPersonalSummaryAsync("user-1");
        var today = now.Date.ToString("yyyy-MM-dd");
        var todayCell = summary.Heatmap.Days.Single(day => day.Date == today);

        Assert.True(todayCell.SignalCount > 1);
        Assert.True(todayCell.Level > 1);
        Assert.True(summary.Heatmap.PeakLevel > 1);
    }

    private static Question CreateQuestion(int id, int documentId, bool isArchived = false)
        => new()
        {
            Id = id,
            DocumentId = documentId,
            QuestionText = $"Question {id}?",
            QuestionType = QuestionType.MultipleChoice,
            IsArchived = isArchived
        };

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"), new InMemoryDatabaseRoot())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }
}
