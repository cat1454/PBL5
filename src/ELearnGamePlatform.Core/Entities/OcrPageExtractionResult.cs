namespace ELearnGamePlatform.Core.Entities;

public class OcrPageExtractionResult
{
    public int PageNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public int? PdfDpi { get; set; }
    public string? SelectedVariant { get; set; }
    public string? SelectedPass { get; set; }
    public string? FailureReason { get; set; }
}
