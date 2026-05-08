using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace ELearnGamePlatform.Services.AI;

public class TokenBudgetPlanner : ITokenBudgetPlanner
{
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
}
