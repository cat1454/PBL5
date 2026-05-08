namespace ELearnGamePlatform.Core.Entities;

public class OcrExtractionOptions
{
    public IReadOnlyCollection<string>? PreprocessingProfiles { get; init; }
    public bool IsPreprocessingFallback { get; init; }
}
