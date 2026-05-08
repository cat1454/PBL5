namespace ELearnGamePlatform.Core.Entities;

public class TokenBudgetPlan
{
    public string PromptType { get; set; } = string.Empty;
    public int ContextWindowTokens { get; set; }
    public int MaxInputTokens { get; set; }
    public int EstimatedInputTokens { get; set; }
    public bool IsWithinBudget { get; set; }
    public bool WasTruncated { get; set; }
    public int TargetInputTokens { get; set; }
    public double TargetInputBudgetFillRatio { get; set; }
    public int SelectedTextTokens { get; set; }
    public double BudgetFillRatio { get; set; }
    public bool IncludeFullChunkText { get; set; }
    public int TotalChunks { get; set; }
    public double AverageChunkTokens { get; set; }
    public List<DocumentCoverageChunk> SelectedChunks { get; set; } = new();
    public List<DocumentCoverageChunk> OmittedChunks { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
