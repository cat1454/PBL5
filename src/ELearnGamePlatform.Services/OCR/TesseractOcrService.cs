using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tesseract;
using UglyToad.PdfPig;

namespace ELearnGamePlatform.Services.OCR;

public class TesseractOcrService : IOcrService
{
    private const int RescueVariantCount = 2;
    public const string ProfileOriginal = "original";
    public const string ProfileContrastEnhanced = "contrast-enhanced";
    public const string ProfileThresholdSoft = "threshold-soft";
    public const string ProfileBinaryStrong = "binary-strong";
    public const string ProfileCropBorder = "crop-border";
    public const string ProfileInvertedBinary = "inverted-binary";

    public static readonly IReadOnlyList<string> LowCostFallbackProfiles =
    [
        ProfileCropBorder,
        ProfileThresholdSoft,
        ProfileBinaryStrong,
        ProfileContrastEnhanced
    ];

    private readonly ILogger<TesseractOcrService> _logger;
    private readonly OcrSettings _settings;
    private readonly string _tessDataPath;
    private readonly string _ocrLanguages;
    private readonly string _pdfToPpmPath;
    private static readonly OcrPass[] PrimaryPasses =
    {
        new(PageSegMode.Auto, "auto", 0.08f),
        new(PageSegMode.SingleBlock, "single-block", 0.05f)
    };

    private static readonly OcrPass[] RescuePasses =
    {
        new(PageSegMode.SingleColumn, "single-column", 0.04f),
        new(PageSegMode.SparseText, "sparse-text", 0.02f)
    };

    public TesseractOcrService(ILogger<TesseractOcrService> logger, IOptions<OcrSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
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
        var pageResults = await ExtractPageResultsFromPdfPagesAsync(pdfPath, pageNumbers, progress: progress);
        return pageResults
            .Where(item => !string.IsNullOrWhiteSpace(item.Value.Text))
            .ToDictionary(item => item.Key, item => item.Value.Text);
    }

    public async Task<IReadOnlyDictionary<int, OcrPageExtractionResult>> ExtractPageResultsFromPdfPagesAsync(
        string pdfPath,
        IReadOnlyCollection<int> pageNumbers,
        int? pdfDpi = null,
        IProgress<DocumentProcessingProgressUpdate>? progress = null)
        => await ExtractPageResultsFromPdfPagesAsync(
            pdfPath,
            pageNumbers,
            new OcrExtractionOptions(),
            pdfDpi,
            progress);

    public async Task<IReadOnlyDictionary<int, OcrPageExtractionResult>> ExtractPageResultsFromPdfPagesAsync(
        string pdfPath,
        IReadOnlyCollection<int> pageNumbers,
        OcrExtractionOptions options,
        int? pdfDpi = null,
        IProgress<DocumentProcessingProgressUpdate>? progress = null)
    {
        options ??= new OcrExtractionOptions();
        var renderDpi = ResolvePdfDpi(pdfDpi);
        var orderedPages = pageNumbers
            .Where(page => page > 0)
            .Distinct()
            .OrderBy(page => page)
            .ToArray();

        if (orderedPages.Length == 0)
        {
            return new Dictionary<int, OcrPageExtractionResult>();
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"elearn_pdf_ocr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            _logger.LogInformation(
                "Starting OCR for {PageCount} selected PDF pages at {PdfDpi} DPI: {PdfPath}",
                orderedPages.Length,
                renderDpi,
                pdfPath);

            var results = new Dictionary<int, OcrPageExtractionResult>(orderedPages.Length);
            using var engine = CreateEngine(renderDpi);

            for (var index = 0; index < orderedPages.Length; index++)
            {
                var pageNumber = orderedPages[index];
                var pageStopwatch = Stopwatch.StartNew();
                ReportPdfPageProgress(
                    progress,
                    Math.Max(5, (int)Math.Round((index / (double)orderedPages.Length) * 100d)),
                    $"Dang OCR trang {pageNumber}",
                    $"Chuyen trang {pageNumber} thanh anh va nhan dang van ban",
                    index + 1,
                    orderedPages.Length);

                var imagePath = await ConvertPdfPageToImageAsync(pdfPath, tempDirectory, pageNumber, renderDpi);
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    pageStopwatch.Stop();
                    _logger.LogWarning("Could not convert page {PageNumber} to image for OCR", pageNumber);
                    results[pageNumber] = new OcrPageExtractionResult
                    {
                        PageNumber = pageNumber,
                        PdfDpi = renderDpi,
                        DurationMs = pageStopwatch.ElapsedMilliseconds,
                        FailureReason = "PDF page could not be converted to image for OCR."
                    };
                    continue;
                }

                var pageResult = await ExtractBestResultFromImageWithEngineAsync(imagePath, engine, options);
                pageStopwatch.Stop();
                if (pageResult != null)
                {
                    results[pageNumber] = new OcrPageExtractionResult
                    {
                        PageNumber = pageNumber,
                        Text = pageResult.Text,
                        Confidence = Math.Round(pageResult.Confidence, 4),
                        PdfDpi = renderDpi,
                        DurationMs = pageStopwatch.ElapsedMilliseconds,
                        SelectedVariant = pageResult.Variant,
                        SelectedPass = pageResult.PassName,
                        PreprocessingProfile = pageResult.Variant,
                        IsPreprocessingFallback = options.IsPreprocessingFallback
                    };
                }
                else
                {
                    results[pageNumber] = new OcrPageExtractionResult
                    {
                        PageNumber = pageNumber,
                        PdfDpi = renderDpi,
                        DurationMs = pageStopwatch.ElapsedMilliseconds,
                        FailureReason = "OCR produced no candidate text."
                    };
                }

                ReportPdfPageProgress(
                    progress,
                    Math.Max(8, (int)Math.Round(((index + 1) / (double)orderedPages.Length) * 100d)),
                    $"Da OCR xong trang {pageNumber}",
                    $"Da xu ly {index + 1}/{orderedPages.Length} trang can OCR",
                    index + 1,
                    orderedPages.Length);
            }

            if (!results.Any(item => !string.IsNullOrWhiteSpace(item.Value.Text)))
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

    private TesseractEngine CreateEngine(int? pdfDpi = null)
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

        TrySetEngineVariable(engine, "user_defined_dpi", ResolvePdfDpi(pdfDpi).ToString());
        TrySetEngineVariable(engine, "tessedit_do_invert", "0");

        return engine;
    }

    private async Task<string> ExtractTextFromImageWithEngineAsync(string imagePath, TesseractEngine engine)
    {
        var best = await ExtractBestResultFromImageWithEngineAsync(imagePath, engine);
        return best?.Text ?? string.Empty;
    }

    private async Task<OcrCandidateResult?> ExtractBestResultFromImageWithEngineAsync(
        string imagePath,
        TesseractEngine engine,
        OcrExtractionOptions? options = null)
    {
        var candidates = await BuildOcrCandidatesAsync(imagePath, options);

        try
        {
            var results = new List<OcrCandidateResult>();

            foreach (var candidate in candidates)
            {
                EvaluateCandidatePasses(candidate, engine, PrimaryPasses, results);
            }

            var best = GetBestResult(results);

            if (!IsReliableOcrResult(best))
            {
                foreach (var rescueCandidate in SelectRescueCandidates(candidates, results))
                {
                    EvaluateCandidatePasses(rescueCandidate, engine, RescuePasses, results);
                }

                best = GetBestResult(results);
            }

            _logger.LogDebug(
                "OCR image {ImagePath} selected variant {Variant} pass {PassName} with confidence {Confidence}",
                imagePath,
                best?.Variant ?? "none",
                best?.PassName ?? "none",
                best?.Confidence ?? 0f);

            return best;
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

    private async Task<string?> ConvertPdfPageToImageAsync(string pdfPath, string tempDirectory, int pageNumber, int pdfDpi)
    {
        var outputPrefix = Path.Combine(tempDirectory, $"page_{pageNumber}");

        var startInfo = new ProcessStartInfo
        {
            FileName = _pdfToPpmPath,
            Arguments = $"-f {pageNumber} -l {pageNumber} -r {pdfDpi} -png \"{pdfPath}\" \"{outputPrefix}\"",
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

    private int ResolvePdfDpi(int? pdfDpi)
        => Math.Clamp(pdfDpi ?? _settings.DefaultPdfDpi, 72, 600);

    private async Task<List<OcrCandidate>> BuildOcrCandidatesAsync(string imagePath, OcrExtractionOptions? options = null)
    {
        var requestedProfiles = ResolveRequestedProfiles(options);
        var candidates = new List<OcrCandidate>();

        if (requestedProfiles.Contains(ProfileOriginal))
        {
            candidates.Add(new() { Name = ProfileOriginal, Path = imagePath, DeleteAfterUse = false, ScoreBoost = 0f });
        }

        using var sourceImage = await Image.LoadAsync<Rgba32>(imagePath);
        var shouldTryInversion = LooksLikeDarkBackground(sourceImage);

        if (requestedProfiles.Contains(ProfileContrastEnhanced))
        {
            var contrastEnhanced = await CreatePreprocessedVariantAsync(
                sourceImage,
                imagePath,
                ProfileContrastEnhanced,
                brightness: 1.03f,
                contrast: 1.22f,
                sharpen: 0.75f,
                binaryThreshold: null);
            if (contrastEnhanced != null)
            {
                candidates.Add(contrastEnhanced);
            }
        }

        if (requestedProfiles.Contains(ProfileBinaryStrong))
        {
            var binaryStrong = await CreatePreprocessedVariantAsync(
                sourceImage,
                imagePath,
                ProfileBinaryStrong,
                brightness: 1.05f,
                contrast: 1.42f,
                sharpen: 0.95f,
                binaryThreshold: 0.61f);
            if (binaryStrong != null)
            {
                candidates.Add(binaryStrong);
            }
        }

        if (requestedProfiles.Contains(ProfileThresholdSoft))
        {
            var thresholdSoft = await CreatePreprocessedVariantAsync(
                sourceImage,
                imagePath,
                ProfileThresholdSoft,
                brightness: 1.02f,
                contrast: 1.28f,
                sharpen: 0.6f,
                binaryThreshold: 0.54f);
            if (thresholdSoft != null)
            {
                candidates.Add(thresholdSoft);
            }
        }

        if (requestedProfiles.Contains(ProfileCropBorder))
        {
            var croppedBorder = await CreateCropBorderVariantAsync(sourceImage, imagePath);
            if (croppedBorder != null)
            {
                candidates.Add(croppedBorder);
            }
        }

        if (shouldTryInversion && requestedProfiles.Contains(ProfileInvertedBinary))
        {
            var inverted = await CreatePreprocessedVariantAsync(
                sourceImage,
                imagePath,
                ProfileInvertedBinary,
                brightness: 1.08f,
                contrast: 1.34f,
                sharpen: 0.8f,
                binaryThreshold: 0.56f,
                invert: true);
            if (inverted != null)
            {
                candidates.Add(inverted);
            }
        }

        if (candidates.Count == 0)
        {
            candidates.Add(new() { Name = ProfileOriginal, Path = imagePath, DeleteAfterUse = false, ScoreBoost = 0f });
        }

        return candidates;
    }

    private static HashSet<string> ResolveRequestedProfiles(OcrExtractionOptions? options)
    {
        if (options?.PreprocessingProfiles == null || options.PreprocessingProfiles.Count == 0)
        {
            return new HashSet<string>(
                new[]
                {
                    ProfileOriginal,
                    ProfileContrastEnhanced,
                    ProfileBinaryStrong,
                    ProfileThresholdSoft,
                    ProfileCropBorder,
                    ProfileInvertedBinary
                },
                StringComparer.OrdinalIgnoreCase);
        }

        return options.PreprocessingProfiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile))
            .Select(profile => profile.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<OcrCandidate?> CreatePreprocessedVariantAsync(
        Image<Rgba32> sourceImage,
        string imagePath,
        string variantName,
        float brightness,
        float contrast,
        float sharpen,
        float? binaryThreshold,
        bool invert = false)
    {
        try
        {
            using var image = sourceImage.Clone();
            var targetWidth = ResolveTargetWidth(image.Width);
            var shouldUpscale = targetWidth > image.Width;
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
                if (invert)
                {
                    context.Invert();
                }

                context.Brightness(brightness);
                context.Contrast(contrast);
                if (sharpen > 0f)
                {
                    context.GaussianSharpen(sharpen);
                }

                if (binaryThreshold.HasValue)
                {
                    context.BinaryThreshold(binaryThreshold.Value);
                }
            });

            var preprocessedPath = Path.Combine(
                Path.GetDirectoryName(imagePath) ?? string.Empty,
                $"{variantName}_{Guid.NewGuid():N}_{Path.GetFileNameWithoutExtension(imagePath)}.png");

            await image.SaveAsync(preprocessedPath);
            return new OcrCandidate
            {
                Name = variantName,
                Path = preprocessedPath,
                DeleteAfterUse = true,
                ScoreBoost = (binaryThreshold.HasValue ? 0.06f : 0.03f) + (invert ? 0.01f : 0f)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error preprocessing image variant {VariantName}, skipping: {ImagePath}", variantName, imagePath);
            return null;
        }
    }

    private async Task<OcrCandidate?> CreateCropBorderVariantAsync(Image<Rgba32> sourceImage, string imagePath)
    {
        try
        {
            using var image = sourceImage.Clone();
            var cropRectangle = DetectContentBounds(image);
            if (cropRectangle.Width >= image.Width * 0.98 && cropRectangle.Height >= image.Height * 0.98)
            {
                return null;
            }

            image.Mutate(context =>
            {
                context.AutoOrient();
                context.Crop(cropRectangle);
                context.Grayscale();
                context.Brightness(1.02f);
                context.Contrast(1.18f);
                context.GaussianSharpen(0.45f);
            });

            var preprocessedPath = Path.Combine(
                Path.GetDirectoryName(imagePath) ?? string.Empty,
                $"{ProfileCropBorder}_{Guid.NewGuid():N}_{Path.GetFileNameWithoutExtension(imagePath)}.png");

            await image.SaveAsync(preprocessedPath);
            return new OcrCandidate
            {
                Name = ProfileCropBorder,
                Path = preprocessedPath,
                DeleteAfterUse = true,
                ScoreBoost = 0.025f
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error preprocessing image variant {VariantName}, skipping: {ImagePath}", ProfileCropBorder, imagePath);
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
        var averageWordLength = words.Length == 0 ? 0f : (float)words.Average(word => word.Length);
        var alphaNumeric = text.Count(char.IsLetterOrDigit);
        var signalRatio = alphaNumeric / (float)Math.Max(1, text.Length);
        var noiseScore = TextCleanupUtility.EstimateNoiseScore(text);
        var longTokenBonus = words.Count(word => word.Length >= 4) * 0.35f;
        var penalty = (noiseScore * 7.5f) + (Math.Max(0f, 3.6f - averageWordLength) * 5f);

        return words.Length
            + (text.Length * 0.02f)
            + (confidence * 100f)
            + (signalRatio * 35f)
            + longTokenBonus
            + scoreBoost
            - penalty;
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

    private sealed class OcrPass
    {
        public OcrPass(PageSegMode mode, string name, float scoreBoost)
        {
            Mode = mode;
            Name = name;
            ScoreBoost = scoreBoost;
        }

        public PageSegMode Mode { get; }
        public string Name { get; }
        public float ScoreBoost { get; }
    }

    private sealed class OcrCandidateResult
    {
        public string Text { get; init; } = string.Empty;
        public float Score { get; init; }
        public string Variant { get; init; } = string.Empty;
        public string VariantPath { get; init; } = string.Empty;
        public string PassName { get; init; } = string.Empty;
        public float Confidence { get; init; }
    }

    private void EvaluateCandidatePasses(
        OcrCandidate candidate,
        TesseractEngine engine,
        IReadOnlyCollection<OcrPass> passes,
        List<OcrCandidateResult> results)
    {
        using var img = Pix.LoadFromFile(candidate.Path);

        foreach (var pass in passes)
        {
            engine.DefaultPageSegMode = pass.Mode;
            using var page = engine.Process(img);

            var text = NormalizeExtractedText(page.GetText());
            var confidence = page.GetMeanConfidence();
            var score = ScoreOcrText(text, confidence, candidate.ScoreBoost + pass.ScoreBoost);
            results.Add(new OcrCandidateResult
            {
                Text = text,
                Score = score,
                Variant = candidate.Name,
                VariantPath = candidate.Path,
                PassName = pass.Name,
                Confidence = confidence
            });
        }
    }

    private static OcrCandidateResult? GetBestResult(IEnumerable<OcrCandidateResult> results)
    {
        return results
            .OrderByDescending(result => result.Score)
            .FirstOrDefault();
    }

    private static bool IsReliableOcrResult(OcrCandidateResult? result)
    {
        if (result == null || string.IsNullOrWhiteSpace(result.Text))
        {
            return false;
        }

        var words = result.Text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var noiseScore = TextCleanupUtility.EstimateNoiseScore(result.Text);
        return words.Length >= 18
            && result.Confidence >= 0.72f
            && noiseScore <= Math.Max(2, words.Length / 35);
    }

    private static IEnumerable<OcrCandidate> SelectRescueCandidates(
        IEnumerable<OcrCandidate> candidates,
        IEnumerable<OcrCandidateResult> results)
    {
        var selectedPaths = results
            .OrderByDescending(result => result.Score)
            .Select(result => result.VariantPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(RescueVariantCount)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return candidates.Where(candidate => selectedPaths.Contains(candidate.Path)).ToList();
    }

    private static int ResolveTargetWidth(int width)
    {
        if (width < 1100)
        {
            return 2600;
        }

        if (width < 1800)
        {
            return 2200;
        }

        return width;
    }

    private static bool LooksLikeDarkBackground(Image<Rgba32> image)
    {
        var stepX = Math.Max(1, image.Width / 48);
        var stepY = Math.Max(1, image.Height / 48);
        double brightnessTotal = 0;
        var samples = 0;

        for (var y = 0; y < image.Height; y += stepY)
        {
            for (var x = 0; x < image.Width; x += stepX)
            {
                var pixel = image[x, y];
                brightnessTotal += ((0.2126d * pixel.R) + (0.7152d * pixel.G) + (0.0722d * pixel.B)) / 255d;
                samples++;
            }
        }

        return samples > 0 && (brightnessTotal / samples) < 0.45d;
    }

    private static Rectangle DetectContentBounds(Image<Rgba32> image)
    {
        var minX = image.Width;
        var minY = image.Height;
        var maxX = 0;
        var maxY = 0;
        var step = Math.Max(1, Math.Min(image.Width, image.Height) / 900);

        for (var y = 0; y < image.Height; y += step)
        {
            for (var x = 0; x < image.Width; x += step)
            {
                var pixel = image[x, y];
                var brightness = ((0.2126d * pixel.R) + (0.7152d * pixel.G) + (0.0722d * pixel.B)) / 255d;
                if (brightness > 0.94d)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (minX >= maxX || minY >= maxY)
        {
            return new Rectangle(0, 0, image.Width, image.Height);
        }

        var paddingX = Math.Max(8, image.Width / 100);
        var paddingY = Math.Max(8, image.Height / 100);
        var left = Math.Max(0, minX - paddingX);
        var top = Math.Max(0, minY - paddingY);
        var right = Math.Min(image.Width - 1, maxX + paddingX);
        var bottom = Math.Min(image.Height - 1, maxY + paddingY);

        return new Rectangle(left, top, Math.Max(1, right - left + 1), Math.Max(1, bottom - top + 1));
    }

    private void TrySetEngineVariable(TesseractEngine engine, string name, string value)
    {
        try
        {
            engine.SetVariable(name, value);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not set Tesseract variable {VariableName}.", name);
        }
    }
}
