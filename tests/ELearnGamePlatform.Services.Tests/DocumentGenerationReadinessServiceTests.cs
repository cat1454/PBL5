using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Options;
using ELearnGamePlatform.Services.DocumentProcessing;
using Microsoft.Extensions.Options;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class DocumentGenerationReadinessServiceTests
{
    [Theory]
    [InlineData(0.85, DocumentGenerationReadinessStatuses.Good, DocumentGenerationReadinessActions.Allow, false, false)]
    [InlineData(0.65, DocumentGenerationReadinessStatuses.NeedsReview, DocumentGenerationReadinessActions.AllowWithReviewWarning, false, false)]
    [InlineData(0.45, DocumentGenerationReadinessStatuses.LowConfidence, DocumentGenerationReadinessActions.WarnStrongly, false, false)]
    [InlineData(0.44, DocumentGenerationReadinessStatuses.ExtractionFailed, DocumentGenerationReadinessActions.BlockAutoGeneration, false, false)]
    public async Task ClassifiesConfidenceThresholdsWhenGateIsDisabled(
        double confidence,
        string expectedStatus,
        string expectedAction,
        bool expectedRequiresConfirmation,
        bool expectedBlocked)
    {
        var service = CreateService(confidence, enforceGate: false);

        var readiness = await service.GetReadinessAsync(CreateDocument());

        Assert.Equal(expectedStatus, readiness.Status);
        Assert.Equal(expectedAction, readiness.Action);
        Assert.Equal(expectedRequiresConfirmation, readiness.RequiresConfirmation);
        Assert.Equal(expectedBlocked, readiness.Blocked);
    }

    [Fact]
    public async Task RequiresConfirmationForLowConfidenceWhenGateIsEnabled()
    {
        var service = CreateService(0.45, enforceGate: true);

        var readiness = await service.GetReadinessAsync(CreateDocument());

        Assert.Equal(DocumentGenerationReadinessStatuses.LowConfidence, readiness.Status);
        Assert.True(readiness.RequiresConfirmation);
        Assert.True(readiness.Blocked);
    }

    [Fact]
    public async Task AllowsConfirmedLowConfidenceWhenGateIsEnabled()
    {
        var service = CreateService(0.45, enforceGate: true);

        var readiness = await service.GetReadinessAsync(CreateDocument(), confirmed: true);

        Assert.Equal(DocumentGenerationReadinessStatuses.LowConfidence, readiness.Status);
        Assert.False(readiness.RequiresConfirmation);
        Assert.False(readiness.Blocked);
    }

    [Fact]
    public async Task BlocksExtractionFailedWhenGateIsEnabled()
    {
        var service = CreateService(0.2, enforceGate: true);

        var readiness = await service.GetReadinessAsync(CreateDocument());

        Assert.Equal(DocumentGenerationReadinessStatuses.ExtractionFailed, readiness.Status);
        Assert.True(readiness.Blocked);
        Assert.False(readiness.RequiresConfirmation);
    }

    [Fact]
    public async Task FallsBackToLegacyTextScoringWhenRunConfidenceIsMissing()
    {
        var repository = new FakeRunRepository(new DocumentUnderstandingRun
        {
            DocumentId = 42,
            Status = "failed",
            DocumentConfidence = null,
            NeedsReview = true
        });
        var service = new DocumentGenerationReadinessService(
            repository,
            new LegacyDocumentQualityScorer(),
            Options.Create(new DocumentUnderstandingOptions()));

        var readiness = await service.GetReadinessAsync(CreateDocument());

        Assert.Equal(DocumentGenerationReadinessStatuses.Good, readiness.Status);
        Assert.Equal(DocumentGenerationReadinessActions.Allow, readiness.Action);
        Assert.False(readiness.Blocked);
    }

    [Fact]
    public async Task FallsBackToLegacyTextScoringWhenNoUnderstandingRunExists()
    {
        var service = new DocumentGenerationReadinessService(
            new FakeRunRepository(null),
            new LegacyDocumentQualityScorer(),
            Options.Create(new DocumentUnderstandingOptions()));

        var readiness = await service.GetReadinessAsync(CreateDocument());

        Assert.Equal(DocumentGenerationReadinessStatuses.Good, readiness.Status);
        Assert.Equal(DocumentGenerationReadinessActions.Allow, readiness.Action);
        Assert.False(readiness.Blocked);
    }

    [Fact]
    public async Task FailedRunReasonIsIncluded()
    {
        var service = CreateService(0.2, enforceGate: true, status: "failed");

        var readiness = await service.GetReadinessAsync(CreateDocument());

        Assert.Contains(readiness.Reasons, reason => reason.Contains("did not complete", StringComparison.OrdinalIgnoreCase));
    }

    private static DocumentGenerationReadinessService CreateService(
        double confidence,
        bool enforceGate,
        string status = "completed")
    {
        var run = new DocumentUnderstandingRun
        {
            DocumentId = 42,
            Status = status,
            DocumentConfidence = confidence,
            NeedsReview = confidence < 0.85d
        };

        return new DocumentGenerationReadinessService(
            new FakeRunRepository(run),
            new LegacyDocumentQualityScorer(),
            Options.Create(new DocumentUnderstandingOptions
            {
                EnforceGenerationGate = enforceGate,
                ShowGenerationWarnings = true,
                MinAutoGenerateConfidence = 0.85d,
                MinReviewRequiredConfidence = 0.65d,
                MinStrongWarningConfidence = 0.45d
            }));
    }

    private static Document CreateDocument()
        => new()
        {
            Id = 42,
            FileName = "source.pdf",
            FileType = ".pdf",
            FilePath = "source.pdf",
            UploadedBy = "1",
            ExtractedText = string.Join(" ", Enumerable.Repeat("Clean readable paragraph for reliable study generation.", 80))
        };

    private sealed class FakeRunRepository : IDocumentUnderstandingRunRepository
    {
        private readonly DocumentUnderstandingRun? _run;

        public FakeRunRepository(DocumentUnderstandingRun? run)
        {
            _run = run;
        }

        public Task<DocumentUnderstandingRun> CreateAsync(DocumentUnderstandingRun run)
            => Task.FromResult(run);

        public Task<DocumentUnderstandingRun?> GetLatestByDocumentIdAsync(int documentId)
            => Task.FromResult(_run?.DocumentId == documentId ? _run : null);
    }
}
