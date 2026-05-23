namespace ELearnGamePlatform.Core.Options;

public class DocumentUnderstandingOptions
{
    public const string SectionName = "DocumentUnderstanding";

    public bool Enabled { get; set; } = false;
    public bool EnableLayoutAnalysis { get; set; } = false;
    public bool EnableVisionAnalysis { get; set; } = false;
    public bool EnableTableExtraction { get; set; } = false;
    public bool FallbackToLegacyOcr { get; set; } = true;
    public bool EnforceGenerationGate { get; set; } = false;
    public bool ShowGenerationWarnings { get; set; } = true;
    public double MinAutoGenerateConfidence { get; set; } = 0.85d;
    public double MinReviewRequiredConfidence { get; set; } = 0.65d;
    public double MinStrongWarningConfidence { get; set; } = 0.45d;
    public string VisionModel { get; set; } = "llama3.2-vision:11b";
    public int VisionTimeoutSeconds { get; set; } = 120;
    public int MaxVisionPagesPerDocument { get; set; } = 5;
    public int MaxVisionRegionsPerPage { get; set; } = 3;

    public int MaxVisionRegionPerPage
    {
        get => MaxVisionRegionsPerPage;
        set => MaxVisionRegionsPerPage = value;
    }
}
