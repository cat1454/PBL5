using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using ELearnGamePlatform.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public class NoOpDocumentUnderstandingOrchestrator : IDocumentUnderstandingOrchestrator
{
    public const string LegacyPassthroughStatus = "LegacyPassthrough";

    private readonly IKnowledgeMapBuilder _knowledgeMapBuilder;
    private readonly IDocumentQualityScorer _qualityScorer;
    private readonly ILayoutAnalyzer _layoutAnalyzer;
    private readonly IVisionRegionDescriber _visionRegionDescriber;
    private readonly IVisionPageImageProvider _visionPageImageProvider;
    private readonly DocumentUnderstandingOptions _options;
    private readonly ILogger<NoOpDocumentUnderstandingOrchestrator> _logger;

    public NoOpDocumentUnderstandingOrchestrator(
        IKnowledgeMapBuilder knowledgeMapBuilder,
        IDocumentQualityScorer qualityScorer,
        ILayoutAnalyzer layoutAnalyzer,
        IVisionRegionDescriber visionRegionDescriber,
        IVisionPageImageProvider visionPageImageProvider,
        IOptions<DocumentUnderstandingOptions> options,
        ILogger<NoOpDocumentUnderstandingOrchestrator> logger)
    {
        _knowledgeMapBuilder = knowledgeMapBuilder;
        _qualityScorer = qualityScorer;
        _layoutAnalyzer = layoutAnalyzer;
        _visionRegionDescriber = visionRegionDescriber;
        _visionPageImageProvider = visionPageImageProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DocumentUnderstandingResult> UnderstandAsync(
        int documentId,
        string filePath,
        string? legacyExtractedText,
        DocumentInputQualityReport? pageQualityReport = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var combinedText = legacyExtractedText ?? string.Empty;
        var quality = _qualityScorer.Score(new DocumentQualityScoreInput
        {
            ExtractedText = combinedText,
            PageQualityReport = pageQualityReport
        });
        var legacyRegions = _knowledgeMapBuilder.BuildRegions(combinedText).ToList();
        var layoutPages = _options.EnableLayoutAnalysis
            ? _layoutAnalyzer.Analyze(filePath, legacyExtractedText, pageQualityReport).ToList()
            : new List<PageUnderstandingResult>();
        var visionDiscoveryPages = ShouldRunVision()
            ? (_options.EnableLayoutAnalysis
                ? layoutPages
                : _layoutAnalyzer.Analyze(filePath, legacyExtractedText, pageQualityReport).ToList())
            : new List<PageUnderstandingResult>();
        var layoutRegions = layoutPages.SelectMany(page => page.Regions).ToList();
        var visionCandidateRegions = visionDiscoveryPages
            .SelectMany(page => page.Regions)
            .Where(IsVisionCandidate)
            .ToList();
        var regions = _options.EnableLayoutAnalysis && layoutRegions.Count > 0
            ? layoutRegions
            : legacyRegions;
        if (!_options.EnableLayoutAnalysis && visionCandidateRegions.Count > 0)
        {
            regions = legacyRegions
                .Concat(visionCandidateRegions)
                .ToList();
        }

        var warnings = quality.Reasons.ToList();
        if (string.IsNullOrWhiteSpace(combinedText))
        {
            warnings.Add("Legacy extracted text was empty.");
        }

        AddLayoutReviewWarnings(layoutRegions, warnings);
        if (layoutRegions.Any(region => region.NeedsReview))
        {
            quality.NeedsReview = true;
            if (quality.Status == DocumentQualityStatuses.AutoGenerateAllowed)
            {
                quality.Status = DocumentQualityStatuses.NeedsReview;
            }
        }

        if (_options.EnableLayoutAnalysis)
        {
            combinedText = AppendLayoutDescriptions(combinedText, layoutRegions);
        }

        await EnrichVisionRegionsAsync(
            documentId,
            filePath,
            Path.GetExtension(filePath).TrimStart('.'),
            combinedText,
            visionCandidateRegions,
            cancellationToken);

        _logger.LogInformation(
            "DocumentUnderstanding quality passthrough for document {DocumentId}: status={Status}, confidence={Confidence}, chars={CharCount}, regions={RegionCount}, reasons={Reasons}",
            documentId,
            quality.Status,
            quality.Confidence,
            combinedText.Length,
            regions.Count,
            string.Join(" | ", warnings.Take(5)));

        var result = new DocumentUnderstandingResult
        {
            DocumentId = documentId,
            CombinedText = combinedText,
            Confidence = quality.Confidence,
            Status = quality.Status,
            Quality = quality,
            Regions = regions,
            Pages = _options.EnableLayoutAnalysis
                ? layoutPages
                : regions.Count == 0
                ? new List<PageUnderstandingResult>()
                : new List<PageUnderstandingResult>
                {
                    new()
                    {
                        PageNumber = 1,
                        Text = combinedText,
                        Confidence = quality.Confidence,
                        Regions = regions
                    }
                },
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        return result;
    }

    private static void AddLayoutReviewWarnings(
        IReadOnlyCollection<DocumentRegion> layoutRegions,
        ICollection<string> warnings)
    {
        foreach (var region in layoutRegions.Where(region => region.NeedsReview))
        {
            if (region.RegionType == DocumentRegionTypes.TableLowConfidence)
            {
                warnings.Add($"Page {region.PageNumber} contains a low-confidence table; preserve raw OCR and review before using exact numbers.");
            }
            else if (region.RegionType == DocumentRegionTypes.FormulaCandidate)
            {
                warnings.Add($"Page {region.PageNumber} contains formula-heavy text; review before using exact formulas or calculations.");
            }
        }
    }

    private bool ShouldRunVision()
        => _options.Enabled && _options.EnableVisionAnalysis;

    private async Task EnrichVisionRegionsAsync(
        int documentId,
        string filePath,
        string fileType,
        string combinedText,
        IReadOnlyCollection<DocumentRegion> candidateRegions,
        CancellationToken cancellationToken)
    {
        if (!ShouldRunVision())
        {
            return;
        }

        var maxPages = Math.Max(0, _options.MaxVisionPagesPerDocument);
        var maxRegionsPerPage = Math.Max(0, _options.MaxVisionRegionsPerPage);
        if (maxPages == 0 || maxRegionsPerPage == 0)
        {
            _logger.LogInformation(
                "DocumentUnderstanding vision skipped for document {DocumentId}: pageLimit={PageLimit}, regionLimit={RegionLimit}",
                documentId,
                maxPages,
                maxRegionsPerPage);
            return;
        }

        var selectedGroups = candidateRegions
            .GroupBy(region => region.PageNumber)
            .OrderBy(group => group.Key)
            .Take(maxPages)
            .Select(group => new
            {
                PageNumber = group.Key,
                Regions = group.Take(maxRegionsPerPage).ToList()
            })
            .Where(group => group.Regions.Count > 0)
            .ToList();

        var selectedRegionCount = selectedGroups.Sum(group => group.Regions.Count);
        _logger.LogInformation(
            "DocumentUnderstanding vision selected document {DocumentId}: model={VisionModel}, candidatePages={CandidatePages}, candidateRegions={CandidateRegions}, selectedPages={SelectedPages}, selectedRegions={SelectedRegions}",
            documentId,
            _options.VisionModel,
            candidateRegions.Select(region => region.PageNumber).Distinct().Count(),
            candidateRegions.Count,
            selectedGroups.Count,
            selectedRegionCount);

        var sentPages = 0;
        var sentRegions = 0;
        var promptContext = BuildPromptContext(combinedText);

        foreach (var group in selectedGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var imageSource = await _visionPageImageProvider.GetPageImageAsync(
                filePath,
                fileType,
                group.PageNumber,
                cancellationToken);
            if (imageSource == null || !File.Exists(imageSource.ImagePath))
            {
                _logger.LogWarning(
                    "DocumentUnderstanding vision could not get page image for document {DocumentId}, page {PageNumber}.",
                    documentId,
                    group.PageNumber);
                continue;
            }

            sentPages++;
            foreach (var region in group.Regions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stopwatch = Stopwatch.StartNew();
                var result = await _visionRegionDescriber.DescribeAsync(
                    new VisionRegionDescriptionRequest
                    {
                        ImagePath = imageSource.ImagePath,
                        Model = _options.VisionModel,
                        PageNumber = region.PageNumber,
                        RegionType = region.RegionType,
                        RegionText = region.Text,
                        PromptContext = promptContext,
                        TimeoutSeconds = _options.VisionTimeoutSeconds
                    },
                    cancellationToken);
                stopwatch.Stop();
                sentRegions++;

                _logger.LogInformation(
                    "DocumentUnderstanding vision processed document {DocumentId}, page {PageNumber}, region {RegionType}: success={Success}, elapsedMs={ElapsedMs}",
                    documentId,
                    region.PageNumber,
                    region.RegionType,
                    result.Succeeded,
                    stopwatch.ElapsedMilliseconds);

                if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Description))
                {
                    continue;
                }

                region.Description = result.Description;
                region.ExtractedLabels = result.ExtractedLabels;
                region.Relationships = result.Relationships;
                region.VisionConfidence = result.Confidence;
                region.UncertaintyReason = result.UncertaintyReason;
            }
        }

        _logger.LogInformation(
            "DocumentUnderstanding vision finished for document {DocumentId}: model={VisionModel}, pagesSent={PagesSent}, regionsSent={RegionsSent}",
            documentId,
            _options.VisionModel,
            sentPages,
            sentRegions);
    }

    private static bool IsVisionCandidate(DocumentRegion region)
        => region.RegionType is DocumentRegionTypes.FigureCandidate or DocumentRegionTypes.DiagramCandidate;

    private static string BuildPromptContext(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= 2000 ? normalized : normalized[..2000];
    }

    private static string AppendLayoutDescriptions(string legacyText, IReadOnlyCollection<DocumentRegion> layoutRegions)
    {
        var descriptiveRegions = layoutRegions
            .Where(region => region.RegionType is DocumentRegionTypes.Title
                or DocumentRegionTypes.TableLikeText
                or DocumentRegionTypes.FigureCandidate
                or DocumentRegionTypes.DiagramCandidate)
            .Take(40)
            .ToList();
        if (descriptiveRegions.Count == 0)
        {
            return legacyText;
        }

        var builder = new StringBuilder();
        builder.Append(legacyText);
        if (!string.IsNullOrWhiteSpace(legacyText))
        {
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.AppendLine("[Layout Analysis]");
        foreach (var region in descriptiveRegions)
        {
            var summary = region.Text.ReplaceLineEndings(" ");
            if (summary.Length > 180)
            {
                summary = summary[..180].TrimEnd() + "...";
            }

            builder.AppendLine($"[Page {region.PageNumber}] {region.RegionType}: {summary}");
        }

        return builder.ToString().TrimEnd();
    }
}
