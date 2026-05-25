using System.Text.Json;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class LearningHardeningTests
{
    [Fact]
    public async Task RecordAttemptAsync_PersistsFlashcardConfidence()
    {
        await using var context = CreateDbContext();
        var service = new LearningProgressService(context);

        await service.RecordAttemptAsync(
            "5",
            documentId: 9,
            questionId: 101,
            LearningMode.Flashcard,
            selectedAnswer: "self:unsure",
            isCorrect: false,
            confidence: "unsure",
            responseTimeMs: 1200);

        var attempt = await context.LearningAttempts.SingleAsync();
        Assert.Equal("unsure", attempt.Confidence);
        Assert.False(attempt.IsCorrect);
    }

    [Fact]
    public async Task GetReviewQueueAsync_ClassifiesNewWeakDueAndMasteredQuestions()
    {
        await using var context = CreateDbContext();
        var now = DateTime.UtcNow;
        context.Questions.AddRange(
            CreateQuestion(1),
            CreateQuestion(2),
            CreateQuestion(3),
            CreateQuestion(4));
        context.LearningProgresses.AddRange(
            new LearningProgress
            {
                UserId = "5",
                DocumentId = 9,
                QuestionId = 2,
                AttemptCount = 3,
                CorrectCount = 1,
                WrongCount = 2,
                MemoryScore = 50,
                MasteryScore = 45,
                Level = LearningLevel.Learning,
                LastReviewedAt = now.AddHours(-2),
                UpdatedAt = now.AddHours(-2)
            },
            new LearningProgress
            {
                UserId = "5",
                DocumentId = 9,
                QuestionId = 3,
                AttemptCount = 4,
                CorrectCount = 4,
                WrongCount = 0,
                MemoryScore = 90,
                MasteryScore = 92,
                Level = LearningLevel.Mastered,
                LastReviewedAt = now.AddDays(-1),
                UpdatedAt = now.AddDays(-1)
            },
            new LearningProgress
            {
                UserId = "5",
                DocumentId = 9,
                QuestionId = 4,
                AttemptCount = 2,
                CorrectCount = 2,
                WrongCount = 0,
                MemoryScore = 95,
                MasteryScore = 78,
                Level = LearningLevel.Good,
                LastReviewedAt = now.AddDays(-4),
                UpdatedAt = now.AddDays(-4)
            });
        await context.SaveChangesAsync();
        var service = new LearningProgressService(context);

        var queue = await service.GetReviewQueueAsync("5", 9);

        Assert.Contains(queue.New, item => item.QuestionId == 1);
        Assert.Contains(queue.Weak, item => item.QuestionId == 2);
        Assert.Contains(queue.Mastered, item => item.QuestionId == 3);
        Assert.Contains(queue.Due, item => item.QuestionId == 1 && item.DueReason == "new");
        Assert.Contains(queue.Due, item => item.QuestionId == 2 && item.DueReason == "weak");
        Assert.Contains(queue.Due, item => item.QuestionId == 4 && item.DueReason == "interval");
        Assert.DoesNotContain(queue.Due, item => item.QuestionId == 3);
    }

    [Fact]
    public async Task AnalyticsEventService_ValidatesAndStoresBatch()
    {
        await using var context = CreateDbContext();
        var service = new AnalyticsEventService(context);

        var recorded = await service.RecordEventsAsync("5", new[]
        {
            new AnalyticsEventInput
            {
                Name = "flashcard_assessed",
                Properties = JsonSerializer.Deserialize<JsonElement>("{\"assessment\":\"unsure\"}"),
                OccurredAt = DateTime.UtcNow.AddMinutes(-1),
                SessionId = "session-1"
            }
        });

        Assert.Equal(1, recorded);
        var analyticsEvent = await context.AnalyticsEvents.SingleAsync();
        Assert.Equal("5", analyticsEvent.UserId);
        Assert.Equal("flashcard_assessed", analyticsEvent.Name);
        Assert.Contains("unsure", analyticsEvent.PropertiesJson);
        Assert.Equal("session-1", analyticsEvent.SessionId);
    }

    [Fact]
    public async Task AnalyticsEventService_RejectsOversizedBatch()
    {
        await using var context = CreateDbContext();
        var service = new AnalyticsEventService(context);
        var events = Enumerable.Range(0, AnalyticsEventService.MaxBatchSize + 1)
            .Select(index => new AnalyticsEventInput { Name = $"event_{index}" })
            .ToList();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordEventsAsync("5", events));
    }

    private static Question CreateQuestion(int id)
        => new()
        {
            Id = id,
            DocumentId = 9,
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
