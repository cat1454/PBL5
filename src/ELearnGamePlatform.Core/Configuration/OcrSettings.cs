namespace ELearnGamePlatform.Core.Configuration;

public class OcrSettings
{
    public const string SectionName = "OcrSettings";

    public int DefaultPdfDpi { get; set; } = 300;
    public int RetryPdfDpi { get; set; } = 400;
    public int MinAcceptablePageQuality { get; set; } = 75;
    public int RetryThreshold { get; set; } = 55;
    public int MaxRetryPerPage { get; set; } = 2;
    public bool EnableQualityProfile { get; set; } = true;
}
