using System.Text.Json;
using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using ELearnGamePlatform.Services.AI;
using ELearnGamePlatform.Services.DocumentProcessing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class PresentationExtractionContractTests
{
    [Fact]
    public void DocumentKnowledgeMapBuilder_RendersPresentationContractSections()
    {
        var builder = CreateBuilder();
        var result = new DocumentUnderstandingResult
        {
            DocumentId = 10,
            CombinedText = "Model evaluation uses accuracy, recall, and a training pipeline.",
            Confidence = 0.9,
            Status = DocumentQualityStatuses.AutoGenerateAllowed,
            Quality = new DocumentQualityScoreResult { Status = DocumentQualityStatuses.AutoGenerateAllowed, Confidence = 0.9 },
            Pages =
            [
                new PageUnderstandingResult
                {
                    PageNumber = 1,
                    Text = "Model evaluation uses accuracy, recall, and a training pipeline.",
                    Confidence = 0.9,
                    Regions =
                    [
                        new DocumentRegion { PageNumber = 1, RegionType = DocumentRegionTypes.Text, Text = "Model evaluation uses accuracy, recall, and a training pipeline." }
                    ]
                }
            ],
            Regions =
            [
                new DocumentRegion { PageNumber = 1, RegionType = DocumentRegionTypes.Text, Text = "Model evaluation uses accuracy, recall, and a training pipeline." }
            ],
            PresentationContract = new PresentationExtractionContract
            {
                SourceSummary = "Evaluation pipeline with metrics.",
                SectionPlan =
                [
                    new PresentationSectionPlan
                    {
                        SectionId = "section-01",
                        Heading = "Evaluation",
                        Rhythm = "dense",
                        PreferredChunkIds = ["C01"]
                    }
                ],
                AudienceProfile = new PresentationAudienceProfile
                {
                    Level = "intermediate",
                    ReadingDifficulty = "medium",
                    JargonTerms = ["accuracy"]
                },
                PresentationFlow = new PresentationFlowPlan
                {
                    SuggestedOpening = "Open with evaluation.",
                    SectionToSlideMap =
                    [
                        new PresentationSectionSlideMap { SectionId = "section-01", Heading = "Evaluation", SuggestedSlideIndex = 1 }
                    ]
                },
                SlideAffordances =
                [
                    new PresentationSlideAffordance
                    {
                        SectionId = "section-01",
                        SuggestedLayout = "chart-review",
                        Rhythm = "dense",
                        VisualRole = "process",
                        ChartIntent = "bar_chart",
                        Density = "high",
                        SlideabilityScore = 0.62,
                        SuggestedQuickActions = ["remove-unsupported-chart-numbers"]
                    }
                ],
                SourceGrounding =
                [
                    new PresentationSourceGrounding
                    {
                        SectionId = "section-01",
                        ChunkIds = ["C01"],
                        PageNumbers = [1],
                        Confidence = 0.91,
                        EvidenceExcerpt = "Accuracy 91, Recall 88"
                    }
                ],
                VisualOpportunities =
                [
                    new PresentationVisualOpportunity
                    {
                        PageNumber = 1,
                        SectionId = "section-01",
                        VisualRole = "process",
                        ImageRendering = "vector-illustration",
                        ImagePalette = "academic-blue"
                    }
                ],
                ChartCandidates =
                [
                    new PresentationChartCandidate
                    {
                        PageNumber = 1,
                        SectionId = "section-01",
                        ChartType = "bar_chart",
                        EvidenceText = "Accuracy 91, Recall 88",
                        NeedsReview = true,
                        ReviewReason = "axis scale missing"
                    }
                ],
                UxReviewHints =
                [
                    new PresentationUxReviewHint
                    {
                        Severity = "high",
                        HintType = "chart-review",
                        PageNumber = 1,
                        SectionId = "section-01",
                        Message = "Chart needs review.",
                        SuggestedAction = "Confirm scale."
                    }
                ],
                Warnings = ["Chart candidate requires review."]
            }
        };

        var map = builder.Build(result);

        Assert.True(map.IsUsable);
        Assert.Contains("Presentation Extraction Contract", map.Text);
        Assert.Contains("section-01", map.Text);
        Assert.Contains("Evidence And Grounding", map.Text);
        Assert.Contains("Slide Affordances", map.Text);
        Assert.Contains("UX Review Warnings", map.Text);
        Assert.Contains("bar_chart", map.Text);
        Assert.Contains("axis scale missing", map.Text);
        Assert.Contains("vector-illustration", map.Text);
    }

    [Fact]
    public void PresentationExtractionContractBuilder_DerivesUxFirstMetadata()
    {
        var pages = new List<PageUnderstandingResult>
        {
            new()
            {
                PageNumber = 1,
                Confidence = 0.52,
                Text = "Learning objective: compare model accuracy and recall. Accuracy 91% Recall 88% Precision 86% F1 89% chart trend axis 0% 100%. Example: validation pipeline.",
                Regions =
                [
                    new DocumentRegion { PageNumber = 1, RegionType = DocumentRegionTypes.Title, Text = "Model Evaluation" }
                ]
            }
        };
        var regions = new List<DocumentRegion>
        {
            new() { PageNumber = 1, RegionType = DocumentRegionTypes.Title, Text = "Model Evaluation" },
            new() { PageNumber = 1, RegionType = DocumentRegionTypes.Text, Text = "Learning objective: compare model accuracy and recall. Example: validation pipeline." },
            new()
            {
                PageNumber = 1,
                RegionType = DocumentRegionTypes.ChartCandidate,
                Text = "Accuracy 91% Recall 88% Precision 86% F1 89% chart trend",
                RawText = "Accuracy 91% Recall 88% Precision 86% F1 89% chart trend",
                NeedsReview = true,
                ReviewTags = ["ScaleMissing", "NeedsReview"],
                LayoutConfidence = 0.52
            }
        };
        var quality = new DocumentQualityScoreResult
        {
            Status = DocumentQualityStatuses.NeedsReview,
            Confidence = 0.61,
            NeedsReview = true,
            Reasons = ["Weak OCR on page 1"]
        };

        var contract = PresentationExtractionContractBuilder.Build(pages, regions, quality);

        Assert.NotNull(contract.AudienceProfile);
        Assert.NotEmpty(contract.PresentationFlow.SectionToSlideMap);
        Assert.Contains(contract.SlideAffordances, item => item.SuggestedQuickActions.Contains("remove-unsupported-chart-numbers"));
        Assert.Contains(contract.SourceGrounding, item => item.MissingEvidenceWarnings.Count > 0);
        Assert.Contains(contract.UxReviewHints, item => item.Severity == "high" && item.HintType.Contains("chart", StringComparison.OrdinalIgnoreCase));
        Assert.True(contract.QualityMetrics.UxReviewHintCount > 0);
        Assert.True(contract.QualityMetrics.AverageSlideabilityScore > 0);
    }

    [Fact]
    public void DocumentKnowledgeMapBuilder_ReadsPresentationContractFromRunJson()
    {
        var builder = CreateBuilder();
        var run = new DocumentUnderstandingRun
        {
            DocumentId = 11,
            Status = DocumentQualityStatuses.AutoGenerateAllowed,
            DocumentConfidence = 0.9,
            CombinedText = "Pipeline metrics are available.",
            ResultJson = JsonSerializer.Serialize(new
            {
                Pages = new[]
                {
                    new
                    {
                        PageNumber = 1,
                        Text = "Pipeline metrics are available.",
                        Confidence = 0.9,
                        Regions = new[]
                        {
                            new { PageNumber = 1, RegionType = DocumentRegionTypes.Text, Text = "Pipeline metrics are available." }
                        }
                    }
                },
                Regions = new[]
                {
                    new { PageNumber = 1, RegionType = DocumentRegionTypes.Text, Text = "Pipeline metrics are available." }
                },
                PresentationContract = new
                {
                    SourceSummary = "Pipeline metrics.",
                    SectionPlan = new[]
                    {
                        new { SectionId = "section-run", Heading = "Pipeline", Rhythm = "dense", PreferredChunkIds = new[] { "C01" } }
                    },
                    ChartCandidates = new[]
                    {
                        new { PageNumber = 1, SectionId = "section-run", ChartType = "line_chart", EvidenceText = "metric trend", NeedsReview = false }
                    }
                }
            })
        };

        var map = builder.Build(run);

        Assert.True(map.IsUsable);
        Assert.Contains("section-run", map.Text);
        Assert.Contains("line_chart", map.Text);
    }

    private static DocumentKnowledgeMapBuilder CreateBuilder()
        => new(
            new TokenEstimator(),
            Options.Create(new LocalLlmSettings
            {
                ContextWindowTokens = 8192,
                TargetInputBudgetFillRatio = 1
            }),
            NullLogger<DocumentKnowledgeMapBuilder>.Instance);
}
