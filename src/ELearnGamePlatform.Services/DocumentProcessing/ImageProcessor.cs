using ELearnGamePlatform.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public class ImageProcessor : IDocumentProcessor
{
    private readonly ILogger<ImageProcessor> _logger;
    private readonly IOcrService _ocrService;

    public ImageProcessor(ILogger<ImageProcessor> logger, IOcrService ocrService)
    {
        _logger = logger;
        _ocrService = ocrService;
    }

    public async Task<string> ExtractTextAsync(string filePath, string fileType)
    {
        if (!SupportedFileType(fileType))
        {
            throw new NotSupportedException($"File type {fileType} is not supported by ImageProcessor");
        }

        try
        {
            _logger.LogInformation("Extracting text from image using OCR: {FilePath}", filePath);
            var text = await _ocrService.ExtractTextFromImageAsync(filePath);
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from image: {FilePath}", filePath);
            throw;
        }
    }

    public bool SupportedFileType(string fileType)
    {
        var supportedTypes = new[] { "png", "jpg", "jpeg", ".png", ".jpg", ".jpeg" };
        return supportedTypes.Contains(fileType.ToLowerInvariant());
    }
}
