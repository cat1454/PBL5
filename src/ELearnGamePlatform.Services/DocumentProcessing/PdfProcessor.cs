using ELearnGamePlatform.Core.Interfaces;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public class PdfProcessor : IDocumentProcessor
{
    private readonly ILogger<PdfProcessor> _logger;
    private readonly IOcrService _ocrService;

    public PdfProcessor(ILogger<PdfProcessor> logger, IOcrService ocrService)
    {
        _logger = logger;
        _ocrService = ocrService;
    }

    public async Task<string> ExtractTextAsync(string filePath, string fileType)
    {
        if (!SupportedFileType(fileType))
        {
            throw new NotSupportedException($"File type {fileType} is not supported by PdfProcessor");
        }

        try
        {
            // First, try to extract text directly (for text-based PDFs)
            var textContent = ExtractTextFromPdf(filePath);

            // If extracted text is too short, the PDF might be scanned
            if (string.IsNullOrWhiteSpace(textContent) || textContent.Length < 100)
            {
                _logger.LogInformation("PDF appears to be scanned, using OCR");
                textContent = await _ocrService.ExtractTextFromPdfScanAsync(filePath);
            }

            return textContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from PDF: {FilePath}", filePath);
            throw;
        }
    }

    public bool SupportedFileType(string fileType)
    {
        return fileType.Equals("pdf", StringComparison.OrdinalIgnoreCase) ||
               fileType.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private string ExtractTextFromPdf(string filePath)
    {
        var textBuilder = new System.Text.StringBuilder();

        using (var document = PdfDocument.Open(filePath))
        {
            foreach (var page in document.GetPages())
            {
                var pageText = page.Text;
                textBuilder.AppendLine(pageText);
                textBuilder.AppendLine(); // Add line break between pages
            }
        }

        return textBuilder.ToString();
    }
}
