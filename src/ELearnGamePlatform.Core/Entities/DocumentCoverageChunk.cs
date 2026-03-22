namespace ELearnGamePlatform.Core.Entities;

public class DocumentCoverageChunk
{
    public int ChunkNumber { get; set; }
    public string ChunkId { get; set; } = string.Empty;
    public string Zone { get; set; } = "giua";
    public string Label { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string EvidenceExcerpt { get; set; } = string.Empty;
    public List<string> KeyFacts { get; set; } = new();
}
