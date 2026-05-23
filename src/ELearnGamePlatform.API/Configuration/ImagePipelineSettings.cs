namespace ELearnGamePlatform.API.Configuration;

public class ImagePipelineSettings
{
    public const string SectionName = "ImagePipeline";

    public bool Enabled { get; set; } = false;
    public string ModelTier { get; set; } = "low";
    public bool EnableLocalImageReview { get; set; } = false;
    public bool DownloadAssetsLocally { get; set; } = true;
    public string AssetStorageRoot { get; set; } = "uploads/slide-assets";
    public int MaxCandidatesToRerank { get; set; } = 8;
    public int MaxCandidatesToPersist { get; set; } = 4;
    public string PreferredAspectRatio { get; set; } = "16:9";
    public string LicensePolicy { get; set; } = "license-safe";

    public ImagePlanningSettings Planning { get; set; } = new();
    public ImageRerankSettings Rerank { get; set; } = new();
    public ImageReviewSettings Review { get; set; } = new();
    public ImageGenerationSettings Generation { get; set; } = new();
    public ImageWebSourceSettings WebSources { get; set; } = new();
}

public class ImagePlanningSettings
{
    public string Provider { get; set; } = "ollama";
    public string Model { get; set; } = "qwen2.5:7b";
    public double Temperature { get; set; } = 0.2;
    public int MaxPromptChars { get; set; } = 800;
    public int TimeoutSeconds { get; set; } = 90;
}

public class ImageRerankSettings
{
    public string Provider { get; set; } = "siglip2";
    public string Model { get; set; } = "google/siglip2-base-patch16-224";
    public int TopKBeforeReview { get; set; } = 8;
    public int FinalShortlistCount { get; set; } = 4;
    public bool PreferGpu { get; set; } = true;
}

public class ImageReviewSettings
{
    public string Provider { get; set; } = "ollama-vl";
    public string Model { get; set; } = "qwen2.5-vl:3b";
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxImagesPerSlide { get; set; } = 4;
    public int MaxParallelSlides { get; set; } = 1;
}

public class ImageGenerationSettings
{
    public string Provider { get; set; } = "openai";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-image-1.5";
    public bool UseOnlyAsFallback { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 120;
    public string Quality { get; set; } = "high";
    public string Size { get; set; } = "1536x1024";
}

public class ImageWebSourceSettings
{
    public bool Enabled { get; set; } = true;
    public int MaxResultsPerQuery { get; set; } = 20;
    public int MaxDownloadsPerSlide { get; set; } = 8;
    public double MinAcceptableScore { get; set; } = 0.86;
    public List<string> AllowedDomains { get; set; } = new();
}
