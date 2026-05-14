using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ELearnGamePlatform.API.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace ELearnGamePlatform.API.Services;

public class SlideImagePlannerService : ISlideImagePlannerService
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "conceptual",
        "process",
        "diagram",
        "object",
        "background",
        "none"
    };

    private readonly IOllamaService _ollamaService;
    private readonly ImagePipelineSettings _settings;
    private readonly ILogger<SlideImagePlannerService> _logger;

    public SlideImagePlannerService(
        IOllamaService ollamaService,
        IOptions<ImagePipelineSettings> settings,
        ILogger<SlideImagePlannerService> logger)
    {
        _ollamaService = ollamaService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SlideImagePlan> PlanAsync(SlideItem item, string? documentTopic = null, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        try
        {
            var response = await _ollamaService.GenerateStructuredResponseAsync<ImagePlanningResponse>(
                BuildPlanningPrompt(item, documentTopic),
                "You are an image planning assistant for academic presentation slides. Return JSON only.",
                OllamaModelProfile.Generation);

            var plan = NormalizePlan(response, item);
            if (!plan.NeedsImage)
            {
                return plan;
            }

            if (IsValidGenerationPrompt(plan.GenerationPrompt))
            {
                return plan;
            }

            var repair = await _ollamaService.GenerateStructuredResponseAsync<ImagePlanningResponse>(
                BuildRepairPrompt(item, documentTopic, plan),
                "You repair image-planning JSON for academic presentation slides. Return JSON only.",
                OllamaModelProfile.Generation);

            var repairedPlan = NormalizePlan(repair, item);
            return repairedPlan.NeedsImage && IsValidGenerationPrompt(repairedPlan.GenerationPrompt)
                ? repairedPlan
                : BuildInvalidPlan(item);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qwen image planning failed for slide item {ItemId}", item.Id);
            return BuildInvalidPlan(item);
        }
    }

    private string BuildPlanningPrompt(SlideItem item, string? documentTopic)
    {
        var body = string.Join("\n", item.GetBodyBlocks().Take(8));
        var topic = FirstNonEmpty(documentTopic, item.SlideDeck?.Title, item.Heading, "Academic document");

        return $$"""
You are an image planning assistant for academic presentation slides.

Decide whether this slide needs an image.

Return JSON only:
{
  "needsImage": true/false,
  "reason": "...",
  "visualRole": "conceptual | process | diagram | object | background | none",
  "generationPrompt": "... or null",
  "negativePrompt": "... or null",
  "altText": "... or null",
  "searchQueries": []
}

Decision rules:
- Use needsImage = true only when an image clearly improves understanding, engagement, or visual explanation.
- Use needsImage = false for table-of-contents slides, dense bullet slides, short definition slides, conclusion slides, and slides that are already clear with text.
- For conceptual/process/system/workflow slides, prefer a generated image prompt.
- Do not create search queries unless the slide is about a real person, place, object, species, or historical entity.
- If generating an image, the prompt must describe a 16:9 academic presentation illustration.
- Style: clean modern education technology, white background, navy and purple accents, rounded cards, soft shadows.
- Do not request readable text inside the image.
- No logos, no watermark, no fake UI paragraphs.

Slide title:
{{Limit(item.Heading, 220)}}

Slide goal:
{{Limit(item.Goal, 260)}}

Key message:
{{Limit(item.KeyMessage, 260)}}

Body:
{{Limit(body, _settings.Planning.MaxPromptChars)}}

Document topic:
{{Limit(topic, 260)}}

Slide index / type:
{{item.SlideIndex}} / {{item.SlideType}}
""";
    }

    private string BuildRepairPrompt(SlideItem item, string? documentTopic, SlideImagePlan invalidPlan)
    {
        return $$"""
Repair this image plan for slide {{item.SlideIndex}}.

The previous generationPrompt was invalid:
{{Limit(invalidPlan.GenerationPrompt, 600)}}

Return JSON only. If the slide truly needs an image, provide a valid generationPrompt that:
- is not empty
- mentions 16:9 or presentation slide
- describes the visual subject and layout
- does not request readable text
- does not include logos or watermark

If you cannot produce a valid prompt, return needsImage=false.

Slide title: {{Limit(item.Heading, 220)}}
Slide goal: {{Limit(item.Goal, 260)}}
Key message: {{Limit(item.KeyMessage, 260)}}
Body: {{Limit(string.Join(" | ", item.GetBodyBlocks().Take(8)), _settings.Planning.MaxPromptChars)}}
Document topic: {{Limit(FirstNonEmpty(documentTopic, item.SlideDeck?.Title, item.Heading, "Academic document"), 260)}}
""";
    }

    private static SlideImagePlan NormalizePlan(ImagePlanningResponse? response, SlideItem item)
    {
        if (response == null)
        {
            return BuildInvalidPlan(item);
        }

        var needsImage = response.NeedsImage;
        var role = NormalizeRole(response.VisualRole, needsImage);
        var reason = NormalizeText(response.Reason, 400)
            ?? (needsImage
                ? "Qwen determined that this slide benefits from a generated visual."
                : "Qwen determined that this slide should remain text-only.");

        if (!needsImage)
        {
            return new SlideImagePlan
            {
                NeedsImage = false,
                Reason = reason,
                VisualRole = "none",
                GenerationPrompt = null,
                NegativePrompt = null,
                AltText = null,
                SearchQueries = new List<string>(),
                StatusHint = "no-image-needed",
                LastResultMessage = reason
            };
        }

        return new SlideImagePlan
        {
            NeedsImage = true,
            Reason = reason,
            VisualRole = role,
            GenerationPrompt = NormalizeText(response.GenerationPrompt, 1200),
            NegativePrompt = NormalizeText(response.NegativePrompt, 600) ?? "No readable text, no logos, no watermark, no fake UI paragraphs.",
            AltText = NormalizeText(response.AltText, 260) ?? $"Illustration for slide {item.SlideIndex}: {item.Heading}",
            SearchQueries = (response.SearchQueries ?? new List<string>())
                .Select(query => NormalizeText(query, 120))
                .Where(query => !string.IsNullOrWhiteSpace(query))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList()!,
            StatusHint = "queued",
            LastResultMessage = reason
        };
    }

    public static bool IsValidGenerationPrompt(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        var normalized = prompt.Trim();
        var lower = normalized.ToLowerInvariant();
        if (!lower.Contains("16:9") && !lower.Contains("presentation slide"))
        {
            return false;
        }

        if (Regex.IsMatch(lower, @"\b(logo|watermark)\b"))
        {
            return false;
        }

        if (Regex.IsMatch(lower, @"\b(with|include|including|add|show|display|contains?)\s+(readable\s+)?(text|text labels|text overlay|paragraphs?|captions?)\b"))
        {
            return false;
        }

        var hasVisualSubject = Regex.IsMatch(
            lower,
            @"\b(illustration|diagram|workflow|process|pipeline|architecture|system|comparison|scene|layout|visual|cards?|nodes?|flow|conceptual)\b");

        return hasVisualSubject && normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 10;
    }

    private static SlideImagePlan BuildInvalidPlan(SlideItem item)
    {
        var reason = $"Image plan for slide {item.SlideIndex} was invalid, so the slide will remain text-only.";
        return new SlideImagePlan
        {
            NeedsImage = false,
            Reason = reason,
            VisualRole = "none",
            SearchQueries = new List<string>(),
            StatusHint = "image-plan-invalid",
            LastResultMessage = reason
        };
    }

    private static string NormalizeRole(string? role, bool needsImage)
    {
        if (!needsImage)
        {
            return "none";
        }

        var normalized = NormalizeText(role, 40);
        return normalized != null && AllowedRoles.Contains(normalized) && !string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase)
            ? normalized.ToLowerInvariant()
            : "conceptual";
    }

    private static string? NormalizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(" ", value.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].Trim();
    }

    private static string Limit(string? value, int maxLength)
        => NormalizeText(value, maxLength) ?? "(empty)";

    private static string FirstNonEmpty(params string?[] values)
        => values.Select(value => NormalizeText(value, 300)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Academic document";

    private sealed class ImagePlanningResponse
    {
        [JsonPropertyName("needsImage")]
        public bool NeedsImage { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("visualRole")]
        public string? VisualRole { get; set; }

        [JsonPropertyName("generationPrompt")]
        public string? GenerationPrompt { get; set; }

        [JsonPropertyName("negativePrompt")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("altText")]
        public string? AltText { get; set; }

        [JsonPropertyName("searchQueries")]
        public List<string>? SearchQueries { get; set; }
    }
}
