using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Utilities;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tesseract;
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

    public async Task<string> ExtractTextFromImageAsync(string imagePath, IProgress<DocumentProcessingProgressUpdate>? progress = null)
    {
        try
        {
            ReportImageOcrProgress(progress, 10, "Dang tien xu ly hinh anh", "Canh chinh, lam ro va tang tuong phan cho anh dau vao");

            using var engine = CreateEngine();

            ReportImageOcrProgress(progress, 50, "Dang chay OCR tren hinh anh", "Nhan dang van ban tu anh da tien xu ly");
            var text = await ExtractTextFromImageWithEngineAsync(imagePath, engine);

            ReportImageOcrProgress(progress, 100, "Da OCR xong hinh anh", $"Da trich xuat {text.Length} ky tu tu hinh anh");
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing OCR on image: {ImagePath}", imagePath);
            throw;
        }
    }

    public async Task<string> ExtractTextFromPdfScanAsync(string pdfPath, IProgress<DocumentProcessingProgressUpdate>? progress = null)
    {
        var pageCount = GetPdfPageCount(pdfPath);
        if (pageCount <= 0)
        {
            _logger.LogWarning("Could not detect page count for PDF: {PdfPath}", pdfPath);
            return string.Empty;
        }

        var pageNumbers = Enumerable.Range(1, pageCount).ToArray();
        var pageTexts = await ExtractTextFromPdfPagesAsync(pdfPath, pageNumbers, progress);
        return RenderPageOrderedText(pageTexts);
    }

    public async Task<IReadOnlyDictionary<int, string>> ExtractTextFromPdfPagesAsync(
        string pdfPath,
        IReadOnlyCollection<int> pageNumbers,
        IProgress<DocumentProcessingProgressUpdate>? progress = null)
    {
        var orderedPages = pageNumbers
            .Where(page => page > 0)
            .Distinct()
            .OrderBy(page => page)
            .ToArray();

        if (orderedPages.Length == 0)
        {
            return new Dictionary<int, string>();
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"elearn_pdf_ocr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            _logger.LogInformation("Starting OCR for {PageCount} selected PDF pages: {PdfPath}", orderedPages.Length, pdfPath);

            var results = new Dictionary<int, string>(orderedPages.Length);
            using var engine = CreateEngine();

            for (var index = 0; index < orderedPages.Length; index++)
            {
                var pageNumber = orderedPages[index];
                ReportPdfPageProgress(
                    progress,
                    Math.Max(5, (int)Math.Round((index / (double)orderedPages.Length) * 100d)),
                    $"Dang OCR trang {pageNumber}",
                    $"Chuyen trang {pageNumber} thanh anh va nhan dang van ban",
                    index + 1,
                    orderedPages.Length);

                var imagePath = await ConvertPdfPageToImageAsync(pdfPath, tempDirectory, pageNumber);
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    _logger.LogWarning("Could not convert page {PageNumber} to image for OCR", pageNumber);
                    continue;
                }

                var pageText = await ExtractTextFromImageWithEngineAsync(imagePath, engine);
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    results[pageNumber] = pageText;
                }

                ReportPdfPageProgress(
                    progress,
                    Math.Max(8, (int)Math.Round(((index + 1) / (double)orderedPages.Length) * 100d)),
                    $"Da OCR xong trang {pageNumber}",
                    $"Da xu ly {index + 1}/{orderedPages.Length} trang can OCR",
                    index + 1,
                    orderedPages.Length);
            }

            if (!results.Any())
            {
                _logger.LogWarning("Selected PDF page OCR produced empty text. Ensure Poppler and image quality are sufficient. Current pdftoppm path: {PdfToPpmPath}", _pdfToPpmPath);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing OCR on selected PDF pages: {PdfPath}", pdfPath);
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

    private TesseractEngine CreateEngine()
    {
        var engine = new TesseractEngine(_tessDataPath, _ocrLanguages, EngineMode.Default);
        engine.DefaultPageSegMode = PageSegMode.Auto;

        try
        {
            engine.SetVariable("preserve_interword_spaces", "1");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not set preserve_interword_spaces on Tesseract engine.");
        }

        return engine;
    }

    private async Task<string> ExtractTextFromImageWithEngineAsync(string imagePath, TesseractEngine engine)
    {
        var candidates = await BuildOcrCandidatesAsync(imagePath);

        try
        {
            OcrCandidateResult? best = null;

            foreach (var candidate in candidates)
            {
                using var img = Pix.LoadFromFile(candidate.Path);
                using var page = engine.Process(img);

                var text = NormalizeExtractedText(page.GetText());
                var confidence = page.GetMeanConfidence();
                var score = ScoreOcrText(text, confidence, candidate.ScoreBoost);

                if (best == null || score > best.Score)
                {
                    best = new OcrCandidateResult
                    {
                        Text = text,
                        Score = score,
                        Variant = candidate.Name,
                        Confidence = confidence
                    };
                }
            }

            _logger.LogDebug(
                "OCR image {ImagePath} selected variant {Variant} with confidence {Confidence}",
                imagePath,
                best?.Variant ?? "none",
                best?.Confidence ?? 0f);

            return best?.Text ?? string.Empty;
        }
        finally
        {
            foreach (var candidate in candidates.Where(candidate => candidate.DeleteAfterUse))
            {
                if (File.Exists(candidate.Path))
                {
                    File.Delete(candidate.Path);
                }
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

            return Directory
                .GetFiles(tempDirectory, $"page_{pageNumber}*.png")
                .OrderBy(file => file)
                .FirstOrDefault();
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

    private async Task<List<OcrCandidate>> BuildOcrCandidatesAsync(string imagePath)
    {
        var candidates = new List<OcrCandidate>
        {
            new() { Name = "original", Path = imagePath, DeleteAfterUse = false, ScoreBoost = 0f }
        };

        var grayscale = await CreatePreprocessedVariantAsync(imagePath, "grayscale", contrast: 1.2f, binaryThreshold: null);
        if (grayscale != null)
        {
            candidates.Add(grayscale);
        }

        var binary = await CreatePreprocessedVariantAsync(imagePath, "binary", contrast: 1.35f, binaryThreshold: 0.62f);
        if (binary != null)
        {
            candidates.Add(binary);
        }

        return candidates;
    }

    private async Task<OcrCandidate?> CreatePreprocessedVariantAsync(
        string imagePath,
        string variantName,
        float contrast,
        float? binaryThreshold)
    {
        try
        {
            using var image = await Image.LoadAsync<Rgba32>(imagePath);

            var shouldUpscale = image.Width < 1800;
            var targetWidth = shouldUpscale ? 1800 : image.Width;
            var targetHeight = shouldUpscale
                ? Math.Max(1, (int)Math.Round(image.Height * (targetWidth / (double)image.Width)))
                : image.Height;

            image.Mutate(context =>
            {
                context.AutoOrient();

                if (shouldUpscale)
                {
                    context.Resize(targetWidth, targetHeight);
                }

                context.Grayscale();
                context.Contrast(contrast);
                if (binaryThreshold.HasValue)
                {
                    context.BinaryThreshold(binaryThreshold.Value);
                }
            });

            var preprocessedPath = Path.Combine(
                Path.GetDirectoryName(imagePath) ?? string.Empty,
                $"{variantName}_{Path.GetFileNameWithoutExtension(imagePath)}.png");

            await image.SaveAsync(preprocessedPath);
            return new OcrCandidate
            {
                Name = variantName,
                Path = preprocessedPath,
                DeleteAfterUse = true,
                ScoreBoost = binaryThreshold.HasValue ? 0.05f : 0.02f
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error preprocessing image variant {VariantName}, skipping: {ImagePath}", variantName, imagePath);
            return null;
        }
    }

    private static string RenderPageOrderedText(IReadOnlyDictionary<int, string> pageTexts)
    {
        var cleanedPages = TextCleanupUtility.RemoveRepeatedPageArtifacts(pageTexts);
        var builder = new StringBuilder();

        foreach (var page in cleanedPages.OrderBy(item => item.Key))
        {
            if (string.IsNullOrWhiteSpace(page.Value))
            {
                continue;
            }

            builder.AppendLine($"[Page {page.Key}]");
            builder.AppendLine(page.Value.Trim());
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeExtractedText(string text)
    {
        return TextCleanupUtility.CleanPageText(text);
    }

    private static float ScoreOcrText(string text, float confidence, float scoreBoost)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return -1000f;
        }

        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var alphaNumeric = text.Count(char.IsLetterOrDigit);
        var signalRatio = alphaNumeric / (float)Math.Max(1, text.Length);
        return words.Length + (text.Length * 0.02f) + (confidence * 100f) + (signalRatio * 35f) + scoreBoost;
    }

    private static void ReportImageOcrProgress(
        IProgress<DocumentProcessingProgressUpdate>? progress,
        int percent,
        string message,
        string detail)
    {
        progress?.Report(new DocumentProcessingProgressUpdate
        {
            Percent = percent,
            Stage = "ocr-image",
            StageLabel = "OCR hinh anh",
            Message = message,
            Detail = detail,
            StageIndex = 2,
            StageCount = 6
        });
    }

    private static void ReportPdfPageProgress(
        IProgress<DocumentProcessingProgressUpdate>? progress,
        int percent,
        string message,
        string detail,
        int current,
        int total)
    {
        progress?.Report(new DocumentProcessingProgressUpdate
        {
            Percent = percent,
            Stage = "ocr-pdf-pages",
            StageLabel = "OCR trang scan",
            Message = message,
            Detail = detail,
            Current = current,
            Total = total,
            UnitLabel = "trang",
            StageIndex = 2,
            StageCount = 6
        });
    }

    private sealed class OcrCandidate
    {
        public string Name { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public bool DeleteAfterUse { get; init; }
        public float ScoreBoost { get; init; }
    }

    private sealed class OcrCandidateResult
    {
        public string Text { get; init; } = string.Empty;
        public float Score { get; init; }
        public string Variant { get; init; } = string.Empty;
        public float Confidence { get; init; }
    }
}
