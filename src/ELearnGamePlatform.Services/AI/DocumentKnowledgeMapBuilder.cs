using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ELearnGamePlatform.Services.AI;

public class DocumentKnowledgeMapBuilder : IDocumentKnowledgeMapBuilder
{
    private const int CombinedTextExcerptChars = 1800;
    private const int PageTextExcerptChars = 700;
    private const int RegionTextExcerptChars = 520;
    private const int DescriptionExcerptChars = 700;
    private const int MinimumUsableChars = 160;
    private static readonly HashSet<string> FailedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Failed",
        "LegacyPassthrough"
    };

    private readonly ITokenEstimator _tokenEstimator;
    private readonly LocalLlmSettings _settings;
    private readonly ILogger<DocumentKnowledgeMapBuilder> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DocumentKnowledgeMapBuilder(
        ITokenEstimator tokenEstimator,
        IOptions<LocalLlmSettings> settings,
        ILogger<DocumentKnowledgeMapBuilder> logger)
    {
        _tokenEstimator = tokenEstimator;
        _settings = settings.Value;
        _logger = logger;
    }

    public KnowledgeMapBuildResult Build(DocumentUnderstandingResult? result)
    {
        if (result == null)
        {
            return Unusable("no-understanding-result");
        }

        if (FailedStatuses.Contains(result.Status))
        {
            return Unusable($"understanding-status-{result.Status}");
        }

        var pages = result.Pages
            .OrderBy(page => page.PageNumber)
            .ToList();
        var regions = result.Regions.Any()
            ? result.Regions
            : pages.SelectMany(page => page.Regions).ToList();
        regions = regions
            .Where(region => !string.IsNullOrWhiteSpace(region.Text) || !string.IsNullOrWhiteSpace(region.Description))
            .OrderBy(region => region.PageNumber)
            .ThenBy(region => GetRegionPriority(region.RegionType))
            .ToList();

        var meaningfulRegions = regions
            .Where(region => !string.Equals(region.RegionType, "legacy-text", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var hasRichRegion = meaningfulRegions.Any(region =>
            region.RegionType is DocumentRegionTypes.TableLikeText
                or DocumentRegionTypes.TableLowConfidence
                or DocumentRegionTypes.FormulaCandidate
                or DocumentRegionTypes.FigureCandidate
                or DocumentRegionTypes.DiagramCandidate
                or DocumentRegionTypes.Title
                or DocumentRegionTypes.Text);
        if (!hasRichRegion)
        {
            return Unusable("no-meaningful-layout-or-vision-regions");
        }

        var warnings = result.Warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (result.Quality?.NeedsReview == true)
        {
            warnings.Add("DocumentUnderstanding marked this document as needing review.");
        }

        var blocks = BuildBlocks(result, pages, meaningfulRegions, warnings);
        var fitted = FitBlocksToBudget(blocks, warnings);
        var text = string.Join(Environment.NewLine + Environment.NewLine, fitted.Select(block => block.Text)).Trim();
        var estimatedTokens = _tokenEstimator.EstimateTokens(text);

        if (NormalizeForLength(text).Length < MinimumUsableChars)
        {
            return Unusable("knowledge-map-too-thin", warnings);
        }

        return new KnowledgeMapBuildResult
        {
            Text = text,
            IsUsable = true,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            EstimatedTokens = estimatedTokens
        };
    }

    public KnowledgeMapBuildResult Build(DocumentUnderstandingRun? run)
    {
        if (run == null)
        {
            return Unusable("no-understanding-run");
        }

        if (FailedStatuses.Contains(run.Status))
        {
            return Unusable($"understanding-run-status-{run.Status}");
        }

        try
        {
            var payload = string.IsNullOrWhiteSpace(run.ResultJson)
                ? null
                : JsonSerializer.Deserialize<UnderstandingRunPayload>(run.ResultJson, _jsonOptions);
            var result = new DocumentUnderstandingResult
            {
                DocumentId = run.DocumentId,
                CombinedText = run.CombinedText ?? string.Empty,
                Confidence = run.DocumentConfidence ?? 0d,
                Status = run.Status,
                Quality = payload?.Quality,
                Pages = payload?.Pages ?? new List<PageUnderstandingResult>(),
                Regions = payload?.Regions ?? new List<DocumentRegion>(),
                Warnings = payload?.Warnings ?? ParseFailureReasons(run.FailureReasonsJson)
            };

            return Build(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not build Knowledge Map from DocumentUnderstandingRun {RunId}.", run.Id);
            return Unusable("understanding-run-parse-failed", new[] { ex.Message });
        }
    }

    private List<KnowledgeMapBlock> BuildBlocks(
        DocumentUnderstandingResult result,
        IReadOnlyList<PageUnderstandingResult> pages,
        IReadOnlyList<DocumentRegion> regions,
        IReadOnlyList<string> warnings)
    {
        var blocks = new List<KnowledgeMapBlock>
        {
            Block(0, $@"# Knowledge Map Context

Safety rules for analysis:
- Use only evidence in this Knowledge Map and the supplied document text.
- Do not invent facts, labels, relationships, formulas, or claims outside the document.
- Mark low-confidence pages or regions as needing human review.
- Do not infer missing table cells, repair formulas, or use low-confidence table/formula evidence for exact numeric calculations.
- Figure and diagram descriptions are AI-generated visual descriptions; use them carefully and note uncertainty.

## Document Summary Source
- status: {SafeLine(result.Status)}
- documentConfidence: {FormatConfidence(result.Confidence)}
- needsReview: {result.Quality?.NeedsReview.ToString() ?? "unknown"}
- qualityStatus: {SafeLine(result.Quality?.Status)}
- combinedTextExcerpt: {Excerpt(result.CombinedText, CombinedTextExcerptChars)}")
        };

        if (warnings.Count > 0)
        {
            blocks.Add(Block(1, $@"## Review Warnings
{BulletLines(warnings.Take(12))}"));
        }

        var tableRegions = regions
            .Where(region => region.RegionType is DocumentRegionTypes.TableLikeText or DocumentRegionTypes.TableLowConfidence)
            .Take(12)
            .ToList();
        if (tableRegions.Count > 0)
        {
            blocks.Add(Block(2, $@"## Tables
{string.Join(Environment.NewLine, tableRegions.Select(RenderTableRegion))}"));
        }

        var formulaRegions = regions
            .Where(region => region.RegionType == DocumentRegionTypes.FormulaCandidate)
            .Take(12)
            .ToList();
        if (formulaRegions.Count > 0)
        {
            blocks.Add(Block(3, $@"## Formula Candidates
Use formula candidates only as raw evidence. Do not repair notation or create exact calculation questions unless the source is independently clear.
{string.Join(Environment.NewLine, formulaRegions.Select(RenderFormulaRegion))}"));
        }

        var visualRegions = regions
            .Where(region => region.RegionType is DocumentRegionTypes.FigureCandidate or DocumentRegionTypes.DiagramCandidate)
            .Take(16)
            .ToList();
        if (visualRegions.Count > 0)
        {
            blocks.Add(Block(4, $@"## Figure And Diagram Descriptions
{string.Join(Environment.NewLine, visualRegions.Select(RenderVisualRegion))}"));
        }

        var titleAndTextRegions = regions
            .Where(region => region.RegionType is DocumentRegionTypes.Title or DocumentRegionTypes.Text)
            .Take(18)
            .ToList();
        if (titleAndTextRegions.Count > 0)
        {
            blocks.Add(Block(5, $@"## Key Text Blocks
{string.Join(Environment.NewLine, titleAndTextRegions.Select(RenderTextRegion))}"));
        }

        if (pages.Count > 0)
        {
            blocks.Add(Block(6, $@"## Page Sections
{string.Join(Environment.NewLine, pages.Take(30).Select(RenderPage))}"));
        }

        return blocks;
    }

    private List<KnowledgeMapBlock> FitBlocksToBudget(List<KnowledgeMapBlock> blocks, List<string> warnings)
    {
        var maxInputTokens = _settings.MaxInputTokens;
        if (maxInputTokens <= 0)
        {
            warnings.Add("Knowledge Map budget is disabled because LocalLlmSettings.MaxInputTokens is not positive.");
            return blocks;
        }

        var targetTokens = Math.Max(1, (int)Math.Floor(maxInputTokens * Math.Clamp(_settings.TargetInputBudgetFillRatio, 0.1d, 1d)));
        var selected = blocks.OrderBy(block => block.Priority).ToList();
        while (selected.Count > 1 && EstimateBlocks(selected) > targetTokens)
        {
            var removable = selected.OrderByDescending(block => block.Priority).First();
            selected.Remove(removable);
            warnings.Add($"Knowledge Map omitted section '{removable.Title}' to fit token budget.");
        }

        if (EstimateBlocks(selected) > targetTokens)
        {
            var first = selected[0];
            selected[0] = first with { Text = TruncateByChars(first.Text, Math.Max(1200, targetTokens * 3)) };
            warnings.Add("Knowledge Map summary section was truncated to fit token budget.");
        }

        return selected.OrderBy(block => block.Priority).ToList();
    }

    private int EstimateBlocks(IReadOnlyList<KnowledgeMapBlock> blocks)
        => _tokenEstimator.EstimateTokens(string.Join(Environment.NewLine + Environment.NewLine, blocks.Select(block => block.Text)));

    private static string RenderPage(PageUnderstandingResult page)
        => $"- page {page.PageNumber}; confidence={FormatConfidence(page.Confidence)}; text={Excerpt(page.Text, PageTextExcerptChars)}";

    private static string RenderTextRegion(DocumentRegion region)
        => $"- page {region.PageNumber}; type={region.RegionType}; text={Excerpt(region.Text, RegionTextExcerptChars)}";

    private static string RenderTableRegion(DocumentRegion region)
    {
        var lines = region.Text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tags = region.ReviewTags?.Where(value => !string.IsNullOrWhiteSpace(value)).Take(8).ToList() ?? new List<string>();
        var confidence = region.LayoutConfidence ?? region.VisionConfidence;
        var caution = region.RegionType == DocumentRegionTypes.TableLowConfidence || region.NeedsReview
            ? "LOW CONFIDENCE: use raw OCR only; do not infer missing table cells, totals, or exact numbers."
            : "high-confidence simple table normalized from OCR; raw OCR retained.";
        return $@"- page {region.PageNumber}; type={region.RegionType}; confidence={FormatConfidence(confidence)}; needsReview={region.NeedsReview}; rowsOrLines={lines.Length}; tags={string.Join(", ", tags.DefaultIfEmpty("none"))}
  caution: {caution}
  tableText: {Excerpt(region.Text, RegionTextExcerptChars)}
  rawText: {Excerpt(region.RawText, RegionTextExcerptChars)}";
    }

    private static string RenderFormulaRegion(DocumentRegion region)
    {
        var tags = region.ReviewTags?.Where(value => !string.IsNullOrWhiteSpace(value)).Take(8).ToList() ?? new List<string>();
        return $@"- page {region.PageNumber}; confidence={FormatConfidence(region.LayoutConfidence)}; needsReview={region.NeedsReview}; tags={string.Join(", ", tags.DefaultIfEmpty("none"))}
  caution: REVIEW REQUIRED: do not repair notation, infer symbols, or use for exact calculation unless explicitly clear.
  rawFormulaText: {Excerpt(region.RawText ?? region.Text, RegionTextExcerptChars)}";
    }

    private static string RenderVisualRegion(DocumentRegion region)
    {
        var labels = region.ExtractedLabels?.Where(value => !string.IsNullOrWhiteSpace(value)).Take(8).ToList() ?? new List<string>();
        var relationships = region.Relationships?.Where(value => !string.IsNullOrWhiteSpace(value)).Take(8).ToList() ?? new List<string>();
        return $@"- page {region.PageNumber}; type={region.RegionType}; visionConfidence={FormatConfidence(region.VisionConfidence)}
  sourceText: {Excerpt(region.Text, RegionTextExcerptChars)}
  description: {Excerpt(region.Description, DescriptionExcerptChars)}
  labels: {string.Join(", ", labels.DefaultIfEmpty("none"))}
  relationships: {string.Join("; ", relationships.DefaultIfEmpty("none"))}
  uncertainty: {SafeLine(region.UncertaintyReason)}";
    }

    private static KnowledgeMapBlock Block(int priority, string text)
    {
        var title = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith('#'))?
            .TrimStart('#')
            .Trim() ?? $"section-{priority}";
        return new KnowledgeMapBlock(priority, title, text.Trim());
    }

    private static string BulletLines(IEnumerable<string> values)
        => string.Join(Environment.NewLine, values.Select(value => $"- {SafeLine(value)}"));

    private static string Excerpt(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        return normalized.Length <= maxChars
            ? normalized
            : normalized[..maxChars].TrimEnd() + "...";
    }

    private static string TruncateByChars(string value, int maxChars)
        => value.Length <= maxChars ? value : value[..maxChars].TrimEnd() + "...";

    private static string SafeLine(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : Regex.Replace(value, @"\s+", " ").Trim();

    private static string FormatConfidence(double? confidence)
        => confidence.HasValue ? confidence.Value.ToString("0.###") : "unknown";

    private static int GetRegionPriority(string regionType)
        => regionType switch
        {
            DocumentRegionTypes.Title => 0,
            DocumentRegionTypes.TableLikeText => 1,
            DocumentRegionTypes.TableLowConfidence => 2,
            DocumentRegionTypes.FormulaCandidate => 3,
            DocumentRegionTypes.DiagramCandidate => 4,
            DocumentRegionTypes.FigureCandidate => 5,
            DocumentRegionTypes.Text => 6,
            _ => 9
        };

    private static string NormalizeForLength(string value)
        => Regex.Replace(value, @"\s+", " ").Trim();

    private KnowledgeMapBuildResult Unusable(string reason, IEnumerable<string>? warnings = null)
        => new()
        {
            IsUsable = false,
            UnusableReason = reason,
            Warnings = warnings?.Where(warning => !string.IsNullOrWhiteSpace(warning)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>()
        };

    private List<string> ParseFailureReasons(string? failureReasonsJson)
    {
        if (string.IsNullOrWhiteSpace(failureReasonsJson))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(failureReasonsJson, _jsonOptions) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private sealed record KnowledgeMapBlock(int Priority, string Title, string Text);

    private sealed class UnderstandingRunPayload
    {
        public List<PageUnderstandingResult>? Pages { get; set; }
        public List<DocumentRegion>? Regions { get; set; }
        public List<string>? Warnings { get; set; }
        public DocumentQualityScoreResult? Quality { get; set; }
    }
}
