namespace ELearnGamePlatform.Core.Entities;

public class DocumentCoverageChunk
{
    public int ChunkNumber { get; set; }
    public string ChunkId { get; set; } = string.Empty;
    public string Zone { get; set; } = "giua";
    public string Label { get; set; } = string.Empty;
    public string? HeadingKind { get; set; }
    public int? HeadingLevel { get; set; }
    public string? HeadingMarker { get; set; }
    public string? HeadingText { get; set; }
    public string? NormalizedHeading { get; set; }
    public string? HeadingPath { get; set; }
    public string? ParentHeadingPath { get; set; }
    public string? SectionKey { get; set; }
    public string CoverageZone { get; set; } = "giua";
    public bool IsPrimarySection { get; set; }
    public string Classification { get; set; } = ChunkClassifications.LessonContent;
    public int TeachabilityScore { get; set; } = 50;
    public int ChunkQualityScore { get; set; } = 50;
    public int EstimatedTokenCount { get; set; }
    public int TokenEfficiencyScore { get; set; }
    public int KeyFactDensityScore { get; set; }
    public List<string> PositiveSignals { get; set; } = new();
    public List<string> NegativeSignals { get; set; } = new();
    public string SelectionReason { get; set; } = string.Empty;
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }
    public int? SourcePageStart { get; set; }
    public int? SourcePageEnd { get; set; }
    public bool IsEligibleForQuestionGeneration { get; set; } = true;
    public List<string> Warnings { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public string EvidenceExcerpt { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();
    public List<string> ConceptAnchors { get; set; } = new();
    public string ChunkingReason { get; set; } = string.Empty;
    public List<string> KeyFacts { get; set; } = new();
    public string? Text { get; set; }
    public string? NormalizedText { get; set; }
    public int TextTokenCount { get; set; }
}
