using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ELearnGamePlatform.API.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace ELearnGamePlatform.API.Services;

public class SlideImageService : ISlideImageService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private readonly HttpClient _httpClient;
    private readonly ISlideDeckRepository _slideDeckRepository;
    private readonly ImagePipelineSettings _settings;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SlideImageService> _logger;

    public SlideImageService(
        HttpClient httpClient,
        ISlideDeckRepository slideDeckRepository,
        IOptions<ImagePipelineSettings> settings,
        IWebHostEnvironment environment,
        ILogger<SlideImageService> logger)
    {
        _httpClient = httpClient;
        _slideDeckRepository = slideDeckRepository;
        _settings = settings.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task SourceImagesForItemAsync(SlideItem item, CancellationToken cancellationToken = default)
    {
        var imagePlan = item.GetImagePlan() ?? new SlideImagePlan();
        if (!ShouldSource(imagePlan))
        {
            UpdatePlanState(
                imagePlan,
                imagePlan.NeedsImage ? "not-requested" : "no-image-needed",
                imagePlan.NeedsImage
                    ? "Image pipeline dang tat hoac slide chua co search query."
                    : "Slide nay duoc giu text-only.");
            item.SetImagePlan(imagePlan);
            item.SetImageCandidates(new List<SlideImageCandidate>());
            item.SelectedImageKey = null;
            return;
        }

        try
        {
            var candidates = await SearchWikimediaCommonsAsync(item, imagePlan, cancellationToken);
            item.SetImageCandidates(candidates);
            item.SelectedImageKey = candidates.FirstOrDefault(candidate => candidate.IsSelected)?.Key;
            UpdatePlanState(
                imagePlan,
                candidates.Count > 0 ? "ready" : "no-license-safe-image",
                candidates.Count > 0
                    ? $"Da tim thay {candidates.Count} anh tu Wikimedia Commons."
                    : "Chua tim thay anh web an toan nguon cho slide nay.");
            item.SetImagePlan(imagePlan);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not source images for slide item {ItemId}", item.Id);
            item.SetImageCandidates(new List<SlideImageCandidate>());
            item.SelectedImageKey = null;
            UpdatePlanState(imagePlan, "failed", $"Tim anh that bai: {ex.Message}");
            item.SetImagePlan(imagePlan);
        }
    }

    public async Task<SlideItem?> RefreshImagesAsync(int deckId, int itemId, CancellationToken cancellationToken = default)
    {
        var item = await _slideDeckRepository.GetItemAsync(deckId, itemId);
        if (item == null)
        {
            return null;
        }

        await SourceImagesForItemAsync(item, cancellationToken);
        await _slideDeckRepository.UpdateItemAsync(item);
        return await _slideDeckRepository.GetItemAsync(deckId, itemId);
    }

    public async Task<SlideItem?> SelectImageAsync(int deckId, int itemId, string candidateKey, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var item = await _slideDeckRepository.GetItemAsync(deckId, itemId);
        if (item == null)
        {
            return null;
        }

        var candidates = item.GetImageCandidates();
        var selectedCandidate = candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, candidateKey, StringComparison.OrdinalIgnoreCase));
        if (selectedCandidate == null)
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            candidate.IsSelected = string.Equals(candidate.Key, selectedCandidate.Key, StringComparison.OrdinalIgnoreCase);
        }

        item.SetImageCandidates(candidates);
        item.SelectedImageKey = selectedCandidate.Key;

        var imagePlan = item.GetImagePlan() ?? new SlideImagePlan { NeedsImage = true };
        UpdatePlanState(imagePlan, "ready", "Da chon image candidate cho slide nay.");
        item.SetImagePlan(imagePlan);

        await _slideDeckRepository.UpdateItemAsync(item);
        return await _slideDeckRepository.GetItemAsync(deckId, itemId);
    }

    private bool ShouldSource(SlideImagePlan imagePlan)
    {
        return _settings.Enabled
            && _settings.WebSources.Enabled
            && imagePlan.NeedsImage
            && imagePlan.SearchQueries.Count > 0;
    }

    private async Task<List<SlideImageCandidate>> SearchWikimediaCommonsAsync(
        SlideItem item,
        SlideImagePlan imagePlan,
        CancellationToken cancellationToken)
    {
        const string wikimediaDomain = "commons.wikimedia.org";
        if (!_settings.WebSources.AllowedDomains.Any(domain =>
                string.Equals(domain, wikimediaDomain, StringComparison.OrdinalIgnoreCase)))
        {
            return new List<SlideImageCandidate>();
        }

        var candidates = new Dictionary<string, SlideImageCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var query in imagePlan.SearchQueries.Where(query => !string.IsNullOrWhiteSpace(query)).Take(3))
        {
            var payload = await FetchWikimediaResponseAsync(query, cancellationToken);
            foreach (var page in payload.Query?.Pages?.Values ?? Enumerable.Empty<WikimediaPage>())
            {
                SlideImageCandidate? candidate;
                try
                {
                    candidate = await BuildCandidateAsync(item, imagePlan, page, candidates.Count == 0, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Skipping Wikimedia page {PageId} for slide item {ItemId}", page.PageId, item.Id);
                    continue;
                }

                if (candidate == null || candidates.ContainsKey(candidate.Key))
                {
                    continue;
                }

                candidates[candidate.Key] = candidate;
                if (candidates.Count >= _settings.MaxCandidatesToPersist)
                {
                    return candidates.Values.ToList();
                }
            }
        }

        return candidates.Values.ToList();
    }

    private async Task<WikimediaSearchResponse> FetchWikimediaResponseAsync(string query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(_settings.WebSources.MaxResultsPerQuery, 1, 20);
        var url =
            $"https://commons.wikimedia.org/w/api.php?action=query&generator=search&gsrsearch={Uri.EscapeDataString(query)}" +
            $"&gsrnamespace=6&gsrlimit={limit}&prop=imageinfo|info&iiprop=url|extmetadata|size&iiurlwidth=1280&format=json&origin=*";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<WikimediaSearchResponse>(stream, cancellationToken: cancellationToken);
        return payload ?? new WikimediaSearchResponse();
    }

    private async Task<SlideImageCandidate?> BuildCandidateAsync(
        SlideItem item,
        SlideImagePlan imagePlan,
        WikimediaPage page,
        bool isFirst,
        CancellationToken cancellationToken)
    {
        var imageInfo = page.ImageInfo?.FirstOrDefault();
        if (imageInfo == null)
        {
            return null;
        }

        var assetUrl = NormalizeHttpUrl(imageInfo.ThumbUrl) ?? NormalizeHttpUrl(imageInfo.Url);
        if (assetUrl == null || !IsSafeImageUrl(assetUrl))
        {
            return null;
        }

        var extension = GetSafeImageExtension(assetUrl);
        if (extension == null)
        {
            return null;
        }

        var originUrl = NormalizeHttpUrl(page.FullUrl) ?? NormalizeHttpUrl(imageInfo.DescriptionUrl);
        if (originUrl == null || !IsAllowedOrigin(originUrl))
        {
            return null;
        }

        var localAssetUrl = _settings.DownloadAssetsLocally
            ? await DownloadAssetAsync(assetUrl, item, page.PageId, extension, cancellationToken)
            : assetUrl;

        var metadata = imageInfo.ExtMetadata ?? new WikimediaExtMetadata();
        return new SlideImageCandidate
        {
            Key = $"wikimedia-{page.PageId}",
            SourceType = "web",
            Provider = "Wikimedia Commons",
            OriginUrl = originUrl,
            LocalAssetUrl = localAssetUrl,
            ThumbnailUrl = NormalizeHttpUrl(imageInfo.ThumbUrl) ?? localAssetUrl,
            AltText = imagePlan.AltText ?? StripHtml(metadata.ImageDescription?.Value) ?? item.Heading,
            LicenseLabel = metadata.LicenseShortName?.Value ?? metadata.UsageTerms?.Value,
            AttributionText = BuildAttribution(metadata),
            Width = imageInfo.Width,
            Height = imageInfo.Height,
            Score = isFirst ? 1 : 0.8,
            IsSelected = isFirst,
            LayoutMode = imagePlan.VisualRole
        };
    }

    private async Task<string> DownloadAssetAsync(
        string assetUrl,
        SlideItem item,
        long pageId,
        string extension,
        CancellationToken cancellationToken)
    {
        var relativeFolder = Path.Combine($"deck-{item.SlideDeckId}", $"slide-{item.SlideIndex}");
        var absoluteFolder = Path.Combine(ResolveStorageRoot(), relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var fileName = $"wikimedia-{pageId}{extension}";
        var absolutePath = Path.Combine(absoluteFolder, fileName);
        if (!File.Exists(absolutePath))
        {
            using var response = await _httpClient.GetAsync(assetUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(absolutePath);
            await source.CopyToAsync(target, cancellationToken);
        }

        var normalizedRoot = _settings.AssetStorageRoot.Replace('\\', '/').Trim('/');
        return $"/{normalizedRoot}/{relativeFolder.Replace('\\', '/')}/{fileName}";
    }

    private string ResolveStorageRoot()
    {
        var configured = _settings.AssetStorageRoot.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(_environment.ContentRootPath, configured);
    }

    private bool IsAllowedOrigin(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return _settings.WebSources.AllowedDomains.Any(domain =>
            uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsSafeImageUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Host.EndsWith("wikimedia.org", StringComparison.OrdinalIgnoreCase)
            && GetSafeImageExtension(uri.AbsolutePath) != null;
    }

    private static string? NormalizeHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Scheme is "http" or "https" ? uri.ToString() : null;
    }

    private static string? GetSafeImageExtension(string pathOrUrl)
    {
        var cleanPath = pathOrUrl.Split('?', '#')[0];
        var extension = Path.GetExtension(cleanPath);
        return SupportedExtensions.Contains(extension) ? extension.ToLowerInvariant() : null;
    }

    private static string? BuildAttribution(WikimediaExtMetadata metadata)
    {
        var segments = new[]
        {
            StripHtml(metadata.Artist?.Value),
            StripHtml(metadata.Credit?.Value)
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        return segments.Count > 0 ? string.Join(" | ", segments) : null;
    }

    private static string? StripHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var withoutTags = Regex.Replace(WebUtility.HtmlDecode(value), "<.*?>", string.Empty);
        return string.IsNullOrWhiteSpace(withoutTags) ? null : withoutTags.Trim();
    }

    private static void UpdatePlanState(SlideImagePlan imagePlan, string statusHint, string message)
    {
        imagePlan.StatusHint = statusHint;
        imagePlan.LastResultMessage = message;
        imagePlan.LastAttemptedAtUtc = DateTime.UtcNow;
    }

    private sealed class WikimediaSearchResponse
    {
        [JsonPropertyName("query")]
        public WikimediaQuery? Query { get; set; }
    }

    private sealed class WikimediaQuery
    {
        [JsonPropertyName("pages")]
        public Dictionary<string, WikimediaPage> Pages { get; set; } = new();
    }

    private sealed class WikimediaPage
    {
        [JsonPropertyName("pageid")]
        public long PageId { get; set; }

        [JsonPropertyName("fullurl")]
        public string? FullUrl { get; set; }

        [JsonPropertyName("imageinfo")]
        public List<WikimediaImageInfo> ImageInfo { get; set; } = new();
    }

    private sealed class WikimediaImageInfo
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("thumburl")]
        public string? ThumbUrl { get; set; }

        [JsonPropertyName("descriptionurl")]
        public string? DescriptionUrl { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        [JsonPropertyName("extmetadata")]
        public WikimediaExtMetadata? ExtMetadata { get; set; }
    }

    private sealed class WikimediaExtMetadata
    {
        [JsonPropertyName("Artist")]
        public WikimediaMetadataValue? Artist { get; set; }

        [JsonPropertyName("Credit")]
        public WikimediaMetadataValue? Credit { get; set; }

        [JsonPropertyName("LicenseShortName")]
        public WikimediaMetadataValue? LicenseShortName { get; set; }

        [JsonPropertyName("UsageTerms")]
        public WikimediaMetadataValue? UsageTerms { get; set; }

        [JsonPropertyName("ImageDescription")]
        public WikimediaMetadataValue? ImageDescription { get; set; }
    }

    private sealed class WikimediaMetadataValue
    {
        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }
}
