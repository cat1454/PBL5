using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
    private const string OpenAiImagesEndpoint = "https://api.openai.com/v1/images/generations";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
                    ? "Image pipeline dang tat hoac slide chua du du lieu de tao anh."
                    : "Slide nay duoc giu text-only.");
            item.SetImagePlan(imagePlan);
            item.SetImageCandidates(new List<SlideImageCandidate>());
            item.SelectedImageKey = null;
            return;
        }

        try
        {
            var candidates = await SearchWikimediaCommonsAsync(item, imagePlan, cancellationToken);
            var fallbackReason = ResolveFallbackReason(candidates, imagePlan);

            if (fallbackReason == null)
            {
                item.SetImageCandidates(candidates);
                item.SelectedImageKey = candidates.FirstOrDefault(candidate => candidate.IsSelected)?.Key;
                UpdatePlanState(
                    imagePlan,
                    candidates.Count > 0 ? "ready" : "no-license-safe-image",
                    candidates.Count > 0
                        ? $"Da tim thay {candidates.Count} anh tu Wikimedia Commons."
                        : "Chua tim thay anh web an toan nguon cho slide nay.");
                item.SetImagePlan(imagePlan);
                return;
            }

            try
            {
                var generatedCandidate = await GenerateOpenAiCandidateAsync(item, imagePlan, fallbackReason, cancellationToken);
                candidates = MergeCandidatesWithGenerated(candidates, generatedCandidate);
                item.SetImageCandidates(candidates);
                item.SelectedImageKey = generatedCandidate.Key;
                UpdatePlanState(
                    imagePlan,
                    "ready",
                    BuildFallbackSuccessMessage(candidates.Count, fallbackReason));
                item.SetImagePlan(imagePlan);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not generate fallback image for slide item {ItemId}", item.Id);

                if (candidates.Count > 0)
                {
                    item.SetImageCandidates(candidates);
                    item.SelectedImageKey = candidates.FirstOrDefault(candidate => candidate.IsSelected)?.Key;
                    UpdatePlanState(
                        imagePlan,
                        "ready",
                        $"OpenAI fallback khong thanh cong ({fallbackReason}); giu lai {candidates.Count} candidate tu Wikimedia Commons.");
                    item.SetImagePlan(imagePlan);
                    return;
                }

                item.SetImageCandidates(new List<SlideImageCandidate>());
                item.SelectedImageKey = null;
                UpdatePlanState(
                    imagePlan,
                    "failed",
                    $"Khong the tao anh cho slide nay sau khi Wikimedia va OpenAI deu that bai ({fallbackReason}).");
                item.SetImagePlan(imagePlan);
            }
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
            && imagePlan.NeedsImage
            && (_settings.WebSources.Enabled || CanAttemptGeneration(imagePlan));
    }

    private async Task<List<SlideImageCandidate>> SearchWikimediaCommonsAsync(
        SlideItem item,
        SlideImagePlan imagePlan,
        CancellationToken cancellationToken)
    {
        if (!_settings.WebSources.Enabled || imagePlan.SearchQueries.Count == 0)
        {
            return new List<SlideImageCandidate>();
        }

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

    private string? ResolveFallbackReason(IReadOnlyCollection<SlideImageCandidate> candidates, SlideImagePlan imagePlan)
    {
        if (!CanAttemptGeneration(imagePlan))
        {
            return null;
        }

        if (candidates.Count == 0)
        {
            return "no-web-candidate";
        }

        if (RequiresConceptualIllustration(imagePlan.VisualRole))
        {
            return "conceptual-visual-role";
        }

        var bestScore = candidates
            .Where(candidate => string.Equals(candidate.SourceType, "web", StringComparison.OrdinalIgnoreCase))
            .Max(candidate => candidate.Score ?? 0d);

        return bestScore < _settings.WebSources.MinAcceptableScore
            ? "low-web-relevance"
            : null;
    }

    private bool CanAttemptGeneration(SlideImagePlan imagePlan)
    {
        return string.Equals(_settings.Generation.Provider, "openai", StringComparison.OrdinalIgnoreCase)
            && imagePlan.NeedsImage
            && (!string.IsNullOrWhiteSpace(imagePlan.GenerationPrompt)
                || !string.IsNullOrWhiteSpace(itemHeadingOrAlt(imagePlan)));
    }

    private static string? itemHeadingOrAlt(SlideImagePlan imagePlan)
    {
        return imagePlan.AltText ?? imagePlan.RedactedPrompt;
    }

    private static bool RequiresConceptualIllustration(string? visualRole)
    {
        return string.Equals(visualRole, "conceptual", StringComparison.OrdinalIgnoreCase)
            || string.Equals(visualRole, "illustration", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SlideImageCandidate> GenerateOpenAiCandidateAsync(
        SlideItem item,
        SlideImagePlan imagePlan,
        string fallbackReason,
        CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY is not configured.");
        }

        var prompt = BuildGenerationPrompt(item, imagePlan);
        var size = NormalizeGenerationSize(_settings.Generation.Size);
        var request = new OpenAiImageGenerationRequest
        {
            Model = string.IsNullOrWhiteSpace(_settings.Generation.Model) ? "gpt-image-1" : _settings.Generation.Model,
            Prompt = prompt,
            Size = size,
            Quality = string.IsNullOrWhiteSpace(_settings.Generation.Quality) ? "high" : _settings.Generation.Quality,
            ResponseFormat = "b64_json",
            OutputFormat = "png"
        };

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, OpenAiImagesEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, _settings.Generation.TimeoutSeconds)));

        using var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "OpenAI image generation returned status {StatusCode} for slide item {ItemId}. Body: {Body}",
                (int)response.StatusCode,
                item.Id,
                TruncateForLog(responseBody, 600));
            throw new InvalidOperationException($"OpenAI image generation failed with status {(int)response.StatusCode}.");
        }

        var payload = JsonSerializer.Deserialize<OpenAiImageGenerationResponse>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("OpenAI image generation returned an empty payload.");
        var imagePayload = payload.Data?.FirstOrDefault()
            ?? throw new InvalidOperationException("OpenAI image generation did not return image data.");

        byte[] imageBytes;
        if (!string.IsNullOrWhiteSpace(imagePayload.B64Json))
        {
            imageBytes = Convert.FromBase64String(imagePayload.B64Json);
        }
        else if (!string.IsNullOrWhiteSpace(imagePayload.Url))
        {
            using var imageResponse = await _httpClient.GetAsync(imagePayload.Url, timeoutCts.Token);
            imageResponse.EnsureSuccessStatusCode();
            imageBytes = await imageResponse.Content.ReadAsByteArrayAsync(timeoutCts.Token);
        }
        else
        {
            throw new InvalidOperationException("OpenAI image generation did not return a usable image.");
        }

        var localAssetUrl = await SaveGeneratedAssetAsync(imageBytes, item, cancellationToken);
        var (width, height) = ParseDimensions(size);
        return new SlideImageCandidate
        {
            Key = $"generated-{item.Id}",
            SourceType = "generated",
            Provider = "OpenAI",
            LocalAssetUrl = localAssetUrl,
            ThumbnailUrl = localAssetUrl,
            AltText = imagePlan.AltText ?? BuildGeneratedAltText(item, imagePlan),
            Width = width,
            Height = height,
            Score = 1,
            IsSelected = true,
            LayoutMode = imagePlan.VisualRole
        };
    }

    private static List<SlideImageCandidate> MergeCandidatesWithGenerated(
        IReadOnlyCollection<SlideImageCandidate> existingCandidates,
        SlideImageCandidate generatedCandidate)
    {
        var merged = existingCandidates
            .Where(candidate => !string.Equals(candidate.Key, generatedCandidate.Key, StringComparison.OrdinalIgnoreCase))
            .Select(candidate =>
            {
                candidate.IsSelected = false;
                return candidate;
            })
            .ToList();

        merged.Add(generatedCandidate);
        return merged;
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

    private async Task<string> SaveGeneratedAssetAsync(
        byte[] imageBytes,
        SlideItem item,
        CancellationToken cancellationToken)
    {
        var relativeFolder = Path.Combine($"deck-{item.SlideDeckId}", $"slide-{item.SlideIndex}");
        var absoluteFolder = Path.Combine(ResolveStorageRoot(), relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var fileName = $"generated-{item.Id}.png";
        var absolutePath = Path.Combine(absoluteFolder, fileName);
        await File.WriteAllBytesAsync(absolutePath, imageBytes, cancellationToken);

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

    private string BuildGenerationPrompt(SlideItem item, SlideImagePlan imagePlan)
    {
        var heading = SanitizePromptField(item.Heading, 180) ?? $"Slide {item.SlideIndex}";
        var keyMessage = SanitizePromptField(item.KeyMessage, 220) ?? SanitizePromptField(item.Goal, 220) ?? "Support the main idea clearly.";
        var goal = SanitizePromptField(item.Goal, 220) ?? keyMessage;
        var visualRole = string.IsNullOrWhiteSpace(imagePlan.VisualRole) ? "supporting" : imagePlan.VisualRole.Trim();
        var bodySummary = SummarizeBodyBlocks(item.GetBodyBlocks());

        var promptParts = new List<string>
        {
            "Create a presentation slide illustration designed for a 16:9 media frame.",
            $"Slide heading: {heading}.",
            $"Key message: {keyMessage}.",
            $"Goal: {goal}.",
            $"Visual role: {visualRole}.",
            $"Body blocks summary: {bodySummary}.",
            "Style guidance: presentation slide illustration, safe margins, central composition.",
            "Constraints: no text, no logo, no watermark."
        };

        if (!string.IsNullOrWhiteSpace(imagePlan.GenerationPrompt))
        {
            promptParts.Add($"Additional guidance: {SanitizePromptField(imagePlan.GenerationPrompt, 320)}.");
        }

        return string.Join(" ", promptParts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string SummarizeBodyBlocks(IReadOnlyCollection<string> bodyBlocks)
    {
        var segments = bodyBlocks
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .Select(block => SanitizePromptField(block, 140))
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .Take(4)
            .ToList();

        return segments.Count == 0
            ? "Use a clean conceptual scene that reinforces the slide topic."
            : string.Join(" | ", segments);
    }

    private static string? SanitizePromptField(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(" ", value
            .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            .Trim();

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].Trim();
    }

    private static string NormalizeGenerationSize(string? configuredSize)
    {
        if (string.IsNullOrWhiteSpace(configuredSize))
        {
            return "1536x1024";
        }

        var match = Regex.Match(configuredSize.Trim(), @"^(?<width>\d{3,5})x(?<height>\d{3,5})$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return "1536x1024";
        }

        return $"{match.Groups["width"].Value}x{match.Groups["height"].Value}";
    }

    private static (int? Width, int? Height) ParseDimensions(string size)
    {
        var parts = size.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var width)
            || !int.TryParse(parts[1], out var height))
        {
            return (null, null);
        }

        return (width, height);
    }

    private static string BuildGeneratedAltText(SlideItem item, SlideImagePlan imagePlan)
    {
        var visualRole = string.IsNullOrWhiteSpace(imagePlan.VisualRole) ? "supporting" : imagePlan.VisualRole.Trim();
        return $"Generated slide illustration for slide {item.SlideIndex}: {item.Heading ?? "Untitled"} ({visualRole}).";
    }

    private static string BuildFallbackSuccessMessage(int candidateCount, string fallbackReason)
    {
        return fallbackReason switch
        {
            "no-web-candidate" => "Khong tim thay anh web phu hop, da tao anh moi bang OpenAI.",
            "low-web-relevance" => $"Anh Wikimedia chua du hop ngu canh, da tao anh moi bang OpenAI va giu lai {candidateCount - 1} candidate web.",
            "conceptual-visual-role" => "Slide yeu cau minh hoa khai niem, da uu tien tao anh moi bang OpenAI.",
            _ => "Da tao anh moi bang OpenAI cho slide nay."
        };
    }

    private static string TruncateForLog(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value[..maxLength];
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

    private sealed class OpenAiImageGenerationRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public string Size { get; set; } = "1536x1024";

        [JsonPropertyName("quality")]
        public string Quality { get; set; } = "high";

        [JsonPropertyName("response_format")]
        public string ResponseFormat { get; set; } = "b64_json";

        [JsonPropertyName("output_format")]
        public string OutputFormat { get; set; } = "png";
    }

    private sealed class OpenAiImageGenerationResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAiImageGenerationData>? Data { get; set; }
    }

    private sealed class OpenAiImageGenerationData
    {
        [JsonPropertyName("b64_json")]
        public string? B64Json { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
