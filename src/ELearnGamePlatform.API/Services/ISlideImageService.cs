using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.API.Services;

public interface ISlideImageService
{
    Task SourceImagesForItemAsync(SlideItem item, CancellationToken cancellationToken = default);
    Task<SlideItem?> RefreshImagesAsync(int deckId, int itemId, CancellationToken cancellationToken = default);
    Task<SlideItem?> SelectImageAsync(int deckId, int itemId, string candidateKey, CancellationToken cancellationToken = default);
}
