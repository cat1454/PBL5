using System.Text;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public class PdfProcessor : IDocumentProcessor, IDocumentInputQualityReportProvider
{
    private readonly ILogger<PdfProcessor> _logger;
    private readonly IOcrService _ocrService;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly OcrSettings _settings;
    private static readonly Regex WordRegex = new(@"\b[\p{L}\p{N}]+\b", RegexOptions.Compiled);

    public PdfProcessor(
        ILogger<PdfProcessor> logger,
        IOcrService ocrService,
        ITokenEstimator tokenEstimator,
        IOptions<OcrSettings> settings)
    {
        _logger = logger;
        _ocrService = ocrService;
        _tokenEstimator = tokenEstimator;
        _settings = settings.Value;
    }

    public DocumentInputQualityReport? LastInputQualityReport { get; private set; }

    public async Task<string> ExtractTextAsync(string filePath, string fileType, IProgress<DocumentProcessingProgressUpdate>? progress = null)
    {
        if (!SupportedFileType(fileType))
        {
            throw new NotSupportedException($"File type {fileType} is not supported by PdfProcessor");
        }

        try
        {
            progress?.Report(new DocumentProcessingProgressUpdate
            {
                Percent = 5,
                Stage = "reading-pdf",
                StageLabel = "Doc PDF",
                Message = "Dang quet tung trang PDF",
                Detail = "Kiem tra trang nao co text truc tiep va trang nao can OCR",
                StageIndex = 2,
                StageCount = 6
            });

            var extraction = ExtractDirectTextPerPage(filePath, progress);

            if (!extraction.PagesNeedingOcr.Any())
            {
                LastInputQualityReport = BuildInputQualityReport(extraction.TotalPages, extraction.PageReports);
                _logger.LogInformation("PDF extracted fully from embedded text without OCR: {FilePath}", filePath);
                progress?.Report(new DocumentProcessingProgressUpdate
                {
                    Percent = 100,
                    Stage = "reading-pdf",
                    StageLabel = "Doc PDF",
                    Message = "Da doc xong PDF bang text co san",
                    Detail = $"Da trich xuat text truc tiep tu {extraction.TotalPages}/{extraction.TotalPages} trang; avgQuality={LastInputQualityReport.AveragePageQuality}",
                    Current = extraction.TotalPages,
                    Total = extraction.TotalPages,
                    UnitLabel = "trang",
                    StageIndex = 2,
                    StageCount = 6
                });
                return BuildMergedPdfText(extraction.TotalPages, extraction.DirectTextByPage, new Dictionary<int, string>());
            }

            _logger.LogInformation(
                "PDF requires OCR for {OcrPageCount}/{TotalPages} pages: {FilePath}",
                extraction.PagesNeedingOcr.Count,
                extraction.TotalPages,
                filePath);

            progress?.Report(new DocumentProcessingProgressUpdate
            {
                Percent = 45,
                Stage = "switching-to-ocr",
                StageLabel = "OCR trang scan",
                Message = "Phat hien trang scan can OCR",
                Detail = $"Se OCR {extraction.PagesNeedingOcr.Count}/{extraction.TotalPages} trang con thieu text",
                Current = extraction.PagesNeedingOcr.Count,
                Total = extraction.TotalPages,
                UnitLabel = "trang",
                StageIndex = 2,
                StageCount = 6
            });

            var ocrProgress = new Progress<DocumentProcessingProgressUpdate>(update =>
            {
                progress?.Report(new DocumentProcessingProgressUpdate
                {
                    Percent = MapPercent(update.Percent, 48, 95),
                    Stage = update.Stage,
                    StageLabel = update.StageLabel,
                    Message = update.Message,
                    Detail = update.Detail,
                    Current = update.Current,
                    Total = update.Total,
                    UnitLabel = update.UnitLabel,
                    StageIndex = 2,
                    StageCount = 6
                });
            });

            var ocrResultsByPage = await _ocrService.ExtractPageResultsFromPdfPagesAsync(filePath, extraction.PagesNeedingOcr, progress: ocrProgress);
            var ocrTextByPage = ocrResultsByPage
                .Where(item => !string.IsNullOrWhiteSpace(item.Value.Text))
                .ToDictionary(item => item.Key, item => item.Value.Text);
            MergeOcrPageReports(extraction, ocrResultsByPage);
            if (_settings.EnableQualityProfile)
            {
                await RetryLowQualityOcrPagesAsync(filePath, extraction, ocrResultsByPage, ocrTextByPage, progress);
            }
            LastInputQualityReport = BuildInputQualityReport(extraction.TotalPages, extraction.PageReports);

            if (_settings.EnableQualityProfile)
            {
                _logger.LogInformation(
                    "PDF page quality report: total={TotalPages}, direct={DirectTextPages}, ocr={OcrPages}, empty={EmptyPages}, failed={FailedPages}, lowQuality={LowQualityPages}, avgQuality={AveragePageQuality}, tokens={EstimatedTokens}",
                    LastInputQualityReport.TotalPages,
                    LastInputQualityReport.DirectTextPages,
                    LastInputQualityReport.OcrPages,
                    LastInputQualityReport.EmptyPages,
                    LastInputQualityReport.FailedPages,
                    LastInputQualityReport.LowQualityPages,
                    LastInputQualityReport.AveragePageQuality,
                    LastInputQualityReport.TotalEstimatedTokens);

                progress?.Report(new DocumentProcessingProgressUpdate
                {
                    Percent = 100,
                    Stage = "ocr-quality-profile",
                    StageLabel = "Bao cao chat luong OCR",
                    Message = "Da tinh chat luong trich xuat tung trang",
                    Detail = $"direct={LastInputQualityReport.DirectTextPages}, ocr={LastInputQualityReport.OcrPages}, empty={LastInputQualityReport.EmptyPages}, failed={LastInputQualityReport.FailedPages}, lowQuality={LastInputQualityReport.LowQualityPages}",
                    Current = LastInputQualityReport.TotalPages,
                    Total = LastInputQualityReport.TotalPages,
                    UnitLabel = "trang",
                    StageIndex = 2,
                    StageCount = 6
                });
            }
            else
            {
                _logger.LogInformation(
                    "PDF extraction completed without quality profile: total={TotalPages}, direct={DirectTextPages}, ocr={OcrPages}, empty={EmptyPages}, failed={FailedPages}, tokens={EstimatedTokens}",
                    LastInputQualityReport.TotalPages,
                    LastInputQualityReport.DirectTextPages,
                    LastInputQualityReport.OcrPages,
                    LastInputQualityReport.EmptyPages,
                    LastInputQualityReport.FailedPages,
                    LastInputQualityReport.TotalEstimatedTokens);

                progress?.Report(new DocumentProcessingProgressUpdate
                {
                    Percent = 100,
                    Stage = "reading-pdf",
                    StageLabel = "Doc PDF",
                    Message = "Da doc xong PDF",
                    Detail = $"direct={LastInputQualityReport.DirectTextPages}, ocr={LastInputQualityReport.OcrPages}, empty={LastInputQualityReport.EmptyPages}, failed={LastInputQualityReport.FailedPages}",
                    Current = LastInputQualityReport.TotalPages,
                    Total = LastInputQualityReport.TotalPages,
                    UnitLabel = "trang",
                    StageIndex = 2,
                    StageCount = 6
                });
            }

            return BuildMergedPdfText(extraction.TotalPages, extraction.DirectTextByPage, ocrTextByPage);
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

    private DirectPdfExtractionResult ExtractDirectTextPerPage(string filePath, IProgress<DocumentProcessingProgressUpdate>? progress)
    {
        var directTextByPage = new Dictionary<int, string>();
        var pagesNeedingOcr = new List<int>();
        var pageReports = new Dictionary<int, DocumentPageProcessingReport>();

        using var document = PdfDocument.Open(filePath);
        var pages = document.GetPages().ToList();
        var totalPages = Math.Max(1, pages.Count);

        for (var index = 0; index < pages.Count; index++)
        {
            var pageNumber = index + 1;
            var pageText = NormalizeDirectPdfText(pages[index].Text);
            var hasUsableText = IsDirectTextUsable(pageText);

            if (hasUsableText)
            {
                directTextByPage[pageNumber] = pageText;
                pageReports[pageNumber] = BuildPageReport(pageNumber, DocumentPageProcessingMethods.DirectText, pageText);
            }
            else
            {
                pagesNeedingOcr.Add(pageNumber);
            }

            progress?.Report(new DocumentProcessingProgressUpdate
            {
                Percent = MapRange(index + 1, totalPages, 8, 40),
                Stage = "reading-pdf",
                StageLabel = "Doc PDF",
                Message = $"Dang kiem tra trang PDF {pageNumber}/{totalPages}",
                Detail = hasUsableText
                    ? $"Trang {pageNumber} dung text co san; quality={pageReports[pageNumber].QualityScore}, tokens={pageReports[pageNumber].EstimatedTokenCount}"
                    : $"Trang {pageNumber} gan nhu khong co text, dua vao hang OCR",
                Current = pageNumber,
                Total = totalPages,
                UnitLabel = "trang",
                StageIndex = 2,
                StageCount = 6
            });
        }

        return new DirectPdfExtractionResult
        {
            TotalPages = totalPages,
            DirectTextByPage = directTextByPage,
            PagesNeedingOcr = pagesNeedingOcr,
            PageReports = pageReports
        };
    }

    private void MergeOcrPageReports(
        DirectPdfExtractionResult extraction,
        IReadOnlyDictionary<int, OcrPageExtractionResult> ocrResultsByPage)
    {
        foreach (var pageNumber in extraction.PagesNeedingOcr)
        {
            if (!ocrResultsByPage.TryGetValue(pageNumber, out var ocrResult))
            {
                extraction.PageReports[pageNumber] = BuildPageReport(
                    pageNumber,
                    DocumentPageProcessingMethods.Failed,
                    string.Empty,
                    failureWarning: "OCR did not return a result for this page.");
                continue;
            }

            var method = string.IsNullOrWhiteSpace(ocrResult.Text)
                ? string.IsNullOrWhiteSpace(ocrResult.FailureReason)
                    ? DocumentPageProcessingMethods.Empty
                    : DocumentPageProcessingMethods.Failed
                : DocumentPageProcessingMethods.Ocr;

            extraction.PageReports[pageNumber] = BuildPageReport(
                pageNumber,
                method,
                ocrResult.Text,
                ocrResult.Confidence,
                ocrResult.SelectedVariant,
                ocrResult.SelectedPass,
                ocrResult.FailureReason);
        }
    }

    private DocumentPageProcessingReport BuildPageReport(
        int pageNumber,
        string method,
        string text,
        double? confidence = null,
        string? selectedVariant = null,
        string? selectedPass = null,
        string? failureWarning = null)
    {
        var normalized = TextCleanupUtility.NormalizeForAi(text, preserveLineBreaks: true);
        var charCount = normalized.Length;
        var wordCount = WordRegex.Matches(normalized).Count;
        var nonWhitespaceCount = normalized.Count(ch => !char.IsWhiteSpace(ch));
        var signalCount = normalized.Count(char.IsLetterOrDigit);
        var suspiciousCount = normalized.Count(IsSuspiciousCharacter);
        var shortTokenCount = WordRegex.Matches(normalized)
            .Select(match => match.Value)
            .Count(word => word.Length <= 2);
        var signalRatio = nonWhitespaceCount == 0 ? 0d : signalCount / (double)nonWhitespaceCount;
        var garbageRatio = nonWhitespaceCount == 0 ? 1d : suspiciousCount / (double)nonWhitespaceCount;
        var shortTokenRatio = wordCount == 0 ? 1d : shortTokenCount / (double)wordCount;
        var noiseScore = TextCleanupUtility.EstimateNoiseScore(normalized);
        var qualityScore = CalculatePageQualityScore(
            charCount,
            wordCount,
            signalRatio,
            garbageRatio,
            shortTokenRatio,
            noiseScore,
            confidence);
        var warnings = BuildPageWarnings(
            method,
            charCount,
            wordCount,
            signalRatio,
            garbageRatio,
            shortTokenRatio,
            noiseScore,
            qualityScore,
            failureWarning);

        return new DocumentPageProcessingReport
        {
            PageNumber = pageNumber,
            Method = method,
            CharCount = charCount,
            WordCount = wordCount,
            SignalRatio = Math.Round(signalRatio, 4),
            NoiseScore = noiseScore,
            EstimatedTokenCount = _tokenEstimator.EstimateTokens(normalized),
            Confidence = confidence.HasValue ? Math.Round(confidence.Value, 4) : null,
            QualityScore = qualityScore,
            SelectedVariant = selectedVariant,
            SelectedPass = selectedPass,
            Warnings = warnings
        };
    }

    private DocumentInputQualityReport BuildInputQualityReport(
        int totalPages,
        IReadOnlyDictionary<int, DocumentPageProcessingReport> reportsByPage)
    {
        var pageReports = Enumerable.Range(1, totalPages)
            .Select(pageNumber => reportsByPage.TryGetValue(pageNumber, out var report)
                ? report
                : BuildPageReport(pageNumber, DocumentPageProcessingMethods.Empty, string.Empty))
            .OrderBy(report => report.PageNumber)
            .ToList();
        var warnings = new List<string>();
        var lowQualityPages = pageReports
            .Where(report => report.QualityScore < _settings.MinAcceptablePageQuality)
            .Select(report => report.PageNumber)
            .ToList();

        if (lowQualityPages.Count > 0)
        {
            warnings.Add($"Low quality extraction detected on page(s): {string.Join(", ", lowQualityPages)}.");
        }

        if (pageReports.Any(report => string.Equals(report.Method, DocumentPageProcessingMethods.Failed, StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("One or more PDF pages failed extraction/OCR.");
        }

        return new DocumentInputQualityReport
        {
            TotalPages = totalPages,
            DirectTextPages = pageReports.Count(report => report.Method == DocumentPageProcessingMethods.DirectText),
            OcrPages = pageReports.Count(report => report.Method == DocumentPageProcessingMethods.Ocr),
            EmptyPages = pageReports.Count(report => report.Method == DocumentPageProcessingMethods.Empty),
            FailedPages = pageReports.Count(report => report.Method == DocumentPageProcessingMethods.Failed),
            LowQualityPages = lowQualityPages.Count,
            AveragePageQuality = pageReports.Count == 0 ? 0d : Math.Round(pageReports.Average(report => report.QualityScore), 2),
            TotalEstimatedTokens = pageReports.Sum(report => report.EstimatedTokenCount),
            Pages = pageReports,
            Warnings = warnings
                .Concat(pageReports.SelectMany(report => report.Warnings))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private async Task RetryLowQualityOcrPagesAsync(
        string filePath,
        DirectPdfExtractionResult extraction,
        IReadOnlyDictionary<int, OcrPageExtractionResult> initialOcrResultsByPage,
        Dictionary<int, string> ocrTextByPage,
        IProgress<DocumentProcessingProgressUpdate>? progress)
    {
        if (!_settings.EnableQualityProfile)
        {
            return;
        }

        var maxRetryPerPage = Math.Max(0, _settings.MaxRetryPerPage);
        if (maxRetryPerPage == 0)
        {
            return;
        }

        var pagesToRetry = extraction.PagesNeedingOcr
            .Where(pageNumber => extraction.PageReports.TryGetValue(pageNumber, out var report)
                && report.QualityScore < _settings.RetryThreshold)
            .ToList();

        if (pagesToRetry.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Retrying {PageCount} low-quality OCR page(s) below threshold {RetryThreshold} at {RetryPdfDpi} DPI: {Pages}",
            pagesToRetry.Count,
            _settings.RetryThreshold,
            _settings.RetryPdfDpi,
            string.Join(", ", pagesToRetry));

        progress?.Report(new DocumentProcessingProgressUpdate
        {
            Percent = 96,
            Stage = "ocr-adaptive-retry",
            StageLabel = "Thu lai OCR",
            Message = "Dang thu lai cac trang OCR chat luong thap",
            Detail = $"Retry {pagesToRetry.Count} trang duoi nguong {_settings.RetryThreshold} voi DPI {_settings.RetryPdfDpi}",
            Current = 0,
            Total = pagesToRetry.Count,
            UnitLabel = "trang",
            StageIndex = 2,
            StageCount = 6
        });

        for (var pageIndex = 0; pageIndex < pagesToRetry.Count; pageIndex++)
        {
            var pageNumber = pagesToRetry[pageIndex];
            var initialReport = extraction.PageReports[pageNumber];
            initialOcrResultsByPage.TryGetValue(pageNumber, out var initialOcrResult);
            var bestReport = initialReport;
            var bestOcrResult = initialOcrResult;
            var attempts = new List<DocumentPageOcrAttemptMetadata>
            {
                BuildAttemptMetadata("initial", initialReport, initialOcrResult)
            };

            _logger.LogInformation(
                "OCR retry starting for page {PageNumber}: initialQuality={InitialQuality}, threshold={RetryThreshold}, retryDpi={RetryPdfDpi}, maxRetry={MaxRetryPerPage}",
                pageNumber,
                initialReport.QualityScore,
                _settings.RetryThreshold,
                _settings.RetryPdfDpi,
                maxRetryPerPage);

            for (var retryIndex = 1; retryIndex <= maxRetryPerPage; retryIndex++)
            {
                var retryResults = await _ocrService.ExtractPageResultsFromPdfPagesAsync(
                    filePath,
                    new[] { pageNumber },
                    _settings.RetryPdfDpi);
                retryResults.TryGetValue(pageNumber, out var retryResult);
                var retryReport = BuildReportFromOcrResult(pageNumber, retryResult);
                attempts.Add(BuildAttemptMetadata($"retry-{retryIndex}", retryReport, retryResult));

                _logger.LogInformation(
                    "OCR retry attempt {RetryIndex} for page {PageNumber}: beforeQuality={BeforeQuality}, retryQuality={RetryQuality}, variant={Variant}, pass={Pass}",
                    retryIndex,
                    pageNumber,
                    initialReport.QualityScore,
                    retryReport.QualityScore,
                    retryReport.SelectedVariant ?? "none",
                    retryReport.SelectedPass ?? "none");

                if (retryReport.QualityScore > bestReport.QualityScore)
                {
                    bestReport = retryReport;
                    bestOcrResult = retryResult;
                }

                if (bestReport.QualityScore >= _settings.RetryThreshold)
                {
                    break;
                }
            }

            var selectedAttempt = attempts
                .OrderByDescending(attempt => attempt.QualityScore)
                .FirstOrDefault()?.Attempt ?? "initial";
            bestReport.OcrRetry = new DocumentPageOcrRetryMetadata
            {
                WasRetried = true,
                RetryThreshold = _settings.RetryThreshold,
                RetryPdfDpi = _settings.RetryPdfDpi,
                MaxRetryPerPage = maxRetryPerPage,
                InitialQualityScore = initialReport.QualityScore,
                SelectedQualityScore = bestReport.QualityScore,
                SelectedAttempt = selectedAttempt,
                Attempts = attempts
            };

            extraction.PageReports[pageNumber] = bestReport;
            if (bestOcrResult != null && !string.IsNullOrWhiteSpace(bestOcrResult.Text))
            {
                ocrTextByPage[pageNumber] = bestOcrResult.Text;
            }

            _logger.LogInformation(
                "OCR retry selected {SelectedAttempt} for page {PageNumber}: beforeQuality={BeforeQuality}, selectedQuality={SelectedQuality}",
                selectedAttempt,
                pageNumber,
                initialReport.QualityScore,
                bestReport.QualityScore);

            progress?.Report(new DocumentProcessingProgressUpdate
            {
                Percent = 96 + (int)Math.Round(((pageIndex + 1) / (double)pagesToRetry.Count) * 3d),
                Stage = "ocr-adaptive-retry",
                StageLabel = "Thu lai OCR",
                Message = $"Da retry OCR trang {pageNumber}",
                Detail = $"quality {initialReport.QualityScore}->{bestReport.QualityScore}; selected={selectedAttempt}",
                Current = pageIndex + 1,
                Total = pagesToRetry.Count,
                UnitLabel = "trang",
                StageIndex = 2,
                StageCount = 6
            });
        }
    }

    private DocumentPageProcessingReport BuildReportFromOcrResult(int pageNumber, OcrPageExtractionResult? ocrResult)
    {
        if (ocrResult == null)
        {
            return BuildPageReport(
                pageNumber,
                DocumentPageProcessingMethods.Failed,
                string.Empty,
                failureWarning: "OCR retry did not return a result for this page.");
        }

        var method = string.IsNullOrWhiteSpace(ocrResult.Text)
            ? string.IsNullOrWhiteSpace(ocrResult.FailureReason)
                ? DocumentPageProcessingMethods.Empty
                : DocumentPageProcessingMethods.Failed
            : DocumentPageProcessingMethods.Ocr;

        return BuildPageReport(
            pageNumber,
            method,
            ocrResult.Text,
            ocrResult.Confidence,
            ocrResult.SelectedVariant,
            ocrResult.SelectedPass,
            ocrResult.FailureReason);
    }

    private static DocumentPageOcrAttemptMetadata BuildAttemptMetadata(
        string attempt,
        DocumentPageProcessingReport report,
        OcrPageExtractionResult? ocrResult)
    {
        return new DocumentPageOcrAttemptMetadata
        {
            Attempt = attempt,
            PdfDpi = ocrResult?.PdfDpi,
            QualityScore = report.QualityScore,
            Confidence = report.Confidence,
            SelectedVariant = report.SelectedVariant,
            SelectedPass = report.SelectedPass,
            FailureReason = ocrResult?.FailureReason
        };
    }

    private int CalculatePageQualityScore(
        int charCount,
        int wordCount,
        double signalRatio,
        double garbageRatio,
        double shortTokenRatio,
        int noiseScore,
        double? confidence)
    {
        var score = 100;

        if (charCount == 0)
        {
            return 0;
        }

        if (charCount < 80)
        {
            score -= 35;
        }
        else if (charCount < 250)
        {
            score -= 12;
        }

        if (wordCount < 12)
        {
            score -= 35;
        }
        else if (wordCount < 40)
        {
            score -= 10;
        }

        score -= (int)Math.Round(Math.Clamp(0.70d - signalRatio, 0d, 0.70d) * 55);
        score -= (int)Math.Round(Math.Clamp(garbageRatio, 0d, 1d) * 40);
        score -= (int)Math.Round(Math.Clamp(shortTokenRatio - 0.35d, 0d, 1d) * 25);
        score -= Math.Min(35, noiseScore);

        if (confidence.HasValue)
        {
            score -= (int)Math.Round(Math.Clamp(0.78d - confidence.Value, 0d, 0.78d) * 25);
        }

        return Math.Clamp(score, 0, 100);
    }

    private List<string> BuildPageWarnings(
        string method,
        int charCount,
        int wordCount,
        double signalRatio,
        double garbageRatio,
        double shortTokenRatio,
        int noiseScore,
        int qualityScore,
        string? failureWarning)
    {
        var warnings = new List<string>();

        if (!string.IsNullOrWhiteSpace(failureWarning))
        {
            warnings.Add(failureWarning);
        }

        if (method == DocumentPageProcessingMethods.Empty || charCount == 0)
        {
            warnings.Add("Page has no extracted text.");
        }

        if (charCount > 0 && wordCount < 12)
        {
            warnings.Add("Page has very few recognized words.");
        }

        if (signalRatio < 0.55d)
        {
            warnings.Add("Page has low letter/digit signal ratio.");
        }

        if (garbageRatio > 0.25d)
        {
            warnings.Add("Page contains many suspicious OCR characters.");
        }

        if (shortTokenRatio > 0.50d)
        {
            warnings.Add("Page has many very short tokens, which can indicate OCR noise.");
        }

        if (noiseScore >= 20)
        {
            warnings.Add($"Page OCR noise heuristic reported noise score {noiseScore}.");
        }

        if (qualityScore < _settings.MinAcceptablePageQuality)
        {
            warnings.Add($"Page quality score is {qualityScore}/100.");
        }

        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string BuildMergedPdfText(
        int totalPages,
        IReadOnlyDictionary<int, string> directTextByPage,
        IReadOnlyDictionary<int, string> ocrTextByPage)
    {
        var combinedPages = new Dictionary<int, string>();

        for (var pageNumber = 1; pageNumber <= totalPages; pageNumber++)
        {
            if (ocrTextByPage.TryGetValue(pageNumber, out var ocrText) && !string.IsNullOrWhiteSpace(ocrText))
            {
                combinedPages[pageNumber] = ocrText.Trim();
                continue;
            }

            if (directTextByPage.TryGetValue(pageNumber, out var directText) && !string.IsNullOrWhiteSpace(directText))
            {
                combinedPages[pageNumber] = directText.Trim();
            }
        }

        var cleanedPages = TextCleanupUtility.RemoveRepeatedPageArtifacts(combinedPages);
        var builder = new StringBuilder();

        for (var pageNumber = 1; pageNumber <= totalPages; pageNumber++)
        {
            if (!cleanedPages.TryGetValue(pageNumber, out var pageText) || string.IsNullOrWhiteSpace(pageText))
            {
                continue;
            }

            builder.AppendLine($"[Page {pageNumber}]");
            builder.AppendLine(pageText);
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static bool IsDirectTextUsable(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var alphanumericCount = text.Count(char.IsLetterOrDigit);
        var wordCount = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var signalRatio = alphanumericCount / (double)Math.Max(1, text.Length);
        var noiseScore = TextCleanupUtility.EstimateNoiseScore(text);

        return alphanumericCount >= 40
            && wordCount >= 12
            && signalRatio >= 0.35d
            && noiseScore <= Math.Max(4, wordCount / 18);
    }

    private static string NormalizeDirectPdfText(string text)
    {
        return TextCleanupUtility.CleanPageText(text);
    }

    private static bool IsSuspiciousCharacter(char ch)
        => !char.IsLetterOrDigit(ch)
            && !char.IsWhiteSpace(ch)
            && ",.;:?!()[]\"'/%+-_:".IndexOf(ch) < 0;

    private static int MapRange(int current, int total, int startPercent, int endPercent)
    {
        if (total <= 0)
        {
            return endPercent;
        }

        var ratio = Math.Clamp(current / (double)total, 0d, 1d);
        return startPercent + (int)Math.Round((endPercent - startPercent) * ratio);
    }

    private static int MapPercent(int percent, int startPercent, int endPercent)
    {
        var ratio = Math.Clamp(percent, 0, 100) / 100d;
        return startPercent + (int)Math.Round((endPercent - startPercent) * ratio);
    }

    private sealed class DirectPdfExtractionResult
    {
        public int TotalPages { get; init; }
        public Dictionary<int, string> DirectTextByPage { get; init; } = new();
        public List<int> PagesNeedingOcr { get; init; } = new();
        public Dictionary<int, DocumentPageProcessingReport> PageReports { get; init; } = new();
    }
}
