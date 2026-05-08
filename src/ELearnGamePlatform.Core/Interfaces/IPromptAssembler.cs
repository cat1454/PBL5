using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IPromptAssembler
{
    PromptAssemblyResult BuildAnalysisPrompt(string inputText, TokenBudgetPlan budgetPlan);
}
