namespace ELearnGamePlatform.Core.Configuration;

public class LocalLlmSettings
{
    public const string SectionName = "LocalLlmSettings";

    public int ContextWindowTokens { get; set; } = 8192;
    public int ReservedOutputTokens { get; set; } = 1200;
    public int ReservedInstructionTokens { get; set; } = 900;
    public int SafetyMarginTokens { get; set; } = 500;
    public string Profile { get; set; } = "quality";
    public int TargetChunkTokens { get; set; } = 700;
    public int MaxChunkTokens { get; set; } = 1100;
    public int ChunkOverlapTokens { get; set; } = 80;
    public double TargetInputBudgetFillRatio { get; set; } = 0.80d;
    public bool IncludeFullSelectedChunkText { get; set; } = false;
    public bool EnableAnalysisRefine { get; set; } = true;
    public bool AllowLowConfidenceCalculationQuestions { get; set; } = false;
    public int MinTextLengthForAIRefine { get; set; } = 1500;
    public int MinCoverageChunksForAIRefine { get; set; } = 3;

    public int MaxInputTokens => Math.Max(
        0,
        ContextWindowTokens
        - ReservedOutputTokens
        - ReservedInstructionTokens
        - SafetyMarginTokens);
}
