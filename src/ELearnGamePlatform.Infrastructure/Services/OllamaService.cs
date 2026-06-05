using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Globalization;

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
        => (await GenerateResponseResultAsync(prompt, systemPrompt, profile)).Response;

    private async Task<OllamaGenerateResult> GenerateResponseResultAsync(
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
        => (await GenerateStructuredResponseWithMetadataAsync<T>(prompt, systemPrompt, profile)).Value;

    public async Task<StructuredGenerationResult<T>> GenerateStructuredResponseWithMetadataAsync<T>(
        string prompt,
        string? systemPrompt = null,
        OllamaModelProfile profile = OllamaModelProfile.Generation) where T : class
    {
        var jsonPrompt = BuildStrictJsonPrompt(prompt);
        var totalStopwatch = Stopwatch.StartNew();
        var response = await GenerateResponseResultAsync(jsonPrompt, systemPrompt, profile);
        var responseText = response.Response;
        
        _logger.LogDebug("Ollama raw response (first 500 chars): {Response}", 
            responseText.Length > 500 ? responseText.Substring(0, 500) + "..." : responseText);

        var rawValidation = TryDeserializeStructuredResponse<T>(responseText);
        if (rawValidation.Value != null)
        {
            _logger.LogInformation("Successfully parsed Ollama response to type {Type}", typeof(T).Name);
            totalStopwatch.Stop();
            return new StructuredGenerationResult<T>
            {
                Value = rawValidation.Value,
                Model = response.Model,
                RawOutputValid = true,
                ErrorType = AutoRepairJsonErrorType.None,
                ErrorMessage = string.Empty,
                AutoRepairTriggered = false,
                RepairSuccess = false,
                FinalOutputValid = true,
                ElapsedMs = totalStopwatch.ElapsedMilliseconds,
                RawOutputPreview = BuildPreview(responseText),
                RepairedOutputPreview = string.Empty
            };
        }

        _logger.LogWarning(
            "Failed to parse Ollama response as JSON. ErrorType={ErrorType} Error={ErrorMessage} Response={Response}",
            rawValidation.ErrorType,
            rawValidation.ErrorMessage,
            BuildPreview(responseText, 1000));

        if (TryApplyDeterministicJsonTextRepair<T>(responseText, out var textRepairText, out var textRepairValidation)
            && textRepairValidation.Value != null)
        {
            totalStopwatch.Stop();
            return new StructuredGenerationResult<T>
            {
                Value = textRepairValidation.Value,
                Model = response.Model,
                RawOutputValid = false,
                ErrorType = rawValidation.ErrorType,
                ErrorMessage = rawValidation.ErrorMessage,
                AutoRepairTriggered = true,
                RepairSuccess = true,
                FinalOutputValid = true,
                ElapsedMs = totalStopwatch.ElapsedMilliseconds,
                RawOutputPreview = BuildPreview(responseText),
                RepairedOutputPreview = BuildPreview(textRepairText)
            };
        }

        if (TryApplyDeterministicWrongTypeRepair<T>(responseText, out var deterministicRepairText, out var deterministicValidation)
            && deterministicValidation.Value != null)
        {
            totalStopwatch.Stop();
            return new StructuredGenerationResult<T>
            {
                Value = deterministicValidation.Value,
                Model = response.Model,
                RawOutputValid = false,
                ErrorType = rawValidation.ErrorType,
                ErrorMessage = rawValidation.ErrorMessage,
                AutoRepairTriggered = true,
                RepairSuccess = true,
                FinalOutputValid = true,
                ElapsedMs = totalStopwatch.ElapsedMilliseconds,
                RawOutputPreview = BuildPreview(responseText),
                RepairedOutputPreview = BuildPreview(deterministicRepairText)
            };
        }

        string repairedText = string.Empty;
        StructuredParseResult<T> repairValidation = StructuredParseResult<T>.Invalid(rawValidation.ErrorType, rawValidation.ErrorMessage);
        try
        {
            var repairPrompt = BuildJsonRepairPrompt(responseText, prompt, rawValidation);
            var repairResponse = await GenerateResponseResultAsync(
                repairPrompt,
                "You repair malformed JSON. Return only valid JSON that matches the requested shape.",
                profile);
            repairedText = repairResponse.Response;
            if (!string.IsNullOrWhiteSpace(repairResponse.Model))
            {
                response = repairResponse;
            }

            repairValidation = TryDeserializeStructuredResponse<T>(repairedText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JSON auto-repair request failed for structured Ollama response.");
            repairValidation = StructuredParseResult<T>.Invalid(AutoRepairJsonErrorType.ParseError, ex.Message);
        }

        totalStopwatch.Stop();
        return new StructuredGenerationResult<T>
        {
            Value = repairValidation.Value,
            Model = response.Model,
            RawOutputValid = false,
            ErrorType = rawValidation.ErrorType,
            ErrorMessage = rawValidation.ErrorMessage,
            AutoRepairTriggered = true,
            RepairSuccess = repairValidation.Value != null,
            FinalOutputValid = repairValidation.Value != null,
            ElapsedMs = totalStopwatch.ElapsedMilliseconds,
            RawOutputPreview = BuildPreview(responseText),
            RepairedOutputPreview = BuildPreview(repairedText)
        };
    }

    private async Task<OllamaGenerateResult> SendGenerateRequestAsync(
        string model,
        string prompt,
        string? systemPrompt,
        OllamaModelProfile profile)
    {
        var numCtx = ResolveContextTokens(profile);
        var request = new OllamaGenerateRequest
        {
            Model = model,
            Prompt = prompt,
            System = systemPrompt,
            Stream = false,
            KeepAlive = ResolveKeepAlive(),
            Options = new OllamaGenerateOptions
            {
                Temperature = ResolveTemperature(profile),
                NumCtx = numCtx
            }
        };

        _logger.LogInformation(
            "[OllamaCall] Profile={Profile} Model={Model} NumCtx={NumCtx} PromptChars={PromptChars} SystemChars={SystemChars}",
            FormatProfile(profile),
            model,
            numCtx?.ToString(CultureInfo.InvariantCulture) ?? "server-default",
            prompt.Length,
            systemPrompt?.Length ?? 0);

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
        return new OllamaGenerateResult(model, result.Response ?? string.Empty, startedAt.Elapsed);
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
            OllamaModelProfile.FastOutline when !string.IsNullOrWhiteSpace(_settings.GenerationModel) => _settings.GenerationModel!,
            OllamaModelProfile.FastSlide when !string.IsNullOrWhiteSpace(_settings.GenerationModel) => _settings.GenerationModel!,
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
            OllamaModelProfile.FastOutline when _settings.FastOutlineTemperature.HasValue => _settings.FastOutlineTemperature.Value,
            OllamaModelProfile.FastSlide when _settings.FastSlideTemperature.HasValue => _settings.FastSlideTemperature.Value,
            OllamaModelProfile.FastSlide when _settings.GenerationTemperature.HasValue => _settings.GenerationTemperature.Value,
            _ => _settings.Temperature
        };
    }

    private int? ResolveContextTokens(OllamaModelProfile profile)
    {
        var configured = profile switch
        {
            OllamaModelProfile.Analysis => _settings.AnalysisContextTokens,
            OllamaModelProfile.Generation => _settings.GenerationContextTokens,
            OllamaModelProfile.Verification => _settings.VerificationContextTokens,
            OllamaModelProfile.FastOutline => _settings.FastOutlineContextTokens,
            OllamaModelProfile.FastSlide => _settings.FastSlideContextTokens,
            _ => null
        };

        return configured.HasValue && configured.Value > 0
            ? configured.Value
            : null;
    }

    private static string FormatProfile(OllamaModelProfile profile)
        => profile switch
        {
            OllamaModelProfile.FastOutline => "fast-outline",
            OllamaModelProfile.FastSlide => "fast-slide",
            OllamaModelProfile.Analysis => "analysis",
            OllamaModelProfile.Generation => "generation",
            OllamaModelProfile.Verification => "verification",
            _ => profile.ToString()
        };

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

    private StructuredParseResult<T> TryDeserializeStructuredResponse<T>(string response) where T : class
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return StructuredParseResult<T>.Invalid(AutoRepairJsonErrorType.EmptyOutput, "Ollama returned an empty response.");
        }

        var jsonText = string.Empty;
        try
        {
            jsonText = ExtractJsonFromResponse(response);
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                return StructuredParseResult<T>.Invalid(AutoRepairJsonErrorType.EmptyOutput, "Ollama returned an empty JSON payload.");
            }

            var result = JsonSerializer.Deserialize<T>(jsonText, _jsonOptions);
            return result == null
                ? StructuredParseResult<T>.Invalid(AutoRepairJsonErrorType.SchemaMismatch, "JSON parsed but deserialized to null.")
                : StructuredParseResult<T>.Valid(result);
        }
        catch (JsonException ex)
        {
            return StructuredParseResult<T>.Invalid(
                ClassifyJsonException(ex),
                ex.Message,
                ex.Path ?? string.Empty,
                InferExpectedType(ex),
                ExtractInvalidSnippet(jsonText, ex.Path));
        }
    }

    private static AutoRepairJsonErrorType ClassifyJsonException(JsonException ex)
    {
        var message = ex.Message;
        if (message.Contains("required", StringComparison.OrdinalIgnoreCase))
        {
            return AutoRepairJsonErrorType.MissingField;
        }

        if (message.Contains("could not be converted", StringComparison.OrdinalIgnoreCase))
        {
            return AutoRepairJsonErrorType.WrongType;
        }

        if (message.Contains("The JSON value could not be converted", StringComparison.OrdinalIgnoreCase))
        {
            return AutoRepairJsonErrorType.WrongType;
        }

        return AutoRepairJsonErrorType.ParseError;
    }

    private bool TryApplyDeterministicJsonTextRepair<T>(
        string response,
        out string repairedText,
        out StructuredParseResult<T> validation) where T : class
    {
        repairedText = string.Empty;
        validation = StructuredParseResult<T>.Invalid(AutoRepairJsonErrorType.SchemaMismatch, "No deterministic JSON text repair was applied.");

        try
        {
            var jsonText = ExtractJsonFromResponse(response);
            if (!TryEscapeRawControlCharactersInJsonStrings(jsonText, out repairedText))
            {
                return false;
            }

            validation = TryDeserializeStructuredResponse<T>(repairedText);
            return true;
        }
        catch
        {
            repairedText = string.Empty;
            return false;
        }
    }

    private static bool TryEscapeRawControlCharactersInJsonStrings(string jsonText, out string repairedText)
    {
        var builder = new StringBuilder(jsonText.Length);
        var inString = false;
        var escaped = false;
        var changed = false;

        foreach (var current in jsonText)
        {
            if (inString)
            {
                if (escaped)
                {
                    builder.Append(current);
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    builder.Append(current);
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    builder.Append(current);
                    inString = false;
                    continue;
                }

                if (current == '\n')
                {
                    builder.Append("\\n");
                    changed = true;
                    continue;
                }

                if (current == '\r')
                {
                    builder.Append("\\r");
                    changed = true;
                    continue;
                }

                if (current == '\t')
                {
                    builder.Append("\\t");
                    changed = true;
                    continue;
                }

                if (char.IsControl(current))
                {
                    builder.Append("\\u");
                    builder.Append(((int)current).ToString("x4"));
                    changed = true;
                    continue;
                }
            }
            else if (current == '"')
            {
                inString = true;
            }

            builder.Append(current);
        }

        repairedText = changed ? builder.ToString() : string.Empty;
        return changed;
    }

    private bool TryApplyDeterministicWrongTypeRepair<T>(
        string response,
        out string repairedText,
        out StructuredParseResult<T> validation) where T : class
    {
        repairedText = string.Empty;
        validation = StructuredParseResult<T>.Invalid(AutoRepairJsonErrorType.SchemaMismatch, "No deterministic repair was applied.");

        try
        {
            var jsonText = ExtractJsonFromResponse(response);
            var node = JsonNode.Parse(jsonText);
            if (node == null)
            {
                return false;
            }

            var changed = RepairKnownWrongTypeShapes(node);
            if (!changed)
            {
                return false;
            }

            repairedText = node.ToJsonString(_jsonOptions);
            validation = TryDeserializeStructuredResponse<T>(repairedText);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool RepairKnownWrongTypeShapes(JsonNode node)
    {
        var changed = false;
        if (node is JsonObject obj)
        {
            var properties = obj.ToList();
            foreach (var property in properties)
            {
                var name = property.Key;
                var value = property.Value;
                if (value == null)
                {
                    continue;
                }

                if (IsStringArrayProperty(name))
                {
                    obj[name] = CoerceNodeToStringArray(value);
                    changed = true;
                    continue;
                }

                if (IsStringProperty(name) && value is not JsonValue)
                {
                    obj[name] = CoerceNodeToString(value);
                    changed = true;
                    continue;
                }

                changed |= RepairKnownWrongTypeShapes(value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child != null)
                {
                    changed |= RepairKnownWrongTypeShapes(child);
                }
            }
        }

        return changed;
    }

    private static bool IsStringArrayProperty(string propertyName)
        => string.Equals(propertyName, "bodyBlocks", StringComparison.OrdinalIgnoreCase)
            || string.Equals(propertyName, "bullets", StringComparison.OrdinalIgnoreCase);

    private static bool IsStringProperty(string propertyName)
        => propertyName.Equals("title", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("heading", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("subheading", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("goal", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("keyMessage", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("evidenceFromText", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("speakerNotes", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals("accentTone", StringComparison.OrdinalIgnoreCase);

    private static JsonArray CoerceNodeToStringArray(JsonNode node)
    {
        var array = new JsonArray();
        if (node is JsonArray source)
        {
            foreach (var item in source)
            {
                var text = item == null ? null : CoerceNodeToString(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    array.Add(text);
                }
            }

            return array;
        }

        var single = CoerceNodeToString(node);
        if (!string.IsNullOrWhiteSpace(single))
        {
            array.Add(single);
        }

        return array;
    }

    private static string CoerceNodeToString(JsonNode node)
    {
        if (node is JsonValue value)
        {
            return value.TryGetValue<string>(out var stringValue)
                ? stringValue ?? string.Empty
                : value.ToJsonString();
        }

        if (node is JsonArray array)
        {
            return string.Join("; ", array
                .Select(item => item == null ? null : CoerceNodeToString(item))
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        if (node is JsonObject obj)
        {
            foreach (var key in new[] { "text", "content", "value", "body", "title", "summary", "label" })
            {
                if (obj.TryGetPropertyValue(key, out var child) && child != null)
                {
                    var text = CoerceNodeToString(child);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            return string.Join("; ", obj
                .Select(property => property.Value == null ? null : CoerceNodeToString(property.Value))
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Take(4));
        }

        return node.ToJsonString();
    }

    private static string BuildJsonRepairPrompt<T>(string malformedOutput, string originalPrompt, StructuredParseResult<T> validation) where T : class
        => BuildJsonRepairPrompt(malformedOutput, originalPrompt, validation.ErrorPath, validation.ExpectedType, validation.InvalidSnippet);

    private static string BuildJsonRepairPrompt(string malformedOutput, string originalPrompt, string errorPath, string expectedType, string invalidSnippet)
    {
        var schemaHint = ExtractReturnJsonHint(originalPrompt);
        return $@"Repair the malformed AI output below into valid JSON.

Rules:
1. Return only one valid JSON object or JSON array.
2. Preserve the original facts and fields when possible.
3. Do not add explanation, markdown, comments, or code fences.
4. Use double quotes for all JSON keys and string values.
5. Remove trailing commas and fix broken escaping.
6. Keep Vietnamese text in Vietnamese. Do not translate content to English.

Validation error:
- path: {SanitizePromptLine(errorPath)}
- expected type: {SanitizePromptLine(expectedType)}
- invalid snippet: {SanitizePromptLine(invalidSnippet)}

Expected JSON shape:
{schemaHint}

Malformed AI output:
{malformedOutput}";
    }

    private static string ExtractReturnJsonHint(string prompt)
    {
        const int maxHintLength = 2500;
        var marker = "Return JSON only:";
        var index = prompt.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            marker = "Return JSON:";
            index = prompt.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        }
        var hint = index >= 0
            ? prompt[(index + marker.Length)..].Trim()
            : "Match the JSON object or array requested by the original task.";

        return hint.Length <= maxHintLength
            ? hint
            : hint[..maxHintLength];
    }

    private static string InferExpectedType(JsonException ex)
    {
        var message = ex.Message;
        var marker = "could not be converted to ";
        var index = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return "valid JSON matching the target schema";
        }

        var expected = message[(index + marker.Length)..];
        var period = expected.IndexOf('.', StringComparison.Ordinal);
        return period > 0 ? expected[..period] : expected;
    }

    private static string ExtractInvalidSnippet(string jsonText, string? path)
    {
        if (string.IsNullOrWhiteSpace(jsonText) || string.IsNullOrWhiteSpace(path))
        {
            return BuildPreview(jsonText);
        }

        try
        {
            using var document = JsonDocument.Parse(jsonText);
            var current = document.RootElement;
            foreach (var segment in ParseJsonPath(path))
            {
                if (segment.PropertyName != null)
                {
                    if (!current.TryGetProperty(segment.PropertyName, out current))
                    {
                        return string.Empty;
                    }
                }

                if (segment.ArrayIndex.HasValue)
                {
                    if (current.ValueKind != JsonValueKind.Array || current.GetArrayLength() <= segment.ArrayIndex.Value)
                    {
                        return string.Empty;
                    }

                    current = current[segment.ArrayIndex.Value];
                }
            }

            return BuildPreview(current.GetRawText(), 350);
        }
        catch
        {
            return BuildPreview(jsonText);
        }
    }

    private static IEnumerable<JsonPathSegment> ParseJsonPath(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.StartsWith("$.", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }
        else if (trimmed == "$")
        {
            yield break;
        }

        foreach (var rawPart in trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart;
            int? arrayIndex = null;
            var bracket = part.IndexOf('[', StringComparison.Ordinal);
            if (bracket >= 0)
            {
                var endBracket = part.IndexOf(']', bracket);
                if (endBracket > bracket && int.TryParse(part[(bracket + 1)..endBracket], out var parsedIndex))
                {
                    arrayIndex = parsedIndex;
                }

                part = part[..bracket];
            }

            yield return new JsonPathSegment(string.IsNullOrWhiteSpace(part) ? null : part, arrayIndex);
        }
    }

    private static string SanitizePromptLine(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : BuildPreview(value, 500);

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
        return normalized.Length <= limit
            ? normalized
            : normalized[..limit];
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

    private sealed record OllamaGenerateResult(string Model, string Response, TimeSpan Elapsed);

    private sealed class StructuredParseResult<T> where T : class
    {
        private StructuredParseResult(
            T? value,
            AutoRepairJsonErrorType errorType,
            string errorMessage,
            string errorPath,
            string expectedType,
            string invalidSnippet)
        {
            Value = value;
            ErrorType = errorType;
            ErrorMessage = errorMessage;
            ErrorPath = errorPath;
            ExpectedType = expectedType;
            InvalidSnippet = invalidSnippet;
        }

        public T? Value { get; }
        public AutoRepairJsonErrorType ErrorType { get; }
        public string ErrorMessage { get; }
        public string ErrorPath { get; }
        public string ExpectedType { get; }
        public string InvalidSnippet { get; }

        public static StructuredParseResult<T> Valid(T value)
            => new(value, AutoRepairJsonErrorType.None, string.Empty, string.Empty, string.Empty, string.Empty);

        public static StructuredParseResult<T> Invalid(AutoRepairJsonErrorType errorType, string errorMessage)
            => new(null, errorType, errorMessage, string.Empty, string.Empty, string.Empty);

        public static StructuredParseResult<T> Invalid(
            AutoRepairJsonErrorType errorType,
            string errorMessage,
            string errorPath,
            string expectedType,
            string invalidSnippet)
            => new(null, errorType, errorMessage, errorPath, expectedType, invalidSnippet);
    }

    private sealed record JsonPathSegment(string? PropertyName, int? ArrayIndex);

    private sealed class OllamaGenerateOptions
    {
        public double Temperature { get; init; }

        [JsonPropertyName("num_ctx")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? NumCtx { get; init; }
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
