using ELearnGamePlatform.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ELearnGamePlatform.Services.AI;

public class OcrTextCleanupService : IOcrTextCleanupService
{
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<OcrTextCleanupService> _logger;

    public OcrTextCleanupService(IOllamaService ollamaService, ILogger<OcrTextCleanupService> logger)
    {
        _ollamaService = ollamaService;
        _logger = logger;
    }

    public async Task<string?> CleanupAsync(string rawText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        try
        {
            var prompt = BuildCleanupPrompt(rawText);
            var cleaned = await _ollamaService.GenerateResponseAsync(
                prompt,
                "You clean OCR text into faithful Markdown. Do not summarize, infer, or add facts.",
                OllamaModelProfile.Analysis);

            return string.IsNullOrWhiteSpace(cleaned)
                ? null
                : cleaned.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI OCR cleanup failed. Falling back to raw extracted text.");
            return null;
        }
    }

    internal static string BuildCleanupPrompt(string rawText)
        => $@"You are cleaning OCR text for downstream educational slide and question generation.

Rules:
- Preserve the original meaning exactly.
- Do not summarize.
- Do not add new information.
- Do not remove important content.
- Fix only OCR artifacts, broken line breaks, spacing, obvious character errors, heading structure, bullet formatting, and paragraph grouping.
- Keep names, numbers, dates, formulas, citations, terminology, and Vietnamese wording unchanged.
- If a phrase is unclear, keep it as-is instead of guessing.
- Return clean Markdown only.

OCR text:
{rawText}";
}
