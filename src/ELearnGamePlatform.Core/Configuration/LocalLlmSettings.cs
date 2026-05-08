namespace ELearnGamePlatform.Core.Configuration;

public class LocalLlmSettings
{
    public const string SectionName = "LocalLlmSettings";

    public int ContextWindowTokens { get; set; } = 8192;
    public int ReservedOutputTokens { get; set; } = 1200;
    public int ReservedInstructionTokens { get; set; } = 900;
    public int SafetyMarginTokens { get; set; } = 500;
    public string Profile { get; set; } = "quality";

    public int MaxInputTokens => Math.Max(
        0,
        ContextWindowTokens
        - ReservedOutputTokens
        - ReservedInstructionTokens
        - SafetyMarginTokens);
}
