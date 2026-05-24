using ELearnGamePlatform.API.Services.QuestionStudio;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class QuestionStudioReviewFixTests
{
    [Fact]
    public async Task VariantGeneration_PreservesSourceUnitIdAndTopic_WhenNavigationIsMissing()
    {
        var generator = new QuestionVariantGenerator();
        var run = CreateRun();
        var canonical = CreateCanonicalDraft(sourceUnitId: 42, topicTag: "Photosynthesis");

        var variants = await generator.GenerateAsync(
            run,
            new[] { canonical },
            new[] { "ShortAnswer" },
            new[] { "Easy" },
            remainingDraftBudget: 1);

        var variant = Assert.Single(variants);
        Assert.Equal(42, variant.SourceUnitId);
        Assert.Equal("Photosynthesis", variant.TopicTag);
        Assert.Equal(canonical.Id, variant.ParentDraftId);
    }

    [Fact]
    public async Task VariantGeneration_UsesIncludedSourceUnit_WhenNavigationIsLoaded()
    {
        var generator = new QuestionVariantGenerator();
        var run = CreateRun();
        var unit = new QuestionSourceUnit
        {
            Id = 77,
            DocumentId = run.DocumentId,
            GenerationRunId = run.Id,
            Content = "Mitochondria convert nutrients into usable cell energy.",
            TopicTag = "Cell Energy",
            SourceHash = "unit-77"
        };
        var canonical = CreateCanonicalDraft(sourceUnitId: unit.Id, topicTag: unit.TopicTag);
        canonical.SourceUnit = unit;

        var variants = await generator.GenerateAsync(
            run,
            new[] { canonical },
            new[] { "ShortAnswer" },
            new[] { "Medium" },
            remainingDraftBudget: 1);

        var variant = Assert.Single(variants);
        Assert.Equal(unit.Id, variant.SourceUnitId);
        Assert.Same(unit, variant.SourceUnit);
        Assert.Equal(unit.TopicTag, variant.TopicTag);
    }

    [Fact]
    public async Task ImportAsync_FiltersInvalidDrafts_AndImportsEligibleDraftsInBatch()
    {
        await using var context = CreateDbContext();
        var run = CreateRun();
        context.QuestionGenerationRuns.Add(run);
        context.QuestionDrafts.AddRange(
            CreateImportDraft(101, run, "Verified", "Unique question text"),
            CreateImportDraft(102, run, "Rejected", "Rejected question text"),
            CreateImportDraft(103, run, "Verified", "Duplicate question text"),
            CreateImportDraft(104, run, "Verified", "Already imported question text"));
        context.Questions.AddRange(
            new Question
            {
                DocumentId = run.DocumentId,
                QuestionText = "Duplicate question text",
                QuestionType = QuestionType.ShortAnswer
            },
            new Question
            {
                DocumentId = run.DocumentId,
                QuestionText = "Existing imported question text",
                QuestionType = QuestionType.ShortAnswer,
                SourceDraftId = 104
            });
        await context.SaveChangesAsync();

        var service = new QuestionDraftImportService(context);

        var result = await service.ImportAsync(run.DocumentId, new[] { 101, 102, 103, 104, 999, 101 }, "reviewer");

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(new[] { 102, 103, 104, 999 }, result.SkippedDraftIds);

        var importedDraft = await context.QuestionDrafts.FindAsync(101);
        Assert.NotNull(importedDraft);
        Assert.Equal("Imported", importedDraft.Status);
        Assert.NotNull(importedDraft.ImportedAt);

        Assert.True(await context.Questions.AnyAsync(x => x.SourceDraftId == 101));
        Assert.True(await context.QuestionReviewEvents.AnyAsync(x => x.QuestionDraftId == 101 && x.Action == "Import"));

        var refreshedRun = await context.QuestionGenerationRuns.FindAsync(run.Id);
        Assert.NotNull(refreshedRun);
        Assert.Equal(1, refreshedRun.ImportedCount);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static QuestionGenerationRun CreateRun()
        => new()
        {
            Id = 7,
            DocumentId = 9,
            UserId = "user-1",
            Mode = "balanced",
            TargetDraftCount = 10
        };

    private static QuestionDraft CreateCanonicalDraft(int sourceUnitId, string topicTag)
        => new()
        {
            Id = 12,
            DocumentId = 9,
            GenerationRunId = 7,
            SourceUnitId = sourceUnitId,
            Status = "Verified",
            DraftKind = "Canonical",
            QuestionText = "Which statement best matches the source evidence?",
            QuestionType = "MultipleChoice",
            CorrectAnswer = "A",
            Explanation = "The answer follows from the source evidence.",
            Difficulty = "Medium",
            TopicTag = topicTag,
            SourceEvidence = "The source evidence supports the correct answer.",
            OverallScore = 0.95
        };

    private static QuestionDraft CreateImportDraft(int id, QuestionGenerationRun run, string status, string questionText)
        => new()
        {
            Id = id,
            DocumentId = run.DocumentId,
            GenerationRunId = run.Id,
            Status = status,
            DraftKind = "Canonical",
            QuestionText = questionText,
            QuestionType = "ShortAnswer",
            CorrectAnswer = "A grounded answer",
            Explanation = "A grounded explanation.",
            Difficulty = "Medium",
            TopicTag = "Topic",
            SourceEvidence = "Evidence",
            OverallScore = 0.9
        };
}
