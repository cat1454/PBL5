using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using ELearnGamePlatform.Core.Options;
using ELearnGamePlatform.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ELearnGamePlatform.Infrastructure.Services;

public class OllamaVisionRegionDescriber : IVisionRegionDescriber
{
    private readonly HttpClient _httpClient;
    private readonly OllamaSettings _ollamaSettings;
    private readonly DocumentUnderstandingOptions _documentUnderstandingOptions;
    private readonly ILogger<OllamaVisionRegionDescriber> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public OllamaVisionRegionDescriber(
        HttpClient httpClient,
        IOptions<OllamaSettings> ollamaSettings,
        IOptions<DocumentUnderstandingOptions> documentUnderstandingOptions,
        ILogger<OllamaVisionRegionDescriber> logger)
    {
        _httpClient = httpClient;
        _ollamaSettings = ollamaSettings.Value;
        _documentUnderstandingOptions = documentUnderstandingOptions.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_ollamaSettings.BaseUrl);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<VisionRegionDescriptionResult> DescribeAsync(
        VisionRegionDescriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(request.Model)
            ? _documentUnderstandingOptions.VisionModel
            : request.Model.Trim();
        var timeoutSeconds = Math.Clamp(
            request.TimeoutSeconds > 0 ? request.TimeoutSeconds : _documentUnderstandingOptions.VisionTimeoutSeconds,
            5,
            600);

        if (string.IsNullOrWhiteSpace(model))
        {
            return VisionRegionDescriptionResult.Failed("Vision model is not configured.");
        }

        if (string.IsNullOrWhiteSpace(request.ImagePath) || !File.Exists(request.ImagePath))
        {
            return VisionRegionDescriptionResult.Failed("Vision image file was not found.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var imageBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(request.ImagePath, cancellationToken));
            var ollamaRequest = new OllamaVisionGenerateRequest
            {
                Model = model,
                Prompt = BuildVisionPrompt(request),
                Stream = false,
                Images = [imageBase64],
                KeepAlive = ResolveKeepAlive(),
                Options = new OllamaVisionGenerateOptions
                {
                    Temperature = 0.1d
                }
            };

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var response = await _httpClient.PostAsJsonAsync(
                "/api/generate",
                ollamaRequest,
                _jsonOptions,
                timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var reason = $"Ollama vision request failed with status {(int)response.StatusCode}.";
                _logger.LogWarning(
                    "Ollama vision model {Model} failed for page {PageNumber}, region {RegionType}: status={StatusCode}, elapsedMs={ElapsedMs}, body={Body}",
                    model,
                    request.PageNumber,
                    request.RegionType,
                    (int)response.StatusCode,
                    stopwatch.ElapsedMilliseconds,
                    BuildPreview(body));
                return VisionRegionDescriptionResult.Failed(reason);
            }

            var generateResponse = JsonSerializer.Deserialize<OllamaVisionGenerateResponse>(body, _jsonOptions);
            if (generateResponse == null)
            {
                _logger.LogWarning(
                    "Ollama vision model {Model} returned an empty generate payload for page {PageNumber}. elapsedMs={ElapsedMs}",
                    model,
                    request.PageNumber,
                    stopwatch.ElapsedMilliseconds);
                return VisionRegionDescriptionResult.Failed("Ollama vision returned an empty payload.");
            }

            var parsed = TryParseVisionJson(generateResponse.Response);
            if (parsed == null)
            {
                _logger.LogWarning(
                    "Ollama vision model {Model} returned malformed JSON for page {PageNumber}, region {RegionType}. elapsedMs={ElapsedMs}, response={Response}",
                    model,
                    request.PageNumber,
                    request.RegionType,
                    stopwatch.ElapsedMilliseconds,
                    BuildPreview(generateResponse.Response));
                return VisionRegionDescriptionResult.Failed("Ollama vision returned malformed JSON.");
            }

            _logger.LogInformation(
                "Ollama vision model {Model} described page {PageNumber}, region {RegionType} in {ElapsedMs}ms. confidence={Confidence}",
                model,
                request.PageNumber,
                request.RegionType,
                stopwatch.ElapsedMilliseconds,
                parsed.Confidence);

            return new VisionRegionDescriptionResult
            {
                Succeeded = true,
                Description = parsed.Description?.Trim() ?? string.Empty,
                ExtractedLabels = CleanList(parsed.ExtractedLabels),
                Relationships = CleanList(parsed.Relationships),
                Confidence = parsed.Confidence,
                UncertaintyReason = parsed.UncertaintyReason?.Trim() ?? string.Empty
            };
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "Ollama vision model {Model} timed out after {TimeoutSeconds}s for page {PageNumber}, region {RegionType}. elapsedMs={ElapsedMs}",
                model,
                timeoutSeconds,
                request.PageNumber,
                request.RegionType,
                stopwatch.ElapsedMilliseconds);
            return VisionRegionDescriptionResult.Failed("Ollama vision request timed out.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "Ollama vision model {Model} failed for page {PageNumber}, region {RegionType}. elapsedMs={ElapsedMs}",
                model,
                request.PageNumber,
                request.RegionType,
                stopwatch.ElapsedMilliseconds);
            return VisionRegionDescriptionResult.Failed(ex.Message);
        }
    }

    private static string BuildVisionPrompt(VisionRegionDescriptionRequest request)
        => $@"Bạn là hệ thống phân tích tài liệu học tập. Hãy mô tả hình/sơ đồ này bằng tiếng Việt. Trích xuất chữ, thành phần, mũi tên, quan hệ, luồng xử lý, ý nghĩa giáo dục. Nếu không đủ rõ, trả về uncertain và lý do.

Thông tin vùng:
- Trang: {request.PageNumber}
- Loại vùng: {request.RegionType}
- Văn bản OCR/layout liên quan: {SanitizePromptText(request.RegionText)}
- Ngữ cảnh tài liệu: {SanitizePromptText(request.PromptContext)}

Return JSON only:
{{
  ""description"": ""Mô tả tiếng Việt hoặc 'uncertain' nếu không đủ rõ"",
  ""extractedLabels"": [""nhãn/chữ nhìn thấy trong hình""],
  ""relationships"": [""quan hệ, mũi tên, luồng xử lý hoặc liên kết thành phần""],
  ""confidence"": 0.0,
  ""uncertaintyReason"": ""lý do nếu uncertain hoặc độ tin cậy thấp""
}}";

    private VisionJsonResponse? TryParseVisionJson(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        try
        {
            var json = ExtractJson(response);
            return JsonSerializer.Deserialize<VisionJsonResponse>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractJson(string response)
    {
        var trimmed = response.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..];
        }
        else if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed[3..];
        }

        if (trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^3];
        }

        trimmed = trimmed.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start
            ? trimmed[start..(end + 1)]
            : trimmed;
    }

    private static List<string> CleanList(IEnumerable<string>? values)
        => values?
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList()
            ?? new List<string>();

    private string? ResolveKeepAlive()
        => string.IsNullOrWhiteSpace(_ollamaSettings.KeepAlive)
            ? null
            : _ollamaSettings.KeepAlive.Trim();

    private static string SanitizePromptText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "không có";
        }

        var normalized = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= 1200 ? normalized : normalized[..1200];
    }

    private static string BuildPreview(string? value, int limit = 500)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= limit ? normalized : normalized[..limit];
    }

    private sealed class OllamaVisionGenerateRequest
    {
        public required string Model { get; init; }
        public required string Prompt { get; init; }
        public bool Stream { get; init; }
        public required IReadOnlyList<string> Images { get; init; }

        [JsonPropertyName("keep_alive")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? KeepAlive { get; init; }

        public required OllamaVisionGenerateOptions Options { get; init; }
    }

    private sealed class OllamaVisionGenerateOptions
    {
        public double Temperature { get; init; }
    }

    private sealed class OllamaVisionGenerateResponse
    {
        public string? Response { get; set; }
    }

    private sealed class VisionJsonResponse
    {
        public string? Description { get; set; }
        public List<string>? ExtractedLabels { get; set; }
        public List<string>? Relationships { get; set; }
        public double? Confidence { get; set; }
        public string? UncertaintyReason { get; set; }
    }
}
