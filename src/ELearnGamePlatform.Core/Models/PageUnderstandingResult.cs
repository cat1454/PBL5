namespace ELearnGamePlatform.Core.Models;

public class PageUnderstandingResult
{
    public int PageNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<DocumentRegion> Regions { get; set; } = new();
}
