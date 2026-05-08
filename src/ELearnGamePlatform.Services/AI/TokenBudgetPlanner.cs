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
            TargetInputTokens = maxInputTokens,
            TargetInputBudgetFillRatio = 1d,
            EstimatedInputTokens = estimatedInputTokens,
            IsWithinBudget = maxInputTokens > 0 && estimatedInputTokens <= maxInputTokens,
            WasTruncated = false,
            SelectedTextTokens = estimatedInputTokens,
            BudgetFillRatio = maxInputTokens > 0 ? Math.Round(estimatedInputTokens / (double)maxInputTokens, 4) : 0d,
            IncludeFullChunkText = false,
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
        var fillRatio = Math.Clamp(_settings.TargetInputBudgetFillRatio, 0.1d, 1d);
        var targetInputTokens = maxInputTokens > 0
            ? Math.Max(1, (int)Math.Floor(maxInputTokens * fillRatio))
            : 0;

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

        var selected = targetInputTokens > 0
            ? SelectChunksWithinBudget(candidates, targetInputTokens)
            : new List<DocumentCoverageChunk>();
        var selectedIds = selected.Select(chunk => chunk.ChunkId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var omitted = preparedChunks
            .Where(chunk => !selectedIds.Contains(chunk.ChunkId))
            .OrderBy(chunk => chunk.ChunkNumber)
            .ToList();
        var estimatedInputTokens = selected.Sum(chunk => chunk.EstimatedTokenCount);
        var selectedTextTokens = selected.Sum(chunk => chunk.TextTokenCount > 0 ? chunk.TextTokenCount : chunk.EstimatedTokenCount);
        var averageChunkTokens = preparedChunks.Count > 0
            ? Math.Round(preparedChunks.Average(chunk => chunk.TextTokenCount > 0 ? chunk.TextTokenCount : chunk.EstimatedTokenCount), 2)
            : 0d;
        var budgetFillRatio = maxInputTokens > 0
            ? Math.Round(selectedTextTokens / (double)maxInputTokens, 4)
            : 0d;

        if (lowQualityChunks.Any())
        {
            warnings.Add($"Omitted {lowQualityChunks.Count} low-quality chunk(s) below quality threshold {MinimumChunkQualityScore}.");
        }

        if (omitted.Count > lowQualityChunks.Count)
        {
            warnings.Add($"Omitted {omitted.Count - lowQualityChunks.Count} chunk(s) to fit target input budget ({targetInputTokens}/{maxInputTokens}).");
        }

        if (estimatedInputTokens > maxInputTokens && maxInputTokens > 0)
        {
            warnings.Add($"Selected chunk tokens ({estimatedInputTokens}) exceed hard max input budget ({maxInputTokens}).");
        }
        else if (estimatedInputTokens >= (int)Math.Round(maxInputTokens * 0.85d) && maxInputTokens > 0)
        {
            warnings.Add($"Selected chunk tokens ({estimatedInputTokens}) are close to max input budget ({maxInputTokens}).");
        }

        warnings.Add($"Selected token count: {selectedTextTokens}/{maxInputTokens}; fill ratio: {budgetFillRatio:P0}; omitted chunks: {omitted.Count}; full text included: {_settings.IncludeFullSelectedChunkText}.");

        return new TokenBudgetPlan
        {
            PromptType = promptType,
            ContextWindowTokens = _settings.ContextWindowTokens,
            MaxInputTokens = maxInputTokens,
            TargetInputTokens = targetInputTokens,
            TargetInputBudgetFillRatio = fillRatio,
            EstimatedInputTokens = estimatedInputTokens,
            IsWithinBudget = maxInputTokens > 0 && estimatedInputTokens <= maxInputTokens,
            WasTruncated = omitted.Any(),
            SelectedTextTokens = selectedTextTokens,
            BudgetFillRatio = budgetFillRatio,
            IncludeFullChunkText = _settings.IncludeFullSelectedChunkText,
            TotalChunks = preparedChunks.Count,
            AverageChunkTokens = averageChunkTokens,
            SelectedChunks = selected,
            OmittedChunks = omitted,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private DocumentCoverageChunk PrepareChunk(DocumentCoverageChunk chunk)
    {
        var cloned = CloneChunk(chunk);
        if (cloned.TextTokenCount <= 0)
        {
            cloned.TextTokenCount = Math.Max(1, _tokenEstimator.EstimateTokens(GetChunkFullText(cloned)));
        }

        var tokenText = _settings.IncludeFullSelectedChunkText
            ? BuildChunkTokenText(cloned)
            : BuildSummaryTokenText(cloned);
        cloned.EstimatedTokenCount = Math.Max(1, _tokenEstimator.EstimateTokens(tokenText));

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
    {
        var text = GetChunkFullText(chunk);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return string.Join(
                Environment.NewLine,
                new[]
                {
                    chunk.ChunkId,
                    chunk.SectionKey,
                    chunk.Label,
                    chunk.Summary,
                    string.Join(" ", chunk.KeyFacts),
                    text
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        return BuildSummaryTokenText(chunk);
    }

    private static string BuildSummaryTokenText(DocumentCoverageChunk chunk)
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

    private static string GetChunkFullText(DocumentCoverageChunk chunk)
        => !string.IsNullOrWhiteSpace(chunk.NormalizedText)
            ? chunk.NormalizedText!
            : chunk.Text ?? string.Empty;

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
            KeyFacts = chunk.KeyFacts.ToList(),
            Text = chunk.Text,
            NormalizedText = chunk.NormalizedText,
            TextTokenCount = chunk.TextTokenCount
        };
}
