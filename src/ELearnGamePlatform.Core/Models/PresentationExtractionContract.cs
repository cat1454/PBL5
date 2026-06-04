using System.Text.Json.Serialization;

namespace ELearnGamePlatform.Core.Models;

public class PresentationExtractionContract
{
    public const string CurrentVersion = "presentation-extraction-contract.v1";

    public string Version { get; set; } = CurrentVersion;
    public string SourceSummary { get; set; } = string.Empty;
    public PresentationAudienceProfile AudienceProfile { get; set; } = new();
    public PresentationFlowPlan PresentationFlow { get; set; } = new();
    public List<PresentationSectionPlan> SectionPlan { get; set; } = new();
    public List<PresentationSlideAffordance> SlideAffordances { get; set; } = new();
    public List<PresentationSourceGrounding> SourceGrounding { get; set; } = new();
    public List<PresentationVisualOpportunity> VisualOpportunities { get; set; } = new();
    public List<PresentationChartCandidate> ChartCandidates { get; set; } = new();
    public List<PresentationUxReviewHint> UxReviewHints { get; set; } = new();
    public PresentationImageIntent ImageIntent { get; set; } = new();
    public PresentationQualityMetrics QualityMetrics { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class PresentationAudienceProfile
{
    public string Level { get; set; } = "introductory";
    public List<string> PrerequisiteConcepts { get; set; } = new();
    public List<string> JargonTerms { get; set; } = new();
    public string ReadingDifficulty { get; set; } = "medium";
}

public class PresentationFlowPlan
{
    public string SuggestedOpening { get; set; } = string.Empty;
    public List<string> TransitionPoints { get; set; } = new();
    public List<string> RecapPoints { get; set; } = new();
    public List<PresentationSectionSlideMap> SectionToSlideMap { get; set; } = new();
}

public class PresentationSectionSlideMap
{
    public string SectionId { get; set; } = string.Empty;
    public string Heading { get; set; } = string.Empty;
    public int SuggestedSlideIndex { get; set; }
    public string SuggestedRole { get; set; } = "content";
}

public class PresentationSectionPlan
{
    public string SectionId { get; set; } = string.Empty;
    public string Heading { get; set; } = string.Empty;
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }
    public string Rhythm { get; set; } = "dense";
    public string TeachingRole { get; set; } = "explanation";
    public List<string> PreferredChunkIds { get; set; } = new();
    public string EvidenceSummary { get; set; } = string.Empty;
}

public class PresentationSlideAffordance
{
    public string SectionId { get; set; } = string.Empty;
    public int? PageNumber { get; set; }
    public string SuggestedLayout { get; set; } = "content";
    public string Rhythm { get; set; } = "dense";
    public string VisualRole { get; set; } = "none";
    public string? ChartIntent { get; set; }
    public string Density { get; set; } = "medium";
    public double SlideabilityScore { get; set; }
    public List<string> SuggestedQuickActions { get; set; } = new();
}

public class PresentationSourceGrounding
{
    public string SectionId { get; set; } = string.Empty;
    public List<string> ChunkIds { get; set; } = new();
    public List<int> PageNumbers { get; set; } = new();
    public double Confidence { get; set; }
    public string EvidenceExcerpt { get; set; } = string.Empty;
    public List<string> MissingEvidenceWarnings { get; set; } = new();
}

public class PresentationVisualOpportunity
{
    public int PageNumber { get; set; }
    public string SectionId { get; set; } = string.Empty;
    public string VisualRole { get; set; } = "conceptual";
    public string EvidenceText { get; set; } = string.Empty;
    public string ImageRendering { get; set; } = "vector-illustration";
    public string ImagePalette { get; set; } = "academic-blue";
    public bool NeedsReview { get; set; }
    public string? ReviewReason { get; set; }
}

public class PresentationChartCandidate
{
    public int PageNumber { get; set; }
    public string SectionId { get; set; } = string.Empty;
    public string ChartType { get; set; } = "chart";
    public string EvidenceText { get; set; } = string.Empty;
    public bool HasExplicitScale { get; set; }
    public bool HasNumericSeries { get; set; }
    public bool NeedsReview { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReviewReason { get; set; }
}

public class PresentationUxReviewHint
{
    public string Severity { get; set; } = "medium";
    public string HintType { get; set; } = "review";
    public int? PageNumber { get; set; }
    public string? SectionId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = string.Empty;
}

public class PresentationImageIntent
{
    public string ImageRendering { get; set; } = "vector-illustration";
    public string ImagePalette { get; set; } = "academic-blue";
    public List<string> PreferredVisualRoles { get; set; } = new();
}

public class PresentationQualityMetrics
{
    public int SectionCount { get; set; }
    public int VisualOpportunityCount { get; set; }
    public int ChartCandidateCount { get; set; }
    public int ReviewOnlyEvidenceCount { get; set; }
    public int UxReviewHintCount { get; set; }
    public int DenseSectionCount { get; set; }
    public double AverageSlideabilityScore { get; set; }
    public double ExtractionConfidence { get; set; }
}
