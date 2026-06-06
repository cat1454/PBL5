using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using ELearnGamePlatform.API.Controllers;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.API.Services.QuestionStudio;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class QuestionStudioReviewFixTests
{
    [Fact]
    public void BuildDraftPayload_ReturnsCamelCasedOptionPayloads()
    {
        var draft = CreateCanonicalDraft(sourceUnitId: 42, topicTag: "Photosynthesis");
        draft.OptionsJson = JsonSerializer.Serialize(new List<QuestionOption>
        {
            new() { Key = "A", Text = "Chlorophyll captures light.", IsCorrect = true },
            new() { Key = "B", Text = "Roots create sunlight.", IsCorrect = false }
        });

        var payload = InvokePrivateStatic<object>("BuildDraftPayload", draft);
        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        var options = document.RootElement.GetProperty("options");

        Assert.Equal("A", options[0].GetProperty("key").GetString());
        Assert.Equal("Chlorophyll captures light.", options[0].GetProperty("text").GetString());
        Assert.True(options[0].GetProperty("isCorrect").GetBoolean());
        Assert.False(options[0].TryGetProperty("Key", out _));
        Assert.False(options[0].TryGetProperty("Text", out _));
    }

    [Fact]
    public void CalculateRunProgressPercent_UsesLateRangeForVariantDeduplication()
    {
        var canonicalDedup = CreateRun();
        canonicalDedup.Stage = "DeduplicatingCanonical";
        canonicalDedup.GeneratedDraftCount = 10;
        var variantDedup = CreateRun();
        variantDedup.Stage = "DeduplicatingVariants";
        variantDedup.GeneratedDraftCount = 10;

        var canonicalProgress = InvokePrivateStatic<int>("CalculateRunProgressPercent", canonicalDedup);
        var variantProgress = InvokePrivateStatic<int>("CalculateRunProgressPercent", variantDedup);

        Assert.Equal(75, canonicalProgress);
        Assert.Equal(99, variantProgress);
        Assert.True(variantProgress > canonicalProgress);
    }

    [Fact]
    public async Task UpdateDraft_RejectsMoreThanSixOptions()
    {
        await using var context = CreateDbContext();
        var document = new Document
        {
            Id = 501,
            FileName = "lesson.pdf",
            FileType = "PDF",
            FilePath = "lesson.pdf",
            UploadedBy = "5"
        };
        var draft = CreateCanonicalDraft(sourceUnitId: 42, topicTag: "Photosynthesis");
        draft.Id = 601;
        draft.DocumentId = document.Id;
        context.Documents.Add(document);
        context.QuestionDrafts.Add(draft);
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var result = await controller.UpdateDraft(draft.Id, new UpdateQuestionDraftRequest
        {
            Options = new List<string> { "A. One", "B. Two", "C. Three", "D. Four", "E. Five", "F. Six", "G. Seven" }
        }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Equal("invalid_options", error.Code);
    }

    [Fact]
    public async Task RunAsync_MarksRunFailed_WhenSourceDocumentIsMissing()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var databaseRoot = new InMemoryDatabaseRoot();
        int runId;

        await using (var seedContext = CreateDbContext(databaseName, databaseRoot))
        {
            var run = CreateRun();
            run.Id = 901;
            run.DocumentId = 902;
            run.Status = "Pending";
            run.Stage = "Created";
            seedContext.QuestionGenerationRuns.Add(run);
            await seedContext.SaveChangesAsync();
            runId = run.Id;
        }

        await using var context = CreateDbContext(databaseName, databaseRoot);

        var orchestrator = new QuestionStudioOrchestrator(
            context,
            new ThrowingSourceUnitExtractor(),
            new ThrowingCanonicalQuestionGenerator(),
            new ThrowingQuestionVariantGenerator(),
            new ThrowingQuestionDraftVerifier(),
            new ThrowingQuestionDraftDeduplicator(),
            new QuestionStudioRunControlStore(),
            NullLogger<QuestionStudioOrchestrator>.Instance);

        await orchestrator.RunAsync(runId);

        await using var assertContext = CreateDbContext(databaseName, databaseRoot);
        var refreshedRun = await assertContext.QuestionGenerationRuns.FindAsync(runId);
        Assert.NotNull(refreshedRun);
        Assert.Equal("Failed", refreshedRun.Status);
        Assert.Equal("Failed", refreshedRun.Stage);
        Assert.Equal("Question Studio source document was not found.", refreshedRun.ErrorMessage);
        Assert.NotNull(refreshedRun.CompletedAt);
    }

    [Fact]
    public async Task RunControls_PersistPauseResumeAndCancelStates()
    {
        await using var context = CreateDbContext();
        var document = new Document
        {
            Id = 701,
            FileName = "lesson.pdf",
            FileType = "PDF",
            FilePath = "lesson.pdf",
            UploadedBy = "5",
            Status = DocumentStatus.Completed,
            ExtractedText = "Ready content"
        };
        var run = CreateRun();
        run.Id = 702;
        run.DocumentId = document.Id;
        run.UserId = "5";
        run.Status = "Running";
        run.Stage = "GeneratingCanonical";
        context.Documents.Add(document);
        context.QuestionGenerationRuns.Add(run);
        await context.SaveChangesAsync();
        var controlStore = new QuestionStudioRunControlStore();
        controlStore.RegisterRun(run.Id);
        var controller = CreateController(context, controlStore);

        Assert.IsType<OkObjectResult>(await controller.PauseRun(run.Id, CancellationToken.None));
        Assert.Equal("Paused", (await context.QuestionGenerationRuns.FindAsync(run.Id))?.Status);

        Assert.IsType<OkObjectResult>(await controller.ResumeRun(run.Id, CancellationToken.None));
        Assert.Equal("Running", (await context.QuestionGenerationRuns.FindAsync(run.Id))?.Status);

        Assert.IsType<OkObjectResult>(await controller.CancelRun(run.Id, CancellationToken.None));
        var cancelled = await context.QuestionGenerationRuns.FindAsync(run.Id);
        Assert.Equal("Cancelled", cancelled?.Status);
        Assert.NotNull(cancelled?.CompletedAt);
    }

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
        => CreateDbContext(Guid.NewGuid().ToString("N"), new InMemoryDatabaseRoot());

    private static ApplicationDbContext CreateDbContext(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static QuestionStudioController CreateController(
        ApplicationDbContext context,
        IQuestionStudioRunControlStore? controlStore = null)
    {
        var controller = new QuestionStudioController(
            context,
            new NoopQuestionDraftImportService(),
            controlStore ?? new QuestionStudioRunControlStore(),
            scopeFactory: null!,
            NullLogger<QuestionStudioController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "5")
                }, "Test"))
            }
        };

        return controller;
    }

    private static T InvokePrivateStatic<T>(string methodName, params object?[] parameters)
    {
        var method = typeof(QuestionStudioController).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<T>(method.Invoke(null, parameters));
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

    private sealed class NoopQuestionDraftImportService : IQuestionDraftImportService
    {
        public Task<QuestionDraftImportResult> ImportAsync(int documentId, IReadOnlyCollection<int> draftIds, string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(new QuestionDraftImportResult(0, 0, Array.Empty<int>()));
    }

    private sealed class ThrowingSourceUnitExtractor : IQuestionSourceUnitExtractor
    {
        public Task<List<QuestionSourceUnit>> ExtractAsync(Document document, int generationRunId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("This fake should not be called.");
    }

    private sealed class ThrowingCanonicalQuestionGenerator : ICanonicalQuestionGenerator
    {
        public Task<List<QuestionDraft>> GenerateAsync(
            QuestionGenerationRun run,
            IReadOnlyCollection<QuestionSourceUnit> sourceUnits,
            IReadOnlyCollection<string> questionTypes,
            IReadOnlyCollection<string> difficulties,
            int maxDrafts,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("This fake should not be called.");
    }

    private sealed class ThrowingQuestionVariantGenerator : IQuestionVariantGenerator
    {
        public Task<List<QuestionDraft>> GenerateAsync(
            QuestionGenerationRun run,
            IReadOnlyCollection<QuestionDraft> canonicalDrafts,
            IReadOnlyCollection<string> questionTypes,
            IReadOnlyCollection<string> difficulties,
            int remainingDraftBudget,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("This fake should not be called.");
    }

    private sealed class ThrowingQuestionDraftVerifier : IQuestionDraftVerifier
    {
        public Task VerifyAsync(QuestionDraft draft, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("This fake should not be called.");
    }

    private sealed class ThrowingQuestionDraftDeduplicator : IQuestionDraftDeduplicator
    {
        public Task<bool> IsExactDuplicateAsync(QuestionDraft draft, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("This fake should not be called.");

        public Task<bool> IsNearDuplicateAsync(QuestionDraft draft, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("This fake should not be called.");

        public Task MarkDuplicatesAsync(int generationRunId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("This fake should not be called.");
    }
}
