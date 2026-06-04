using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Models;

public class DocumentUnderstandingResult
{
    public int DocumentId { get; set; }
    public string CombinedText { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Status { get; set; } = "LegacyPassthrough";
    public DocumentQualityScoreResult? Quality { get; set; }
    public List<PageUnderstandingResult> Pages { get; set; } = new();
    public List<DocumentRegion> Regions { get; set; } = new();
    public PresentationExtractionContract? PresentationContract { get; set; }
    public List<string> Warnings { get; set; } = new();
}
