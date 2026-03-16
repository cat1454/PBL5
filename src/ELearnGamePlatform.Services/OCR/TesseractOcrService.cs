using ELearnGamePlatform.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Tesseract;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using System.Diagnostics;
using System.Text;
using UglyToad.PdfPig;

namespace ELearnGamePlatform.Services.OCR;

public class TesseractOcrService : IOcrService
{
    private readonly ILogger<TesseractOcrService> _logger;
    private readonly string _tessDataPath;
    private readonly string _ocrLanguages;
    private readonly string _pdfToPpmPath;

    public TesseractOcrService(ILogger<TesseractOcrService> logger)
    {
        _logger = logger;
        _tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        if (!Directory.Exists(_tessDataPath))
        {
            _logger.LogWarning("Tesseract data path not found: {Path}. OCR may not work correctly.", _tessDataPath);
        }

        _ocrLanguages = ResolveOcrLanguages();
        _pdfToPpmPath = ResolvePdfToPpmPath();
    }

    public async Task<string> ExtractTextFromImageAsync(string imagePath)
    {
        try
        {
            var preprocessedPath = await PreprocessImageAsync(imagePath);

            using var engine = new TesseractEngine(_tessDataPath, _ocrLanguages, EngineMode.Default);
            using var img = Pix.LoadFromFile(preprocessedPath);
            using var page = engine.Process(img);
            
            var text = page.GetText();
            
            // Clean up preprocessed image if it's different from original
            if (preprocessedPath != imagePath && File.Exists(preprocessedPath))
            {
                File.Delete(preprocessedPath);
            }
            
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing OCR on image: {ImagePath}", imagePath);
            throw;
        }
    }

    public async Task<string> ExtractTextFromPdfScanAsync(string pdfPath)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"elearn_pdf_ocr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var pageCount = GetPdfPageCount(pdfPath);
            if (pageCount <= 0)
            {
                _logger.LogWarning("Could not detect page count for PDF: {PdfPath}", pdfPath);
                return string.Empty;
            }

            _logger.LogInformation("Starting scanned PDF OCR for {PageCount} pages: {PdfPath}", pageCount, pdfPath);

            var textBuilder = new StringBuilder();
            for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
            {
                var imagePath = await ConvertPdfPageToImageAsync(pdfPath, tempDirectory, pageNumber);
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    _logger.LogWarning("Could not convert page {PageNumber} to image for OCR", pageNumber);
                    continue;
                }

                var pageText = await ExtractTextFromImageAsync(imagePath);
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    textBuilder.AppendLine($"[Page {pageNumber}]");
                    textBuilder.AppendLine(pageText);
                    textBuilder.AppendLine();
                }
            }

            var extracted = textBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                _logger.LogInformation("Completed scanned PDF OCR. Extracted length: {Length}", extracted.Length);
                return extracted;
            }

            _logger.LogWarning("Scanned PDF OCR produced empty text. Ensure Poppler is available and image quality is sufficient. Current pdftoppm path: {PdfToPpmPath}", _pdfToPpmPath);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing OCR on scanned PDF: {PdfPath}", pdfPath);
            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to cleanup temporary OCR directory: {TempDirectory}", tempDirectory);
            }
        }
    }

    private static int GetPdfPageCount(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);
        return document.NumberOfPages;
    }

    private async Task<string?> ConvertPdfPageToImageAsync(string pdfPath, string tempDirectory, int pageNumber)
    {
        var outputPrefix = Path.Combine(tempDirectory, $"page_{pageNumber}");

        var startInfo = new ProcessStartInfo
        {
            FileName = _pdfToPpmPath,
            Arguments = $"-f {pageNumber} -l {pageNumber} -r 220 -png \"{pdfPath}\" \"{outputPrefix}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            var stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("pdftoppm failed for page {PageNumber} (exit code {ExitCode}): {Error}", pageNumber, process.ExitCode, stdErr);
                return null;
            }

            var generatedFile = Directory
                .GetFiles(tempDirectory, $"page_{pageNumber}*.png")
                .OrderBy(file => file)
                .FirstOrDefault();

            return generatedFile;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "pdftoppm is unavailable or failed. Checked executable: {PdfToPpmPath}", _pdfToPpmPath);
            return null;
        }
    }

    private string ResolveOcrLanguages()
    {
        var englishData = Path.Combine(_tessDataPath, "eng.traineddata");
        var vietnameseData = Path.Combine(_tessDataPath, "vie.traineddata");

        var hasEnglish = File.Exists(englishData);
        var hasVietnamese = File.Exists(vietnameseData);

        if (hasEnglish && hasVietnamese)
        {
            return "eng+vie";
        }

        if (hasEnglish)
        {
            _logger.LogWarning("vie.traineddata was not found in {TessDataPath}. OCR will fall back to English only.", _tessDataPath);
            return "eng";
        }

        if (hasVietnamese)
        {
            _logger.LogWarning("eng.traineddata was not found in {TessDataPath}. OCR will use Vietnamese only.", _tessDataPath);
            return "vie";
        }

        _logger.LogWarning("No OCR language packs were found in {TessDataPath}. Tesseract may fail until tessdata is added.", _tessDataPath);
        return "eng";
    }

    private string ResolvePdfToPpmPath()
    {
        var candidates = new List<string>
        {
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "poppler-25.12.0", "Library", "bin", "pdftoppm.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "poppler-25.12.0", "Library", "bin", "pdftoppm.exe"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                _logger.LogInformation("Using bundled pdftoppm executable at {PdfToPpmPath}", candidate);
                return candidate;
            }
        }

        return "pdftoppm";
    }

    private async Task<string> PreprocessImageAsync(string imagePath)
    {
        try
        {
            using var image = await Image.LoadAsync<Rgba32>(imagePath);

            image.Mutate(x => x
                .Grayscale()
                .Contrast(1.2f)
                .BinaryThreshold(0.5f)
            );

            var preprocessedPath = Path.Combine(
                Path.GetDirectoryName(imagePath) ?? "",
                $"preprocessed_{Path.GetFileName(imagePath)}"
            );

            await image.SaveAsync(preprocessedPath);
            return preprocessedPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error preprocessing image, using original: {ImagePath}", imagePath);
            return imagePath;
        }
    }
}
