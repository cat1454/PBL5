using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace ELearnGamePlatform.Services.AI;

public class TokenBudgetPlanner : ITokenBudgetPlanner
{
    private const int MinimumChunkQualityScore = 35;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly LocalLlmSettings _settings;

    public TokenBudgetPlanner(ITokenEstimator tokenEstimator, IOptions<LocalLlmSettings> settings)
    {
        _tokenEstimator = tokenEstimator;
        _settings = settings.Value;
    }

    public TokenBudgetPlan PlanText(string text, string promptType)
    {
        var estimatedInputTokens = _tokenEstimator.EstimateTokens(text);
        var maxInputTokens = _settings.MaxInputTokens;
        var warnings = new List<string>();

        if (maxInputTokens <= 0)
        {
            warnings.Add("Local LLM input budget is zero or negative; check LocalLlmSettings.");
        }
        else if (estimatedInputTokens > maxInputTokens)
        {
            warnings.Add($"Estimated input tokens ({estimatedInputTokens}) exceed max input budget ({maxInputTokens}).");
        }
        else if (estimatedInputTokens >= (int)Math.Round(maxInputTokens * 0.85d))
        {
            warnings.Add($"Estimated input tokens ({estimatedInputTokens}) are close to max input budget ({maxInputTokens}).");
        }

        return new TokenBudgetPlan
        {
            PromptType = promptType,
            ContextWindowTokens = _settings.ContextWindowTokens,
            MaxInputTokens = maxInputTokens,
            EstimatedInputTokens = estimatedInputTokens,
            IsWithinBudget = maxInputTokens > 0 && estimatedInputTokens <= maxInputTokens,
            WasTruncated = false,
            Warnings = warnings
        };
    }

    public TokenBudgetPlan PlanChunks(IReadOnlyList<DocumentCoverageChunk> chunks, string promptType)
    {
        var maxInputTokens = _settings.MaxInputTokens;
        var preparedChunks = chunks
            .Select(chunk => PrepareChunk(chunk))
            .OrderBy(chunk => chunk.ChunkNumber)
            .ToList();
        var warnings = new List<string>();

        if (maxInputTokens <= 0)
        {
            warnings.Add("Local LLM input budget is zero or negative; check LocalLlmSettings.");
        }

        var lowQualityChunks = preparedChunks
            .Where(chunk => !chunk.IsEligibleForQuestionGeneration || chunk.ChunkQualityScore < MinimumChunkQualityScore)
            .ToList();
        var candidates = preparedChunks
            .Where(chunk => chunk.IsEligibleForQuestionGeneration && chunk.ChunkQualityScore >= MinimumChunkQualityScore)
            .ToList();

        var selected = maxInputTokens > 0
            ? SelectChunksWithinBudget(candidates, maxInputTokens)
            : new List<DocumentCoverageChunk>();
        var selectedIds = selected.Select(chunk => chunk.ChunkId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var omitted = preparedChunks
            .Where(chunk => !selectedIds.Contains(chunk.ChunkId))
            .OrderBy(chunk => chunk.ChunkNumber)
            .ToList();
        var estimatedInputTokens = selected.Sum(chunk => chunk.EstimatedTokenCount);

        if (lowQualityChunks.Any())
        {
            warnings.Add($"Omitted {lowQualityChunks.Count} low-quality chunk(s) below quality threshold {MinimumChunkQualityScore}.");
        }

        if (omitted.Count > lowQualityChunks.Count)
        {
            warnings.Add($"Omitted {omitted.Count - lowQualityChunks.Count} chunk(s) to fit max input budget ({maxInputTokens}).");
        }

        if (estimatedInputTokens >= (int)Math.Round(maxInputTokens * 0.85d) && maxInputTokens > 0)
        {
            warnings.Add($"Selected chunk tokens ({estimatedInputTokens}) are close to max input budget ({maxInputTokens}).");
        }

        return new TokenBudgetPlan
        {
            PromptType = promptType,
            ContextWindowTokens = _settings.ContextWindowTokens,
            MaxInputTokens = maxInputTokens,
            EstimatedInputTokens = estimatedInputTokens,
            IsWithinBudget = maxInputTokens > 0 && estimatedInputTokens <= maxInputTokens,
            WasTruncated = omitted.Any(),
            SelectedChunks = selected,
            OmittedChunks = omitted,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private DocumentCoverageChunk PrepareChunk(DocumentCoverageChunk chunk)
    {
        var cloned = CloneChunk(chunk);
        if (cloned.EstimatedTokenCount <= 0)
        {
            cloned.EstimatedTokenCount = Math.Max(1, _tokenEstimator.EstimateTokens(BuildChunkTokenText(cloned)));
        }

        if (cloned.TokenEfficiencyScore <= 0)
        {
            var qualityPerToken = cloned.ChunkQualityScore / Math.Max(1d, cloned.EstimatedTokenCount / 100d);
            cloned.TokenEfficiencyScore = Math.Clamp((int)Math.Round(qualityPerToken), 0, 100);
        }

        return cloned;
    }

    private List<DocumentCoverageChunk> SelectChunksWithinBudget(List<DocumentCoverageChunk> candidates, int maxInputTokens)
    {
        if (candidates.Sum(chunk => chunk.EstimatedTokenCount) <= maxInputTokens)
        {
            return candidates.OrderBy(chunk => chunk.ChunkNumber).ToList();
        }

        var selected = new List<DocumentCoverageChunk>();
        var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in candidates
            .GroupBy(chunk => chunk.SectionKey ?? chunk.ChunkId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(GetChunkRankScore).ThenBy(item => item.EstimatedTokenCount).First())
            .OrderByDescending(GetChunkRankScore))
        {
            TryAdd(chunk);
        }

        foreach (var chunk in candidates
            .GroupBy(chunk => chunk.CoverageZone, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(GetChunkRankScore).ThenBy(item => item.EstimatedTokenCount).First())
            .OrderByDescending(GetChunkRankScore))
        {
            TryAdd(chunk);
        }

        foreach (var chunk in candidates.OrderByDescending(GetChunkRankScore).ThenBy(item => item.EstimatedTokenCount))
        {
            TryAdd(chunk);
        }

        return selected.OrderBy(chunk => chunk.ChunkNumber).ToList();

        void TryAdd(DocumentCoverageChunk chunk)
        {
            if (selectedIds.Contains(chunk.ChunkId))
            {
                return;
            }

            var nextTotal = selected.Sum(item => item.EstimatedTokenCount) + chunk.EstimatedTokenCount;
            if (nextTotal > maxInputTokens)
            {
                return;
            }

            selected.Add(chunk);
            selectedIds.Add(chunk.ChunkId);
        }
    }

    private static double GetChunkRankScore(DocumentCoverageChunk chunk)
        => (0.45d * chunk.ChunkQualityScore)
            + (0.25d * chunk.TokenEfficiencyScore)
            + (0.20d * GetCoverageDiversityScore(chunk))
            + (0.10d * chunk.KeyFactDensityScore);

    private static int GetCoverageDiversityScore(DocumentCoverageChunk chunk)
    {
        var score = 55;
        if (!string.IsNullOrWhiteSpace(chunk.SectionKey))
        {
            score += 20;
        }

        if (chunk.CoverageZone is "dau" or "giua" or "cuoi")
        {
            score += 15;
        }

        if (chunk.IsPrimarySection)
        {
            score += 10;
        }

        return Math.Clamp(score, 0, 100);
    }

    private static string BuildChunkTokenText(DocumentCoverageChunk chunk)
        => string.Join(
            Environment.NewLine,
            new[]
            {
                chunk.ChunkId,
                chunk.SectionKey,
                chunk.Label,
                chunk.Summary,
                chunk.EvidenceExcerpt,
                string.Join(" ", chunk.KeyFacts)
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static DocumentCoverageChunk CloneChunk(DocumentCoverageChunk chunk)
        => new()
        {
            ChunkNumber = chunk.ChunkNumber,
            ChunkId = chunk.ChunkId,
            Zone = chunk.Zone,
            CoverageZone = string.IsNullOrWhiteSpace(chunk.CoverageZone) ? chunk.Zone : chunk.CoverageZone,
            Label = chunk.Label,
            HeadingKind = chunk.HeadingKind,
            HeadingLevel = chunk.HeadingLevel,
            HeadingMarker = chunk.HeadingMarker,
            HeadingText = chunk.HeadingText,
            NormalizedHeading = chunk.NormalizedHeading,
            HeadingPath = chunk.HeadingPath,
            ParentHeadingPath = chunk.ParentHeadingPath,
            SectionKey = chunk.SectionKey,
            IsPrimarySection = chunk.IsPrimarySection,
            Classification = chunk.Classification,
            TeachabilityScore = chunk.TeachabilityScore,
            ChunkQualityScore = chunk.ChunkQualityScore,
            EstimatedTokenCount = chunk.EstimatedTokenCount,
            TokenEfficiencyScore = chunk.TokenEfficiencyScore,
            KeyFactDensityScore = chunk.KeyFactDensityScore,
            PositiveSignals = chunk.PositiveSignals.ToList(),
            NegativeSignals = chunk.NegativeSignals.ToList(),
            SelectionReason = chunk.SelectionReason,
            StartPage = chunk.StartPage,
            EndPage = chunk.EndPage,
            SourcePageStart = chunk.SourcePageStart ?? chunk.StartPage,
            SourcePageEnd = chunk.SourcePageEnd ?? chunk.EndPage,
            IsEligibleForQuestionGeneration = chunk.IsEligibleForQuestionGeneration,
            Warnings = chunk.Warnings.ToList(),
            Summary = chunk.Summary,
            EvidenceExcerpt = chunk.EvidenceExcerpt,
            KeyFacts = chunk.KeyFacts.ToList()
        };
}
