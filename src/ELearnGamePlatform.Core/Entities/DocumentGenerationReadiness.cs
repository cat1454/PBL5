namespace ELearnGamePlatform.Core.Entities;

public static class DocumentGenerationReadinessStatuses
{
    public const string Good = "Good";
    public const string NeedsReview = "NeedsReview";
    public const string LowConfidence = "LowConfidence";
    public const string ExtractionFailed = "ExtractionFailed";
}

public static class DocumentGenerationReadinessActions
{
    public const string Allow = "Allow";
    public const string AllowWithReviewWarning = "AllowWithReviewWarning";
    public const string WarnStrongly = "WarnStrongly";
    public const string BlockAutoGeneration = "BlockAutoGeneration";
}

public sealed class DocumentGenerationReadiness
{
    public int? DocumentId { get; set; }
    public string Status { get; set; } = DocumentGenerationReadinessStatuses.ExtractionFailed;
    public string Action { get; set; } = DocumentGenerationReadinessActions.BlockAutoGeneration;
    public double? Confidence { get; set; }
    public bool NeedsReview { get; set; } = true;
    public bool RequiresConfirmation { get; set; }
    public bool Blocked { get; set; }
    public bool ShowWarning { get; set; } = true;
    public List<string> Reasons { get; set; } = new();
}
