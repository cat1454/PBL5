using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Services.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class SlideGeneratorPromptTests
{
    [Fact]
    public async Task GenerateOutlineAsync_IncludesMarkdownStructureGuidanceWithoutNewLayoutField()
    {
        var ollama = new CapturingOllamaService();
        var service = new SlideGeneratorService(ollama, NullLogger<SlideGeneratorService>.Instance);

        await service.GenerateOutlineAsync(MarkdownSource(), null, new SlideDeckBrief(), 5);

        var prompt = ollama.Prompts.First(value =>
            value.Contains("lesson deck", StringComparison.OrdinalIgnoreCase)
            && value.Contains("Return JSON:", StringComparison.Ordinal));

        Assert.Contains("Use H1, H2, and H3 headings", prompt);
        Assert.Contains("Turn supported tables into comparison, process, or statistic slides", prompt);
        Assert.Contains("source evidence is weak", prompt);
        Assert.Contains("Prefer one key message per slide", prompt);
        Assert.Contains("slideType as the compatible layoutType", prompt);
        Assert.DoesNotContain("\"layoutType\"", prompt);
    }

    [Fact]
    public async Task GenerateSlideAsync_RequiresGroundedMarkdownAwareCompatibleContentSchema()
    {
        var ollama = new CapturingOllamaService();
        var service = new SlideGeneratorService(ollama, NullLogger<SlideGeneratorService>.Instance);
        var outline = new SlideOutlineSlide
        {
            SlideIndex = 2,
            SlideType = SlideItemType.Content,
            Heading = "Comparison",
            Goal = "Compare the documented values",
            KeyMessage = "Alpha is larger than Beta"
        };

        await service.GenerateSlideAsync(
            MarkdownSource(),
            null,
            new SlideDeckBrief(),
            outline,
            2,
            5);

        var prompt = ollama.Prompts.First(value =>
            value.Contains("learner-facing presentation slide", StringComparison.OrdinalIgnoreCase)
            || value.Contains("grounded study slides", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("nearest relevant heading or section", prompt);
        Assert.Contains("Preserve table meaning", prompt);
        Assert.Contains("Keep one keyMessage per slide", prompt);
        Assert.Contains("heading, keyMessage, bodyBlocks, evidenceFromText, speakerNotes", prompt);
        Assert.Contains("Do not add a layoutType JSON field", prompt);
        Assert.Contains("\"bodyBlocks\"", prompt);
        Assert.DoesNotContain("\"layoutType\"", prompt);
    }

    private static string MarkdownSource()
        => """
        # Main topic

        ## Comparison

        | Item | Value |
        | --- | --- |
        | Alpha | 10 |
        | Beta | 6 |

        ### Interpretation

        - Alpha exceeds Beta by four units.
        - The table is the only allowed evidence.
        """;

    private sealed class CapturingOllamaService : IOllamaService
    {
        public List<string> Prompts { get; } = new();

        public Task<string> GenerateResponseAsync(
            string prompt,
            string? systemPrompt = null,
            OllamaModelProfile profile = OllamaModelProfile.Generation)
        {
            Prompts.Add(prompt);
            return Task.FromResult(string.Empty);
        }

        public Task<T?> GenerateStructuredResponseAsync<T>(
            string prompt,
            string? systemPrompt = null,
            OllamaModelProfile profile = OllamaModelProfile.Generation)
            where T : class
        {
            Prompts.Add(prompt);
            return Task.FromResult<T?>(null);
        }

        public Task<StructuredGenerationResult<T>> GenerateStructuredResponseWithMetadataAsync<T>(
            string prompt,
            string? systemPrompt = null,
            OllamaModelProfile profile = OllamaModelProfile.Generation)
            where T : class
        {
            Prompts.Add(prompt);
            return Task.FromResult(new StructuredGenerationResult<T>());
        }

        public Task<bool> IsAvailableAsync()
            => Task.FromResult(true);
    }
}
