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
    private readonly ISlideImagePlannerService _imagePlanner;
    private readonly ImagePipelineSettings _settings;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SlideImageService> _logger;
    private readonly ISlidePdfImageAssetService? _pdfImageAssetService;

    public SlideImageService(
        HttpClient httpClient,
        ISlideDeckRepository slideDeckRepository,
        ISlideImagePlannerService imagePlanner,
        IOptions<ImagePipelineSettings> settings,
        IWebHostEnvironment environment,
        ILogger<SlideImageService> logger,
        ISlidePdfImageAssetService? pdfImageAssetService = null)
    {
        _httpClient = httpClient;
        _slideDeckRepository = slideDeckRepository;
        _imagePlanner = imagePlanner;
        _settings = settings.Value;
        _environment = environment;
        _logger = logger;
        _pdfImageAssetService = pdfImageAssetService;
        EnsureHttpClientAllowsConfiguredGenerationTimeout();
    }

    public async Task SourceImagesForItemAsync(
        SlideItem item,
        SlideImageSourcingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= SlideImageSourcingOptions.Quality;

        if (!options.AllowImagePlanning)
        {
            ApplyFastPreviewImageSkip(item, options.SkipReason);
            return;
        }

        var imagePlan = await _imagePlanner.PlanAsync(item, item.SlideDeck?.Title, cancellationToken);
        item.SetImagePlan(imagePlan);

        if (!imagePlan.NeedsImage)
        {
            UpdatePlanState(
                imagePlan,
                string.IsNullOrWhiteSpace(imagePlan.StatusHint) ? "no-image-needed" : imagePlan.StatusHint,
                imagePlan.LastResultMessage ?? "Slide nay duoc giu text-only.");
            item.SetImagePlan(imagePlan);
            item.SetImageCandidates(new List<SlideImageCandidate>());
            item.SelectedImageKey = null;
            return;
        }

        var pdfRegionCandidate = options.AllowPdfRegionExtraction
            ? await TryCreatePdfRegionCandidateAsync(item, imagePlan, cancellationToken)
            : null;
        if (pdfRegionCandidate != null)
        {
            item.SetImageCandidates(new List<SlideImageCandidate> { pdfRegionCandidate });
            item.SelectedImageKey = pdfRegionCandidate.Key;
            UpdatePlanState(imagePlan, "ready", "Da trich xuat anh tu PDF goc cho slide nay.");
            item.SetImagePlan(imagePlan);
            return;
        }

        if (!options.AllowExternalImageGeneration || !CanGenerate(imagePlan))
        {
            UpdatePlanState(
                imagePlan,
                options.AllowExternalImageGeneration ? "not-requested" : "fast-mode-skipped",
                options.AllowExternalImageGeneration
                    ? "Image generation dang tat hoac generationPrompt chua hop le."
                    : "Fast Preview dang bo qua tao anh nang; co the refresh image khi can Quality mode.");
            item.SetImagePlan(imagePlan);
            item.SetImageCandidates(new List<SlideImageCandidate>());
            item.SelectedImageKey = null;
            return;
        }

        try
        {
            var generatedCandidate = await GenerateOpenAiCandidateAsync(item, imagePlan, "qwen-image-plan", cancellationToken);
            item.SetImageCandidates(new List<SlideImageCandidate> { generatedCandidate });
            item.SelectedImageKey = generatedCandidate.Key;
            UpdatePlanState(imagePlan, "ready", "Da tao anh minh hoa bang OpenAI theo image plan cua Qwen.");
            item.SetImagePlan(imagePlan);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not generate planned image for slide item {ItemId}", item.Id);
            item.SetImageCandidates(new List<SlideImageCandidate>());
            item.SelectedImageKey = null;
            UpdatePlanState(imagePlan, "failed", $"Tao anh that bai: {ex.Message}");
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

        await SourceImagesForItemAsync(item, cancellationToken: cancellationToken);
        await _slideDeckRepository.UpdateItemAsync(item);
        return await _slideDeckRepository.GetItemAsync(deckId, itemId);
    }

    private static void ApplyFastPreviewImageSkip(SlideItem item, string? reason)
    {
        var imagePlan = new SlideImagePlan
        {
            NeedsImage = false,
            Reason = "Fast Preview keeps the slide text-only and defers PDF-region extraction, vision render, and generated image sourcing.",
            VisualRole = "none",
            SourceEvidence = item.EvidenceFromText ?? item.KeyMessage,
            SearchQueries = new List<string>(),
            StatusHint = "fast-mode-skipped",
            LastResultMessage = string.Equals(reason, "fast-preview", StringComparison.OrdinalIgnoreCase)
                ? "Fast Preview skipped heavy image sourcing for this slide."
                : "Heavy image sourcing was skipped for this slide.",
            LastAttemptedAtUtc = DateTime.UtcNow
        };

        item.SetImagePlan(imagePlan);
        item.SetImageCandidates(new List<SlideImageCandidate>());
        item.SelectedImageKey = null;
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

    private async Task<SlideImageCandidate?> TryCreatePdfRegionCandidateAsync(
        SlideItem item,
        SlideImagePlan imagePlan,
        CancellationToken cancellationToken)
    {
        if (_pdfImageAssetService == null)
        {
            return null;
        }

        try
        {
            return await _pdfImageAssetService.TryCreateCandidateAsync(item, imagePlan, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not create PDF region image candidate for slide item {ItemId}", item.Id);
            return null;
        }
    }

    private bool CanGenerate(SlideImagePlan imagePlan)
        => _settings.Enabled
            && string.Equals(_settings.Generation.Provider, "openai", StringComparison.OrdinalIgnoreCase)
            && imagePlan.NeedsImage
            && SlideImagePlannerService.IsValidGenerationPrompt(imagePlan.GenerationPrompt);

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
        var apiKey = ResolveOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY is not configured.");
        }

        var prompt = BuildGenerationPrompt(item, imagePlan);
        var size = NormalizeGenerationSize(_settings.Generation.Size);
        var model = string.IsNullOrWhiteSpace(_settings.Generation.Model) ? "gpt-image-1" : _settings.Generation.Model;
        var isGptImageModel = IsGptImageModel(model);
        var request = new OpenAiImageGenerationRequest
        {
            Model = model,
            Prompt = prompt,
            Size = size,
            Quality = string.IsNullOrWhiteSpace(_settings.Generation.Quality) ? "high" : _settings.Generation.Quality,
            ResponseFormat = isGptImageModel ? null : "b64_json",
            OutputFormat = isGptImageModel ? "png" : null
        };

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, OpenAiImagesEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(GetGenerationTimeout());

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

    private string? ResolveOpenAiApiKey()
    {
        var environmentApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var apiKey = string.IsNullOrWhiteSpace(environmentApiKey)
            ? _settings.Generation.ApiKey
            : environmentApiKey;

        return string.IsNullOrWhiteSpace(apiKey)
            ? null
            : apiKey.Trim();
    }

    private void EnsureHttpClientAllowsConfiguredGenerationTimeout()
    {
        var requiredTimeout = GetGenerationTimeout().Add(TimeSpan.FromSeconds(15));
        if (_httpClient.Timeout == Timeout.InfiniteTimeSpan || _httpClient.Timeout >= requiredTimeout)
        {
            return;
        }

        _httpClient.Timeout = requiredTimeout;
    }

    private TimeSpan GetGenerationTimeout()
        => TimeSpan.FromSeconds(Math.Max(30, _settings.Generation.TimeoutSeconds));

    private static bool IsGptImageModel(string? model)
    {
        return !string.IsNullOrWhiteSpace(model)
            && model.Trim().StartsWith("gpt-image-", StringComparison.OrdinalIgnoreCase);
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
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ResponseFormat { get; set; }

        [JsonPropertyName("output_format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OutputFormat { get; set; }
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
