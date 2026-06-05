using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.API.Services;

public interface ISlideImageService
{
    Task SourceImagesForItemAsync(
        SlideItem item,
        SlideImageSourcingOptions? options = null,
        CancellationToken cancellationToken = default);
    Task<SlideItem?> RefreshImagesAsync(int deckId, int itemId, CancellationToken cancellationToken = default);
    Task<SlideItem?> SelectImageAsync(int deckId, int itemId, string candidateKey, CancellationToken cancellationToken = default);
}

public sealed class SlideImageSourcingOptions
{
    public static readonly SlideImageSourcingOptions Quality = new();

    public static readonly SlideImageSourcingOptions FastPreview = new()
    {
        AllowImagePlanning = false,
        AllowPdfRegionExtraction = false,
        AllowExternalImageGeneration = false,
        SkipReason = "fast-preview"
    };

    public bool AllowImagePlanning { get; init; } = true;
    public bool AllowPdfRegionExtraction { get; init; } = true;
    public bool AllowExternalImageGeneration { get; init; } = true;
    public string? SkipReason { get; init; }
}
