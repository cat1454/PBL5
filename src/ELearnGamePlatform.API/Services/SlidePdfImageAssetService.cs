using System.Text.Json;
using ELearnGamePlatform.API.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace ELearnGamePlatform.API.Services;

public sealed class SlidePdfImageAssetService : ISlidePdfImageAssetService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> PreferredRegionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "FigureCandidate",
        "DiagramCandidate",
        "ChartCandidate",
        "ProcessCandidate"
    };

    private readonly IDocumentUnderstandingRunRepository _understandingRuns;
    private readonly IVisionPageImageProvider _pageImageProvider;
    private readonly ImagePipelineSettings _settings;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SlidePdfImageAssetService> _logger;

    public SlidePdfImageAssetService(
        IDocumentUnderstandingRunRepository understandingRuns,
        IVisionPageImageProvider pageImageProvider,
        IOptions<ImagePipelineSettings> settings,
        IWebHostEnvironment environment,
        ILogger<SlidePdfImageAssetService> logger)
    {
        _understandingRuns = understandingRuns;
        _pageImageProvider = pageImageProvider;
        _settings = settings.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<SlideImageCandidate?> TryCreateCandidateAsync(
        SlideItem item,
        SlideImagePlan imagePlan,
        CancellationToken cancellationToken = default)
    {
        var document = item.SlideDeck?.Document;
        if (document == null || !IsPdf(document.FileType, document.FilePath) || !File.Exists(document.FilePath))
        {
            return null;
        }

        var run = await _understandingRuns.GetLatestByDocumentIdAsync(document.Id);
        var regions = ParseRegions(run?.ResultJson)
            .Select((region, index) => new CandidateRegion(region, index))
            .Where(candidate => IsUsableVisualRegion(candidate.Region))
            .OrderByDescending(candidate => Score(candidate.Region, imagePlan))
            .ThenBy(candidate => candidate.Region.PageNumber)
            .ThenBy(candidate => candidate.Index)
            .ToList();

        if (regions.Count == 0)
        {
            return null;
        }

        foreach (var candidateRegion in regions)
        {
            try
            {
                var cropped = await CropRegionAsync(document, item, candidateRegion, cancellationToken);
                if (cropped != null)
                {
                    cropped.LayoutMode = imagePlan.VisualRole;
                    cropped.AltText = FirstNonBlank(
                        imagePlan.AltText,
                        candidateRegion.Region.Description,
                        candidateRegion.Region.Text,
                        $"PDF region from page {candidateRegion.Region.PageNumber}");
                    return cropped;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Skipping PDF region candidate {RegionIndex} on page {PageNumber} for slide item {ItemId}",
                    candidateRegion.Index,
                    candidateRegion.Region.PageNumber,
                    item.Id);
            }
        }

        return null;
    }

    private async Task<SlideImageCandidate?> CropRegionAsync(
        Document document,
        SlideItem item,
        CandidateRegion candidateRegion,
        CancellationToken cancellationToken)
    {
        var region = candidateRegion.Region;
        await using var pageImage = await _pageImageProvider.GetPageImageAsync(
            document.FilePath,
            document.FileType,
            region.PageNumber,
            cancellationToken);
        if (pageImage == null || !File.Exists(pageImage.ImagePath))
        {
            return null;
        }

        using var image = await Image.LoadAsync(pageImage.ImagePath, cancellationToken);
        var crop = ResolveCropRectangle(region, image.Width, image.Height);
        if (crop.Width < 4 || crop.Height < 4)
        {
            return null;
        }

        image.Mutate(context => context.Crop(crop));

        var relativeFolder = Path.Combine($"deck-{item.SlideDeckId}", $"slide-{item.SlideIndex}");
        var absoluteFolder = Path.Combine(ResolveStorageRoot(), relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var fileName = $"pdf-region-{region.PageNumber}-{candidateRegion.Index + 1}.png";
        var absolutePath = Path.Combine(absoluteFolder, fileName);
        await image.SaveAsPngAsync(absolutePath, cancellationToken);

        var normalizedRoot = _settings.AssetStorageRoot.Replace('\\', '/').Trim('/');
        var localAssetUrl = $"/{normalizedRoot}/{relativeFolder.Replace('\\', '/')}/{fileName}";

        return new SlideImageCandidate
        {
            Key = $"pdf-region-{item.Id}-{region.PageNumber}-{candidateRegion.Index + 1}",
            SourceType = "pdf-region",
            Provider = "Source PDF",
            LocalAssetUrl = localAssetUrl,
            ThumbnailUrl = localAssetUrl,
            Width = crop.Width,
            Height = crop.Height,
            Score = Score(region, null),
            IsSelected = true,
            PageNumber = region.PageNumber,
            RegionType = region.RegionType,
            RegionText = Truncate(region.Text, 240)
        };
    }

    private static Rectangle ResolveCropRectangle(DocumentRegion region, int imageWidth, int imageHeight)
    {
        var x = Clamp01(region.NormalizedX ?? 0);
        var y = Clamp01(region.NormalizedY ?? 0);
        var width = Clamp01(region.NormalizedWidth ?? 0);
        var height = Clamp01(region.NormalizedHeight ?? 0);
        var paddingX = Math.Min(0.015, width * 0.08);
        var paddingY = Math.Min(0.015, height * 0.08);

        x = Math.Max(0, x - paddingX);
        y = Math.Max(0, y - paddingY);
        width = Math.Min(1 - x, width + paddingX * 2);
        height = Math.Min(1 - y, height + paddingY * 2);

        var left = Math.Clamp((int)Math.Floor(x * imageWidth), 0, imageWidth - 1);
        var top = Math.Clamp((int)Math.Floor(y * imageHeight), 0, imageHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling((x + width) * imageWidth), left + 1, imageWidth);
        var bottom = Math.Clamp((int)Math.Ceiling((y + height) * imageHeight), top + 1, imageHeight);
        return new Rectangle(left, top, right - left, bottom - top);
    }

    private static List<DocumentRegion> ParseRegions(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return new List<DocumentRegion>();
        }

        try
        {
            var payload = JsonSerializer.Deserialize<UnderstandingRunPayload>(resultJson, JsonOptions);
            return payload?.Regions ?? new List<DocumentRegion>();
        }
        catch
        {
            return new List<DocumentRegion>();
        }
    }

    private static bool IsUsableVisualRegion(DocumentRegion region)
        => region.PageNumber > 0
            && PreferredRegionTypes.Contains(region.RegionType)
            && region.NormalizedWidth is > 0.02 and <= 1
            && region.NormalizedHeight is > 0.02 and <= 1
            && region.NormalizedX is >= 0 and < 1
            && region.NormalizedY is >= 0 and < 1;

    private static double Score(DocumentRegion region, SlideImagePlan? imagePlan)
    {
        var score = region.VisionConfidence ?? region.LayoutConfidence ?? 0.7;
        if (!string.IsNullOrWhiteSpace(imagePlan?.VisualRole)
            && region.RegionType.Contains(imagePlan.VisualRole, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.2;
        }

        if (region.NeedsReview)
        {
            score -= 0.15;
        }

        return Math.Clamp(score, 0, 1);
    }

    private string ResolveStorageRoot()
    {
        var configured = _settings.AssetStorageRoot.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(_environment.ContentRootPath, configured);
    }

    private static bool IsPdf(string fileType, string filePath)
    {
        var extension = string.IsNullOrWhiteSpace(fileType) ? Path.GetExtension(filePath) : fileType.Trim();
        return (extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}")
            .Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }

    private sealed record CandidateRegion(DocumentRegion Region, int Index);

    private sealed class UnderstandingRunPayload
    {
        public List<DocumentRegion>? Regions { get; set; }
    }
}
