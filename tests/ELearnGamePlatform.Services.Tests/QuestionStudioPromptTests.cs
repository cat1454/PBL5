using ELearnGamePlatform.API.Services.QuestionStudio;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class QuestionStudioPromptTests
{
    [Fact]
    public async Task CanonicalGenerator_PromptUsesMarkdownSectionsAndStrongEvidenceRules()
    {
        var ollama = new CapturingOllamaService();
        var generator = new CanonicalQuestionGenerator(
            ollama,
            NullLogger<CanonicalQuestionGenerator>.Instance);
        var run = new QuestionGenerationRun
        {
            Id = 12,
            DocumentId = 7,
            Mode = "balanced",
            TargetDraftCount = 2
        };
        var unit = new QuestionSourceUnit
        {
            Id = 21,
            DocumentId = 7,
            GenerationRunId = 12,
            TopicTag = "chapter-1:comparison",
            Content = "| Model | Accuracy |\n| A | 82% |\n| B | 91% |",
            Confidence = 1
        };

        await generator.GenerateAsync(
            run,
            new[] { unit },
            new[] { "MultipleChoice" },
            new[] { "Medium" },
            1);

        var prompt = Assert.Single(ollama.Prompts);
        Assert.Contains("H1/H2/H3 hierarchy", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("specific source section", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("specific excerpt", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("table of contents", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reliable Markdown table", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quote a short evidence phrase or clearly paraphrase", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("return fewer questions", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"""sourceEvidence"": ""...""", prompt, StringComparison.Ordinal);
    }

    private sealed class CapturingOllamaService : IOllamaService
    {
        public List<string> Prompts { get; } = new();

        public Task<string> GenerateResponseAsync(
            string prompt,
            string? systemPrompt = null,
            OllamaModelProfile profile = OllamaModelProfile.Generation)
            => Task.FromResult(string.Empty);

        public Task<T?> GenerateStructuredResponseAsync<T>(
            string prompt,
            string? systemPrompt = null,
            OllamaModelProfile profile = OllamaModelProfile.Generation)
            where T : class
        {
            Prompts.Add(prompt);
            return Task.FromResult<T?>(null);
        }

        public async Task<StructuredGenerationResult<T>> GenerateStructuredResponseWithMetadataAsync<T>(
            string prompt,
            string? systemPrompt = null,
            OllamaModelProfile profile = OllamaModelProfile.Generation)
            where T : class
        {
            var value = await GenerateStructuredResponseAsync<T>(prompt, systemPrompt, profile);
            return new StructuredGenerationResult<T> { Value = value };
        }

        public Task<bool> IsAvailableAsync() => Task.FromResult(true);
    }
}
