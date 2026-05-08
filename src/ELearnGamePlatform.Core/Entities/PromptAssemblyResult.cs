namespace ELearnGamePlatform.Core.Entities;

public class PromptAssemblyResult
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public TokenBudgetPlan BudgetPlan { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
