namespace ELearnGamePlatform.Core.Models;

public sealed class VisionRegionDescriptionRequest
{
    public required string ImagePath { get; init; }
    public required string Model { get; init; }
    public int PageNumber { get; init; }
    public string RegionType { get; init; } = string.Empty;
    public string RegionText { get; init; } = string.Empty;
    public string PromptContext { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; }
}

public sealed class VisionRegionDescriptionResult
{
    public string Description { get; init; } = string.Empty;
    public List<string> ExtractedLabels { get; init; } = new();
    public List<string> Relationships { get; init; } = new();
    public double? Confidence { get; init; }
    public string UncertaintyReason { get; init; } = string.Empty;
    public bool Succeeded { get; init; }
    public string FailureReason { get; init; } = string.Empty;

    public static VisionRegionDescriptionResult Failed(string reason)
        => new()
        {
            Succeeded = false,
            UncertaintyReason = reason,
            FailureReason = reason
        };
}

public sealed class VisionPageImageSource : IAsyncDisposable
{
    public required string ImagePath { get; init; }
    public int PageNumber { get; init; }
    public string? TemporaryDirectory { get; init; }

    public ValueTask DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(TemporaryDirectory) && Directory.Exists(TemporaryDirectory))
        {
            try
            {
                Directory.Delete(TemporaryDirectory, recursive: true);
            }
            catch
            {
                // Temporary vision images are best-effort cleanup.
            }
        }

        return ValueTask.CompletedTask;
    }
}
