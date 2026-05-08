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
    Task<IReadOnlyDictionary<int, OcrPageExtractionResult>> ExtractPageResultsFromPdfPagesAsync(
        string pdfPath,
        IReadOnlyCollection<int> pageNumbers,
        int? pdfDpi = null,
        IProgress<DocumentProcessingProgressUpdate>? progress = null);
    Task<IReadOnlyDictionary<int, OcrPageExtractionResult>> ExtractPageResultsFromPdfPagesAsync(
        string pdfPath,
        IReadOnlyCollection<int> pageNumbers,
        OcrExtractionOptions options,
        int? pdfDpi = null,
        IProgress<DocumentProcessingProgressUpdate>? progress = null);
}
