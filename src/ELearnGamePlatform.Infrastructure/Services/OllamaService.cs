using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;

namespace ELearnGamePlatform.Infrastructure.Services;

public class OllamaService : IOllamaService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<OllamaService> _logger;

    public OllamaService(HttpClient httpClient, IOptions<OllamaSettings> settings, ILogger<OllamaService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task<string> GenerateResponseAsync(
        string prompt,
        string? systemPrompt = null,
        OllamaModelProfile profile = OllamaModelProfile.Generation)
    {
        var requestedModel = ResolveModel(profile);
        var defaultModel = ResolveModel(OllamaModelProfile.Generation);

        try
        {
            return await SendGenerateRequestAsync(requestedModel, prompt, systemPrompt, profile);
        }
        catch (Exception ex) when (
            profile != OllamaModelProfile.Generation &&
            !string.Equals(requestedModel, defaultModel, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                ex,
                "Could not use {Profile} model {RequestedModel}. Falling back to generation/default model {DefaultModel}.",
                profile,
                requestedModel,
                defaultModel);

            return await SendGenerateRequestAsync(defaultModel, prompt, systemPrompt, OllamaModelProfile.Generation);
        }
    }

    public async Task<T?> GenerateStructuredResponseAsync<T>(
        string prompt,
        string? systemPrompt = null,
        OllamaModelProfile profile = OllamaModelProfile.Generation) where T : class
    {
        var jsonPrompt = BuildStrictJsonPrompt(prompt);
        var responseText = await GenerateResponseAsync(jsonPrompt, systemPrompt, profile);
        
        _logger.LogDebug("Ollama raw response (first 500 chars): {Response}", 
            responseText.Length > 500 ? responseText.Substring(0, 500) + "..." : responseText);
        
        try
        {
            // Try to extract JSON from the response if it contains markdown code blocks
            var jsonText = ExtractJsonFromResponse(responseText);
            var result = JsonSerializer.Deserialize<T>(jsonText, _jsonOptions);
            
            if (result == null)
            {
                _logger.LogWarning("Ollama response was successfully parsed but resulted in null object");
            }
            else
            {
                _logger.LogInformation("Successfully parsed Ollama response to type {Type}", typeof(T).Name);
            }
            
            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Ollama response as JSON. Response: {Response}", 
                responseText.Length > 1000 ? responseText.Substring(0, 1000) + "..." : responseText);
            return null;
        }
    }

    private async Task<string> SendGenerateRequestAsync(
        string model,
        string prompt,
        string? systemPrompt,
        OllamaModelProfile profile)
    {
        var request = new OllamaGenerateRequest
        {
            Model = model,
            Prompt = prompt,
            System = systemPrompt,
            Stream = false,
            KeepAlive = ResolveKeepAlive(),
            Options = new OllamaGenerateOptions
            {
                Temperature = ResolveTemperature(profile)
            }
        };

        var startedAt = Stopwatch.StartNew();
        var response = await _httpClient.PostAsJsonAsync("/api/generate", request, _jsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Ollama request failed for model '{model}' with status {(int)response.StatusCode}: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(_jsonOptions);
        if (result == null)
        {
            throw new InvalidOperationException($"Ollama returned an empty response for model '{model}'.");
        }

        LogTiming(result, model, profile, prompt.Length, startedAt.Elapsed);
        return result?.Response ?? string.Empty;
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private string ResolveModel(OllamaModelProfile profile)
    {
        var fallback = string.IsNullOrWhiteSpace(_settings.GenerationModel)
            ? _settings.Model
            : _settings.GenerationModel;

        return profile switch
        {
            OllamaModelProfile.Analysis when !string.IsNullOrWhiteSpace(_settings.AnalysisModel) => _settings.AnalysisModel!,
            OllamaModelProfile.Verification when !string.IsNullOrWhiteSpace(_settings.VerificationModel) => _settings.VerificationModel!,
            OllamaModelProfile.Generation when !string.IsNullOrWhiteSpace(_settings.GenerationModel) => _settings.GenerationModel!,
            _ => fallback
        };
    }

    private double ResolveTemperature(OllamaModelProfile profile)
    {
        return profile switch
        {
            OllamaModelProfile.Analysis when _settings.AnalysisTemperature.HasValue => _settings.AnalysisTemperature.Value,
            OllamaModelProfile.Verification when _settings.VerificationTemperature.HasValue => _settings.VerificationTemperature.Value,
            OllamaModelProfile.Generation when _settings.GenerationTemperature.HasValue => _settings.GenerationTemperature.Value,
            _ => _settings.Temperature
        };
    }

    private string? ResolveKeepAlive()
    {
        return string.IsNullOrWhiteSpace(_settings.KeepAlive)
            ? null
            : _settings.KeepAlive.Trim();
    }

    private void LogTiming(
        OllamaResponse response,
        string model,
        OllamaModelProfile profile,
        int promptLength,
        TimeSpan wallClockElapsed)
    {
        if (!_settings.EnableTimingLogs)
        {
            return;
        }

        var totalMs = ToMilliseconds(response.TotalDuration) ?? wallClockElapsed.TotalMilliseconds;
        var loadMs = ToMilliseconds(response.LoadDuration);
        var promptEvalMs = ToMilliseconds(response.PromptEvalDuration);
        var evalMs = ToMilliseconds(response.EvalDuration);
        var keepAlive = ResolveKeepAlive() ?? "server-default";
        var responseLength = response.Response?.Length ?? 0;

        _logger.LogInformation(
            "Ollama {Profile} model {Model}: total={TotalMs}ms load={LoadMs}ms prompt_eval={PromptEvalMs}ms eval={EvalMs}ms prompt_tokens={PromptTokens} response_tokens={ResponseTokens} chars_in={PromptChars} chars_out={ResponseChars} keep_alive={KeepAlive}",
            profile,
            model,
            Math.Round(totalMs, 1),
            loadMs.HasValue ? Math.Round(loadMs.Value, 1) : null,
            promptEvalMs.HasValue ? Math.Round(promptEvalMs.Value, 1) : null,
            evalMs.HasValue ? Math.Round(evalMs.Value, 1) : null,
            response.PromptEvalCount,
            response.EvalCount,
            promptLength,
            responseLength,
            keepAlive);

        if (loadMs.HasValue && totalMs > 0 && loadMs.Value / totalMs >= 0.35d)
        {
            _logger.LogWarning(
                "Ollama model {Model} spent {LoadPercent}% of total time loading. keep_alive={KeepAlive} may need to be increased or the number of model switches reduced.",
                model,
                Math.Round(loadMs.Value / totalMs * 100d, 1),
                keepAlive);
        }
    }

    private static double? ToMilliseconds(long? durationNanoseconds)
    {
        return durationNanoseconds.HasValue
            ? durationNanoseconds.Value / 1_000_000d
            : null;
    }

    private string ExtractJsonFromResponse(string response)
    {
        var trimmed = response.Trim();
        if (trimmed.StartsWith("```json"))
        {
            trimmed = trimmed.Substring(7);
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed.Substring(3);
        }
        
        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 3);
        }

        trimmed = trimmed.Trim();

        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
        {
            return trimmed;
        }

        var objectStart = trimmed.IndexOf('{');
        var arrayStart = trimmed.IndexOf('[');

        var start = objectStart >= 0 && arrayStart >= 0
            ? Math.Min(objectStart, arrayStart)
            : Math.Max(objectStart, arrayStart);

        if (start < 0)
        {
            return trimmed;
        }

        var objectEnd = trimmed.LastIndexOf('}');
        var arrayEnd = trimmed.LastIndexOf(']');
        var end = Math.Max(objectEnd, arrayEnd);

        if (end > start)
        {
            return trimmed.Substring(start, end - start + 1).Trim();
        }

        return trimmed;
    }

    private static string BuildStrictJsonPrompt(string prompt)
    {
        return $@"{prompt}

Output rules (must follow exactly):
1. Return exactly one valid JSON object or JSON array.
2. Do not use markdown, code fences, comments, or extra explanation text.
3. Use double quotes for all JSON keys and string values.
4. Do not include trailing commas.
5. If a field has no data, return empty string or empty array instead of null when possible.";
    }

    private sealed class OllamaGenerateRequest
    {
        public required string Model { get; init; }
        public required string Prompt { get; init; }

        [JsonPropertyName("system")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? System { get; init; }

        public bool Stream { get; init; }

        [JsonPropertyName("keep_alive")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? KeepAlive { get; init; }

        public required OllamaGenerateOptions Options { get; init; }
    }

    private sealed class OllamaGenerateOptions
    {
        public double Temperature { get; init; }
    }

    private sealed class OllamaResponse
    {
        public string? Model { get; set; }
        public string? Response { get; set; }
        public bool Done { get; set; }

        [JsonPropertyName("total_duration")]
        public long? TotalDuration { get; set; }

        [JsonPropertyName("load_duration")]
        public long? LoadDuration { get; set; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; set; }

        [JsonPropertyName("prompt_eval_duration")]
        public long? PromptEvalDuration { get; set; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; set; }

        [JsonPropertyName("eval_duration")]
        public long? EvalDuration { get; set; }
    }
}
