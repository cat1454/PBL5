using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using ELearnGamePlatform.Core.Options;
using ELearnGamePlatform.Services.AI;
using ELearnGamePlatform.Services.DocumentProcessing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class DocumentUnderstandingTests
{
    [Fact]
    public async Task NoOpOrchestrator_ReturnsLegacyTextWithPassthroughStatus()
    {
        var orchestrator = CreateOrchestrator();
        var legacyText = string.Join(" ", Enumerable.Repeat("legacy extracted text has enough readable words for automatic generation", 45));

        var result = await orchestrator.UnderstandAsync(12, "sample.pdf", legacyText);

        Assert.Equal(12, result.DocumentId);
        Assert.Equal(legacyText, result.CombinedText);
        Assert.Equal(DocumentQualityStatuses.AutoGenerateAllowed, result.Status);
        Assert.True(result.Confidence >= 0.85d);
        Assert.Single(result.Pages);
        Assert.Single(result.Regions);
        Assert.Equal("legacy-text", result.Regions[0].RegionType);
    }

    [Fact]
    public async Task NoOpOrchestrator_UsesLowerConfidenceForShortText()
    {
        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.UnderstandAsync(3, "short.pdf", "short text");

        Assert.Equal("short text", result.CombinedText);
        Assert.True(result.Confidence < 0.45d);
        Assert.Equal(DocumentQualityStatuses.ExtractionFailed, result.Status);
        Assert.Contains(result.Warnings, warning => warning.Contains("too short", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NoOpOrchestrator_UsesLowConfidenceAndWarningForEmptyText()
    {
        var orchestrator = CreateOrchestrator();

        var result = await orchestrator.UnderstandAsync(4, "empty.pdf", "  ");

        Assert.Equal("  ", result.CombinedText);
        Assert.True(result.Confidence < 0.45d);
        Assert.Equal(DocumentQualityStatuses.ExtractionFailed, result.Status);
        Assert.Empty(result.Pages);
        Assert.Empty(result.Regions);
        Assert.Contains("Legacy extracted text was empty.", result.Warnings);
    }

    [Fact]
    public void QualityScorer_UsesPageSignalsAndOcrConfidence()
    {
        var scorer = new LegacyDocumentQualityScorer();
        var text = string.Join(
            Environment.NewLine,
            Enumerable.Repeat("Clean readable paragraph with enough OCR signal and stable lines for study generation.", 15));
        var report = new DocumentInputQualityReport
        {
            TotalPages = 3,
            AveragePageQuality = 76,
            AveragePageQualityWeighted = 76,
            BodyPageQualityAverage = 76,
            BodyPageCount = 3,
            Pages =
            [
                new DocumentPageProcessingReport
                {
                    PageNumber = 1,
                    Method = DocumentPageProcessingMethods.Ocr,
                    CharCount = 420,
                    WordCount = 70,
                    Confidence = 0.91,
                    QualityScore = 82,
                    PageRole = DocumentPageRoles.Body
                },
                new DocumentPageProcessingReport
                {
                    PageNumber = 2,
                    Method = DocumentPageProcessingMethods.Ocr,
                    CharCount = 390,
                    WordCount = 64,
                    Confidence = 0.88,
                    QualityScore = 78,
                    PageRole = DocumentPageRoles.Body
                },
                new DocumentPageProcessingReport
                {
                    PageNumber = 3,
                    Method = DocumentPageProcessingMethods.Ocr,
                    CharCount = 40,
                    WordCount = 6,
                    Confidence = 0.41,
                    QualityScore = 35,
                    PageRole = DocumentPageRoles.Body,
                    Warnings = ["Page has very few recognized words."]
                }
            ]
        };

        var result = scorer.Score(new DocumentQualityScoreInput
        {
            ExtractedText = text,
            PageQualityReport = report
        });

        Assert.Equal(DocumentQualityStatuses.NeedsReview, result.Status);
        Assert.True(result.Confidence is >= 0.65d and < 0.85d);
        Assert.True(result.NeedsReview);
        Assert.Equal(1, result.LowTextPageCount);
        Assert.True(result.AverageOcrConfidence < 0.85d);
        Assert.Contains(result.Reasons, reason => reason.Contains("low-text page", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void QualityScorer_MapsVeryNoisyTextToSummaryOnlyOrFailed()
    {
        var scorer = new LegacyDocumentQualityScorer();
        var noisyText = string.Join(Environment.NewLine, Enumerable.Repeat("@@@ ## x y z | || 11 ??", 35));

        var result = scorer.Score(new DocumentQualityScoreInput
        {
            ExtractedText = noisyText
        });

        Assert.True(result.Confidence < 0.65d);
        Assert.Contains(result.Status, new[] { DocumentQualityStatuses.SummaryOnlyRecommended, DocumentQualityStatuses.ExtractionFailed });
        Assert.True(result.GarbageRatio > 0.20d);
        Assert.Contains(result.Reasons, reason => reason.Contains("garbage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NoOpOrchestrator_WhenLayoutAnalysisEnabled_AddsTableAndDiagramRegionsWithoutDroppingLegacyText()
    {
        var orchestrator = CreateOrchestrator(enableLayoutAnalysis: true);
        var legacyText = string.Join(Environment.NewLine, new[]
        {
            "[Page 1]",
            "CHAPTER OVERVIEW",
            "Name | Score | Rank",
            "Lan | 9 | 1",
            "Minh | 8 | 2",
            "Input -> Process => Output"
        });

        var result = await orchestrator.UnderstandAsync(15, "layout.pdf", legacyText);

        Assert.StartsWith(legacyText, result.CombinedText, StringComparison.Ordinal);
        Assert.Contains("[Layout Analysis]", result.CombinedText);
        Assert.Contains(result.Regions, region => region.RegionType == DocumentRegionTypes.Title);
        Assert.Contains(result.Regions, region => region.RegionType == DocumentRegionTypes.TableLikeText);
        Assert.Contains(result.Regions, region => region.RegionType == DocumentRegionTypes.DiagramCandidate);
        Assert.Contains(result.Pages.Single().Regions, region => region.RegionType == DocumentRegionTypes.Text);
    }

    [Fact]
    public void HeuristicLayoutAnalyzer_ConvertsSimpleTableToMarkdownAndKeepsRawText()
    {
        var analyzer = new HeuristicLayoutAnalyzer();
        var text = string.Join(Environment.NewLine, new[]
        {
            "[Page 1]",
            "Name | Score | Rank",
            "Lan | 9 | 1",
            "Minh | 8 | 2"
        });

        var page = Assert.Single(analyzer.Analyze("table.pdf", text));
        var table = Assert.Single(page.Regions.Where(region => region.RegionType == DocumentRegionTypes.TableLikeText));

        Assert.Contains("| Name | Score | Rank |", table.Text);
        Assert.Contains("| --- | --- | --- |", table.Text);
        Assert.Equal("Name | Score | Rank\r\nLan | 9 | 1\r\nMinh | 8 | 2".ReplaceLineEndings(), table.RawText?.ReplaceLineEndings());
        Assert.True(table.LayoutConfidence >= 0.82d);
        Assert.False(table.NeedsReview);
    }

    [Fact]
    public void HeuristicLayoutAnalyzer_KeepsRaggedTableRawAndMarksLowConfidence()
    {
        var analyzer = new HeuristicLayoutAnalyzer();
        var text = string.Join(Environment.NewLine, new[]
        {
            "[Page 1]",
            "Metric | Value | Note",
            "Accuracy | 91",
            "Recall | 88 | measured | extra"
        });

        var page = Assert.Single(analyzer.Analyze("table.pdf", text));
        var table = Assert.Single(page.Regions.Where(region => region.RegionType == DocumentRegionTypes.TableLowConfidence));

        Assert.Contains("Accuracy | 91", table.Text);
        Assert.Equal(table.Text.ReplaceLineEndings(), table.RawText?.ReplaceLineEndings());
        Assert.True(table.NeedsReview);
        Assert.Contains("TableLowConfidence", table.ReviewTags ?? []);
    }

    [Fact]
    public async Task NoOpOrchestrator_MarksFormulaHeavyTextAsNeedsReview()
    {
        var orchestrator = CreateOrchestrator(enableLayoutAnalysis: true);
        var text = string.Join(Environment.NewLine, new[]
        {
            "[Page 1]",
            "Formula: E = m*c^2 + k",
            "x = (-b + sqrt(b^2 - 4ac)) / 2a"
        });

        var result = await orchestrator.UnderstandAsync(19, "formula.pdf", text);

        Assert.True(result.Quality?.NeedsReview);
        Assert.Contains(result.Regions, region => region.RegionType == DocumentRegionTypes.FormulaCandidate && region.NeedsReview);
        Assert.Contains(result.Warnings, warning => warning.Contains("formula-heavy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NoOpOrchestrator_WhenLayoutAnalysisEnabled_AddsFigureCandidateForImageHeavyPage()
    {
        var orchestrator = CreateOrchestrator(enableLayoutAnalysis: true);
        var report = new DocumentInputQualityReport
        {
            TotalPages = 1,
            Pages =
            [
                new DocumentPageProcessingReport
                {
                    PageNumber = 1,
                    Method = DocumentPageProcessingMethods.Ocr,
                    CharCount = 12,
                    WordCount = 2,
                    Confidence = 0.32
                }
            ]
        };

        var result = await orchestrator.UnderstandAsync(16, "diagram.pdf", "[Page 1]\nA B", report);

        Assert.Contains(result.Regions, region => region.RegionType == DocumentRegionTypes.FigureCandidate);
    }

    [Fact]
    public void HeuristicLayoutAnalyzer_DetectsPresentationChartTimelineAndNumericEvidence()
    {
        var analyzer = new HeuristicLayoutAnalyzer();
        var text = string.Join(Environment.NewLine, new[]
        {
            "[Page 1]",
            "Revenue by quarter",
            "Q1 120 24%",
            "Q2 180 36%",
            "Q3 210 42%",
            "2024 -> collect data -> train model -> evaluate results",
            "Accuracy = 91%"
        });

        var page = Assert.Single(analyzer.Analyze("chart.pdf", text));

        Assert.Contains(page.Regions, region => region.RegionType == DocumentRegionTypes.ChartCandidate);
        Assert.Contains(page.Regions, region => region.RegionType == DocumentRegionTypes.ProcessCandidate);
        Assert.Contains(page.Regions, region => region.RegionType == DocumentRegionTypes.NumericEvidence);
    }

    [Fact]
    public async Task NoOpOrchestrator_BuildsPresentationContractWithReviewOnlyChartEvidence()
    {
        var orchestrator = CreateOrchestrator(enableLayoutAnalysis: true);
        var text = string.Join(Environment.NewLine, new[]
        {
            "[Page 1]",
            "MODEL PERFORMANCE",
            "Metric | Value | Note",
            "Accuracy | 91",
            "Recall | 88 | measured | extra",
            "Q1 120 24%",
            "Q2 180 36%",
            "Formula: F1 = 2 * precision * recall / (precision + recall)",
            "Input -> Model -> Output"
        });

        var result = await orchestrator.UnderstandAsync(22, "metrics.pdf", text);

        Assert.NotNull(result.PresentationContract);
        Assert.NotEmpty(result.PresentationContract.SectionPlan);
        Assert.NotEmpty(result.PresentationContract.ChartCandidates);
        Assert.Contains(result.PresentationContract.ChartCandidates, chart => chart.NeedsReview);
        Assert.Contains(result.PresentationContract.VisualOpportunities, visual => visual.VisualRole == "process");
        Assert.True(result.PresentationContract.QualityMetrics.ReviewOnlyEvidenceCount > 0);
        Assert.Contains(result.PresentationContract.Warnings, warning => warning.Contains("review", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NoOpOrchestrator_WhenVisionDisabled_DoesNotCallVisionDescriber()
    {
        var vision = new FakeVisionRegionDescriber();
        var orchestrator = CreateOrchestrator(
            enableLayoutAnalysis: true,
            options: new DocumentUnderstandingOptions
            {
                Enabled = true,
                EnableLayoutAnalysis = true,
                EnableVisionAnalysis = false
            },
            visionRegionDescriber: vision);

        await orchestrator.UnderstandAsync(17, "diagram.pdf", "[Page 1]\nInput -> Process");

        Assert.Equal(0, vision.CallCount);
    }

    [Fact]
    public async Task NoOpOrchestrator_WhenVisionEnabled_AppendsDescriptionToCandidateRegion()
    {
        var vision = new FakeVisionRegionDescriber();
        var pageImages = new FakeVisionPageImageProvider();
        var orchestrator = CreateOrchestrator(
            enableLayoutAnalysis: true,
            options: new DocumentUnderstandingOptions
            {
                Enabled = true,
                EnableLayoutAnalysis = true,
                EnableVisionAnalysis = true,
                MaxVisionPagesPerDocument = 1,
                MaxVisionRegionsPerPage = 1
            },
            visionRegionDescriber: vision,
            visionPageImageProvider: pageImages);

        var result = await orchestrator.UnderstandAsync(18, "diagram.pdf", "[Page 1]\nInput -> Process");

        var region = Assert.Single(result.Regions.Where(item => item.RegionType == DocumentRegionTypes.DiagramCandidate));
        Assert.Equal("So do mo ta bang tieng Viet.", region.Description);
        Assert.Equal(["Input", "Process"], region.ExtractedLabels);
        Assert.Equal(["Input -> Process"], region.Relationships);
        Assert.Equal(0.9d, region.VisionConfidence);
        Assert.StartsWith("[Page 1]\nInput -> Process", result.CombinedText, StringComparison.Ordinal);
        Assert.Equal(1, vision.CallCount);
    }

    [Fact]
    public void KnowledgeMapBuilder_BuildsMarkdownWithTablesVisionConfidenceAndWarnings()
    {
        var builder = CreateKnowledgeMapBuilder();
        var result = new DocumentUnderstandingResult
        {
            DocumentId = 31,
            Status = DocumentQualityStatuses.NeedsReview,
            Confidence = 0.72d,
            CombinedText = "Document body explains a process with a table and diagram.",
            Quality = new DocumentQualityScoreResult
            {
                Status = DocumentQualityStatuses.NeedsReview,
                Confidence = 0.72d,
                NeedsReview = true
            },
            Warnings = ["Page 2 has low confidence."],
            Pages =
            [
                new PageUnderstandingResult
                {
                    PageNumber = 1,
                    Confidence = 0.72d,
                    Text = "Page one text describes the source document."
                }
            ],
            Regions =
            [
                new DocumentRegion
                {
                    PageNumber = 1,
                    RegionType = DocumentRegionTypes.TableLikeText,
                    Text = "Metric | Value\nAccuracy | 91\nRecall | 88"
                },
                new DocumentRegion
                {
                    PageNumber = 1,
                    RegionType = DocumentRegionTypes.DiagramCandidate,
                    Text = "Input -> Model -> Output",
                    Description = "Diagram shows inputs flowing through a model to an output.",
                    ExtractedLabels = ["Input", "Model", "Output"],
                    Relationships = ["Input -> Model", "Model -> Output"],
                    VisionConfidence = 0.86d,
                    UncertaintyReason = "Small labels may be hard to read."
                }
            ]
        };

        var map = builder.Build(result);

        Assert.True(map.IsUsable);
        Assert.Contains("## Tables", map.Text);
        Assert.Contains("Accuracy", map.Text);
        Assert.Contains("Figure And Diagram Descriptions", map.Text);
        Assert.Contains("inputs flowing through a model", map.Text);
        Assert.Contains("visionConfidence=0.86", map.Text);
        Assert.Contains("AI-generated visual descriptions", map.Text);
        Assert.Contains("Page 2 has low confidence", map.Text);
    }

    [Fact]
    public void KnowledgeMapBuilder_RendersLowConfidenceTablesAndFormulasWithCautions()
    {
        var builder = CreateKnowledgeMapBuilder();
        var result = new DocumentUnderstandingResult
        {
            DocumentId = 34,
            Status = DocumentQualityStatuses.NeedsReview,
            Confidence = 0.74d,
            CombinedText = "Readable lesson content with table and formula review regions.",
            Quality = new DocumentQualityScoreResult
            {
                Status = DocumentQualityStatuses.NeedsReview,
                Confidence = 0.74d,
                NeedsReview = true
            },
            Regions =
            [
                new DocumentRegion
                {
                    PageNumber = 2,
                    RegionType = DocumentRegionTypes.TableLowConfidence,
                    Text = "Metric | Value\nAccuracy | 91\nRecall |",
                    RawText = "Metric | Value\nAccuracy | 91\nRecall |",
                    LayoutConfidence = 0.62d,
                    NeedsReview = true,
                    ReviewTags = ["TableLowConfidence"]
                },
                new DocumentRegion
                {
                    PageNumber = 3,
                    RegionType = DocumentRegionTypes.FormulaCandidate,
                    Text = "x = (-b + sqrt(b^2 - 4ac)) / 2a",
                    RawText = "x = (-b + sqrt(b^2 - 4ac)) / 2a",
                    LayoutConfidence = 0.45d,
                    NeedsReview = true,
                    ReviewTags = ["FormulaHeavy"]
                }
            ]
        };

        var map = builder.Build(result);

        Assert.True(map.IsUsable);
        Assert.Contains("## Tables", map.Text);
        Assert.Contains("LOW CONFIDENCE", map.Text);
        Assert.Contains("## Formula Candidates", map.Text);
        Assert.Contains("do not repair notation", map.Text);
        Assert.Contains("do not infer missing table cells", map.Text);
    }

    [Fact]
    public void KnowledgeMapBuilder_ReturnsUnusableForLegacyPassthroughRun()
    {
        var builder = CreateKnowledgeMapBuilder();
        var run = new DocumentUnderstandingRun
        {
            DocumentId = 32,
            Status = NoOpDocumentUnderstandingOrchestrator.LegacyPassthroughStatus,
            CombinedText = "legacy text only"
        };

        var map = builder.Build(run);

        Assert.False(map.IsUsable);
        Assert.Contains("LegacyPassthrough", map.UnusableReason);
    }

    [Fact]
    public void KnowledgeMapBuilder_FitsLongInputsToTokenBudget()
    {
        var settings = new LocalLlmSettings
        {
            ContextWindowTokens = 900,
            ReservedOutputTokens = 120,
            ReservedInstructionTokens = 120,
            SafetyMarginTokens = 120,
            TargetInputBudgetFillRatio = 0.5d
        };
        var builder = CreateKnowledgeMapBuilder(settings);
        var longText = string.Join(" ", Enumerable.Repeat("Long page evidence with repeated educational details.", 500));
        var result = new DocumentUnderstandingResult
        {
            DocumentId = 33,
            Status = DocumentQualityStatuses.AutoGenerateAllowed,
            Confidence = 0.9d,
            CombinedText = longText,
            Pages =
            [
                new PageUnderstandingResult
                {
                    PageNumber = 1,
                    Confidence = 0.9d,
                    Text = longText
                }
            ],
            Regions =
            [
                new DocumentRegion
                {
                    PageNumber = 1,
                    RegionType = DocumentRegionTypes.Text,
                    Text = longText
                }
            ]
        };

        var map = builder.Build(result);

        Assert.True(map.IsUsable);
        Assert.True(map.EstimatedTokens <= settings.MaxInputTokens);
    }

    private static NoOpDocumentUnderstandingOrchestrator CreateOrchestrator(
        bool enableLayoutAnalysis = false,
        DocumentUnderstandingOptions? options = null,
        IVisionRegionDescriber? visionRegionDescriber = null,
        IVisionPageImageProvider? visionPageImageProvider = null)
        => new(
            new LegacyKnowledgeMapBuilder(),
            new LegacyDocumentQualityScorer(),
            new HeuristicLayoutAnalyzer(),
            visionRegionDescriber ?? new FakeVisionRegionDescriber(),
            visionPageImageProvider ?? new FakeVisionPageImageProvider(),
            Options.Create(options ?? new DocumentUnderstandingOptions { EnableLayoutAnalysis = enableLayoutAnalysis }),
            NullLogger<NoOpDocumentUnderstandingOrchestrator>.Instance);

    private static DocumentKnowledgeMapBuilder CreateKnowledgeMapBuilder(LocalLlmSettings? settings = null)
        => new(
            new TokenEstimator(),
            Options.Create(settings ?? new LocalLlmSettings
            {
                ContextWindowTokens = 4000,
                ReservedOutputTokens = 400,
                ReservedInstructionTokens = 300,
                SafetyMarginTokens = 300
            }),
            NullLogger<DocumentKnowledgeMapBuilder>.Instance);

    private sealed class FakeVisionRegionDescriber : IVisionRegionDescriber
    {
        public int CallCount { get; private set; }

        public Task<VisionRegionDescriptionResult> DescribeAsync(
            VisionRegionDescriptionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new VisionRegionDescriptionResult
            {
                Succeeded = true,
                Description = "So do mo ta bang tieng Viet.",
                ExtractedLabels = ["Input", "Process"],
                Relationships = ["Input -> Process"],
                Confidence = 0.9d
            });
        }
    }

    private sealed class FakeVisionPageImageProvider : IVisionPageImageProvider
    {
        public async Task<VisionPageImageSource?> GetPageImageAsync(
            string filePath,
            string fileType,
            int pageNumber,
            CancellationToken cancellationToken = default)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), $"elearn_test_vision_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            var imagePath = Path.Combine(tempDirectory, "page.png");
            await File.WriteAllTextAsync(imagePath, "fake-image", cancellationToken);

            return new VisionPageImageSource
            {
                ImagePath = imagePath,
                PageNumber = pageNumber,
                TemporaryDirectory = tempDirectory
            };
        }
    }
}
