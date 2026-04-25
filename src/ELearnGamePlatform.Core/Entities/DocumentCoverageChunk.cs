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
    public bool IsPrimarySection { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string EvidenceExcerpt { get; set; } = string.Empty;
    public List<string> KeyFacts { get; set; } = new();
}
