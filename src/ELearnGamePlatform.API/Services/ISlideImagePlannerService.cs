using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.API.Services;

public interface ISlideImagePlannerService
{
    Task<SlideImagePlan> PlanAsync(SlideItem item, string? documentTopic = null, CancellationToken cancellationToken = default);
}
