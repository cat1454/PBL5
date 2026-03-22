namespace ELearnGamePlatform.Core.Entities;

/// <summary>
/// Represents analyzed content from a document
/// Used as a DTO for content analysis, not persisted directly to database
/// </summary>
public class ProcessedContent
{
    public List<string> MainTopics { get; set; } = new();
    public List<string> KeyPoints { get; set; } = new();
    public string? Summary { get; set; }
    public string? Language { get; set; }
    public List<DocumentCoverageChunk> CoverageMap { get; set; } = new();
}
