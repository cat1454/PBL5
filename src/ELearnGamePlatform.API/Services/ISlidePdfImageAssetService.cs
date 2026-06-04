using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.API.Services;

public interface ISlidePdfImageAssetService
{
    Task<SlideImageCandidate?> TryCreateCandidateAsync(
        SlideItem item,
        SlideImagePlan imagePlan,
        CancellationToken cancellationToken = default);
}
