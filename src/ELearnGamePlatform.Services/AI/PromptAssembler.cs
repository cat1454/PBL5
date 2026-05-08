using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;

namespace ELearnGamePlatform.Services.AI;

public class PromptAssembler : IPromptAssembler
{
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
        if (budgetPlan.OmittedChunks.Any())
        {
            warnings.Add($"Prompt assembled from {chunks.Count} selected chunk(s); {budgetPlan.OmittedChunks.Count} chunk(s) omitted.");
        }

        return new PromptAssemblyResult
        {
            SystemPrompt = "You are an educational content analyzer. Use only the supplied selected document chunks and keep output grounded.",
            UserPrompt = $@"Analyze the following selected educational document chunks for downstream study flows.

Use only these chunks. Respect chunk ids, section keys, page ranges, and quality scores when grounding topics and key points.

Selected chunks:
{BuildSelectedChunkBlock(chunks)}",
            BudgetPlan = budgetPlan,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static string BuildSelectedChunkBlock(IReadOnlyList<DocumentCoverageChunk> chunks)
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
estimatedTokens: {chunk.EstimatedTokenCount}
label: {chunk.Label}
summary: {chunk.Summary}
keyFacts:
- {string.Join(Environment.NewLine + "- ", chunk.KeyFacts.DefaultIfEmpty("No key facts extracted."))}
evidence:
{chunk.EvidenceExcerpt}";
            }));
    }

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
