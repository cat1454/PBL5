namespace ELearnGamePlatform.Core.Entities;

using ELearnGamePlatform.Core.Models;

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
    public string DocumentType { get; set; } = DocumentTypes.Unknown;
    public string? Title { get; set; }
    public int? MainContentStartPage { get; set; }
    public List<DocumentSectionDescriptor> Structure { get; set; } = new();
    public List<ExcludedContentDescriptor> ExcludedContent { get; set; } = new();
    public List<DocumentCoverageChunk> CoverageMap { get; set; } = new();
    public PresentationExtractionContract? PresentationContract { get; set; }
}
