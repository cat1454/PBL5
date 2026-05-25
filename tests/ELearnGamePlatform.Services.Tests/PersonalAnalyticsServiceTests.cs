using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

        Assert.Equal("id", idProperty?.GetColumnName());
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

    private static Question CreateQuestion(int id, int documentId)
        => new()
        {
            Id = id,
            DocumentId = documentId,
            QuestionText = $"Question {id}?",
            QuestionType = QuestionType.MultipleChoice
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
