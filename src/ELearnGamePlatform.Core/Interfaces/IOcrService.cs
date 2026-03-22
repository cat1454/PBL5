using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IOcrService
{
    Task<string> ExtractTextFromImageAsync(string imagePath, IProgress<DocumentProcessingProgressUpdate>? progress = null);
    Task<string> ExtractTextFromPdfScanAsync(string pdfPath, IProgress<DocumentProcessingProgressUpdate>? progress = null);
    Task<IReadOnlyDictionary<int, string>> ExtractTextFromPdfPagesAsync(
        string pdfPath,
        IReadOnlyCollection<int> pageNumbers,
        IProgress<DocumentProcessingProgressUpdate>? progress = null);
}
