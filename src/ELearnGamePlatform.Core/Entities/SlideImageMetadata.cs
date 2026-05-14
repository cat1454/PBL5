namespace ELearnGamePlatform.Core.Entities;

public class SlideImagePlan
{
    public bool NeedsImage { get; set; } = true;
    public string? Reason { get; set; }
    public string? VisualRole { get; set; }
    public string? AltText { get; set; }
    public string? RedactedPrompt { get; set; }
    public List<string> SearchQueries { get; set; } = new();
    public string? GenerationPrompt { get; set; }
    public string? NegativePrompt { get; set; }
    public string? StatusHint { get; set; }
    public string? LastResultMessage { get; set; }
    public DateTime? LastAttemptedAtUtc { get; set; }
}

public class SlideImageCandidate
{
    public string Key { get; set; } = string.Empty;
    public string SourceType { get; set; } = "web";
    public string Provider { get; set; } = string.Empty;
    public string? OriginUrl { get; set; }
    public string? LocalAssetUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? AltText { get; set; }
    public string? LicenseLabel { get; set; }
    public string? AttributionText { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? Score { get; set; }
    public bool IsSelected { get; set; }
    public string? LayoutMode { get; set; }
}

public class SlideImageState
{
    public bool NeedsImage { get; set; } = true;
    public string Status { get; set; } = "not-requested";
    public string? Message { get; set; }
    public string? Detail { get; set; }
    public int CandidateCount { get; set; }
    public string? SelectedImageKey { get; set; }
    public string? Error { get; set; }
}
