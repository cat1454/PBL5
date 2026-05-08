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
    public bool EnablePreprocessingFallback { get; set; } = true;
    public bool EnableCropBorder { get; set; } = true;
    public bool EnableThresholdFallback { get; set; } = true;
    public int MaxPreprocessingVariantsPerPage { get; set; } = 3;
    public int MinPreprocessingGainThreshold { get; set; } = 3;
    public int MaxLowGainPreprocessingAttemptsPerDocument { get; set; } = 3;
    public bool EnableTextLayerQualityCalibration { get; set; } = true;
    public bool ExcludeCoverPagesFromQualityAverage { get; set; } = true;
    public bool EnableVietnameseTextNormalization { get; set; } = true;
    public int MinBodyPageQualityForAccepted { get; set; } = 60;
    public int MinBodyPageQualityForNeedsReview { get; set; } = 45;
}
