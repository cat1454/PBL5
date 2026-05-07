using ELearnGamePlatform.API.Contracts;
using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.API.Services;

public interface IQuestionMetricsService
{
    Task<QuestionGenerationMetricsDto> GetMetricsAsync(Document document, CancellationToken cancellationToken = default);
}
