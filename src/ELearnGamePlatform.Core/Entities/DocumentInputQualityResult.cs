namespace ELearnGamePlatform.Core.Entities;

public static class DocumentInputQualityClassifications
{
    public const string Rejected = "Rejected";
    public const string NeedReview = "NeedReview";
    public const string UsableWithWarning = "UsableWithWarning";
    public const string Good = "Good";
}

public class DocumentInputQualityResult
{
    public string Classification { get; set; } = DocumentInputQualityClassifications.Rejected;
    public int CharCount { get; set; }
    public int WordCount { get; set; }
    public double SignalRatio { get; set; }
    public double GarbageRatio { get; set; }
    public double TokenWasteRatio { get; set; }
    public int NoiseScore { get; set; }
    public int QualityScore { get; set; }
    public int EstimatedTokenCount { get; set; }
    public List<string> Warnings { get; set; } = new();

    public bool IsRejected => string.Equals(
        Classification,
        DocumentInputQualityClassifications.Rejected,
        StringComparison.OrdinalIgnoreCase);
}
