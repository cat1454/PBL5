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
}
