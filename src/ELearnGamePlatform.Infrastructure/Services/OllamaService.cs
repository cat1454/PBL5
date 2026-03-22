using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

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
            return await SendGenerateRequestAsync(requestedModel, prompt, systemPrompt);
        }
        catch (Exception ex) when (
            profile == OllamaModelProfile.Analysis &&
            !string.Equals(requestedModel, defaultModel, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                ex,
                "Could not use analysis model {AnalysisModel}. Falling back to generation/default model {DefaultModel}.",
                requestedModel,
                defaultModel);

            return await SendGenerateRequestAsync(defaultModel, prompt, systemPrompt);
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

    private async Task<string> SendGenerateRequestAsync(string model, string prompt, string? systemPrompt)
    {
        var request = new
        {
            model,
            prompt = prompt,
            system = systemPrompt,
            stream = false,
            options = new
            {
                temperature = _settings.Temperature
            }
        };

        var response = await _httpClient.PostAsJsonAsync("/api/generate", request, _jsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Ollama request failed for model '{model}' with status {(int)response.StatusCode}: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();
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
            OllamaModelProfile.Generation when !string.IsNullOrWhiteSpace(_settings.GenerationModel) => _settings.GenerationModel!,
            _ => fallback
        };
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

    private class OllamaResponse
    {
        public string? Model { get; set; }
        public string? Response { get; set; }
        public bool Done { get; set; }
    }
}
