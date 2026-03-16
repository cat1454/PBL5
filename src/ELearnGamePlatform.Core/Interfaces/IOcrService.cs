namespace ELearnGamePlatform.Core.Interfaces;

public interface IOcrService
{
    Task<string> ExtractTextFromImageAsync(string imagePath);
    Task<string> ExtractTextFromPdfScanAsync(string pdfPath);
}
