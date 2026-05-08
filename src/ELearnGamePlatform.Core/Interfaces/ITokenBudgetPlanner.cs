using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface ITokenBudgetPlanner
{
    TokenBudgetPlan PlanText(string text, string promptType);
    TokenBudgetPlan PlanChunks(IReadOnlyList<DocumentCoverageChunk> chunks, string promptType);
}
