using ELearnGamePlatform.Core.Models;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IVisionRegionDescriber
{
    Task<VisionRegionDescriptionResult> DescribeAsync(
        VisionRegionDescriptionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IVisionPageImageProvider
{
    Task<VisionPageImageSource?> GetPageImageAsync(
        string filePath,
        string fileType,
        int pageNumber,
        CancellationToken cancellationToken = default);
}
