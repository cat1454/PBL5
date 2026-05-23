namespace ELearnGamePlatform.Core.Models;

public class KnowledgeMapBuildResult
{
    public string Text { get; set; } = string.Empty;
    public bool IsUsable { get; set; }
    public List<string> Warnings { get; set; } = new();
    public int EstimatedTokens { get; set; }
    public string? UnusableReason { get; set; }
}
