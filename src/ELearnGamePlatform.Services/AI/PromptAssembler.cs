using System.Text;
using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace ELearnGamePlatform.Services.AI;

public class PromptAssembler : IPromptAssembler
{
    private readonly ITokenEstimator _tokenEstimator;
    private readonly LocalLlmSettings _settings;

    public PromptAssembler(ITokenEstimator tokenEstimator, IOptions<LocalLlmSettings> settings)
    {
        _tokenEstimator = tokenEstimator;
        _settings = settings.Value;
    }

    public PromptAssemblyResult BuildAnalysisPrompt(string inputText, TokenBudgetPlan budgetPlan)
    {
        var warnings = new List<string>(budgetPlan.Warnings);
        if (!budgetPlan.IsWithinBudget)
        {
            warnings.Add("Prompt assembly received input that exceeds the configured local LLM budget.");
        }

        return new PromptAssemblyResult
        {
            SystemPrompt = "You are an educational content analyzer. Use only the supplied document text and keep output grounded.",
            UserPrompt = $@"Analyze the following educational document text for downstream study flows.

Document text:
{inputText}",
            BudgetPlan = budgetPlan,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    public PromptAssemblyResult BuildAnalysisPrompt(IReadOnlyList<DocumentCoverageChunk> selectedChunks, TokenBudgetPlan budgetPlan)
    {
        var selectedIds = budgetPlan.SelectedChunks
            .Select(chunk => chunk.ChunkId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chunks = selectedChunks
            .Where(chunk => chunk.IsEligibleForQuestionGeneration)
            .Where(chunk => chunk.ChunkQualityScore >= 35)
            .Where(chunk => selectedIds.Count == 0 || selectedIds.Contains(chunk.ChunkId))
            .OrderBy(chunk => chunk.ChunkNumber)
            .ToList();
        var warnings = new List<string>(budgetPlan.Warnings);
        var includeFullText = ShouldIncludeFullChunkText(budgetPlan);
        chunks = FitChunksToHardBudget(chunks, budgetPlan.MaxInputTokens, includeFullText, warnings);
        var selectedTextTokens = chunks.Sum(chunk => chunk.TextTokenCount > 0 ? chunk.TextTokenCount : _tokenEstimator.EstimateTokens(GetChunkPromptText(chunk)));
        var fillRatio = budgetPlan.MaxInputTokens > 0
            ? selectedTextTokens / (double)budgetPlan.MaxInputTokens
            : 0d;

        if (budgetPlan.OmittedChunks.Any())
        {
            warnings.Add($"Prompt assembled from {chunks.Count} selected chunk(s); {budgetPlan.OmittedChunks.Count} chunk(s) omitted.");
        }

        warnings.Add($"Prompt selected token count: {selectedTextTokens}/{budgetPlan.MaxInputTokens}; budget fill ratio: {fillRatio:P0}; full text included: {includeFullText}.");

        return new PromptAssemblyResult
        {
            SystemPrompt = "You are an educational content analyzer. Use only the supplied selected document chunks and keep output grounded.",
            UserPrompt = $@"Analyze the following selected educational document chunks for downstream study flows.

Use only these chunks. Respect chunk ids, section keys, page ranges, and quality scores when grounding topics and key points.

Selected chunks:
{BuildSelectedChunkBlock(chunks, includeFullText)}",
            BudgetPlan = budgetPlan,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private List<DocumentCoverageChunk> FitChunksToHardBudget(
        List<DocumentCoverageChunk> chunks,
        int maxInputTokens,
        bool includeFullText,
        List<string> warnings)
    {
        if (maxInputTokens <= 0 || chunks.Count == 0)
        {
            return chunks;
        }

        var fitted = chunks.ToList();
        while (fitted.Count > 1 && EstimatePromptTokens(fitted, includeFullText) > maxInputTokens)
        {
            var remove = fitted
                .OrderBy(GetPromptChunkRankScore)
                .ThenByDescending(chunk => chunk.EstimatedTokenCount)
                .First();
            fitted.Remove(remove);
            warnings.Add($"Omitted {remove.ChunkId} during prompt assembly to stay under hard input budget.");
        }

        if (EstimatePromptTokens(fitted, includeFullText) > maxInputTokens)
        {
            warnings.Add("Prompt still exceeds hard budget after chunk-level trimming; the remaining chunk may be trimmed by the Ollama service or downstream request limits.");
        }

        return fitted.OrderBy(chunk => chunk.ChunkNumber).ToList();
    }

    private int EstimatePromptTokens(IReadOnlyList<DocumentCoverageChunk> chunks, bool includeFullText)
        => _tokenEstimator.EstimateTokens(BuildSelectedChunkBlock(chunks, includeFullText));

    private bool ShouldIncludeFullChunkText(TokenBudgetPlan budgetPlan)
        => _settings.IncludeFullSelectedChunkText
            && budgetPlan.IncludeFullChunkText
            && budgetPlan.MaxInputTokens >= 4000;

    private static string BuildSelectedChunkBlock(IReadOnlyList<DocumentCoverageChunk> chunks, bool includeFullText)
    {
        if (chunks.Count == 0)
        {
            return "(no eligible chunks selected)";
        }

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            chunks.Select(chunk =>
            {
                var pageRange = BuildPageRange(chunk);
                return $@"[{chunk.ChunkId}]
sectionKey: {chunk.SectionKey ?? chunk.ChunkId}
coverageZone: {chunk.CoverageZone}
pageRange: {pageRange}
qualityScore: {chunk.ChunkQualityScore}
estimatedTokens: {(chunk.TextTokenCount > 0 ? chunk.TextTokenCount : chunk.EstimatedTokenCount)}
heading: {chunk.HeadingPath ?? chunk.HeadingText ?? chunk.NormalizedHeading ?? "none"}
label: {chunk.Label}
summary: {chunk.Summary}
keyFacts:
- {string.Join(Environment.NewLine + "- ", chunk.KeyFacts.DefaultIfEmpty("No key facts extracted."))}
evidence:
{chunk.EvidenceExcerpt}
text:
<<<
{(includeFullText ? GetChunkPromptText(chunk) : chunk.EvidenceExcerpt)}
>>>";
            }));
    }

    private static double GetPromptChunkRankScore(DocumentCoverageChunk chunk)
        => (0.55d * chunk.ChunkQualityScore)
            + (0.20d * chunk.TokenEfficiencyScore)
            + (0.15d * chunk.KeyFactDensityScore)
            + (0.10d * (chunk.IsPrimarySection ? 100 : 50));

    private static string GetChunkPromptText(DocumentCoverageChunk chunk)
        => !string.IsNullOrWhiteSpace(chunk.NormalizedText)
            ? chunk.NormalizedText!
            : !string.IsNullOrWhiteSpace(chunk.Text)
                ? chunk.Text!
                : chunk.EvidenceExcerpt;

    private static string BuildPageRange(DocumentCoverageChunk chunk)
    {
        var start = chunk.SourcePageStart ?? chunk.StartPage;
        var end = chunk.SourcePageEnd ?? chunk.EndPage;
        if (start.HasValue && end.HasValue)
        {
            return start == end ? start.Value.ToString() : $"{start}-{end}";
        }

        return start?.ToString() ?? end?.ToString() ?? "unknown";
    }
}
