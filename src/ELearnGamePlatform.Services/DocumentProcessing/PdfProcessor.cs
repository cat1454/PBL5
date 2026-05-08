using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Utilities;
using ELearnGamePlatform.Services.OCR;
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
    private static readonly Regex SentenceRegex = new(@"[.!?;:]\s+|\n", RegexOptions.Compiled);
    private static readonly Regex VietnameseDiacriticRegex = new(@"[ăâđêôơưáàảãạấầẩẫậắằẳẵặéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

            var ocrResultsByPage = await _ocrService.ExtractPageResultsFromPdfPagesAsync(
                filePath,
                extraction.PagesNeedingOcr,
                new OcrExtractionOptions
                {
                    PreprocessingProfiles = new[] { TesseractOcrService.ProfileOriginal }
                },
                progress: ocrProgress);
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
        string? failureWarning = null,
        IReadOnlyCollection<string>? preprocessingSkipReasons = null)
    {
        var normalized = NormalizeExtractedTextForQuality(text);
        var charCount = normalized.Length;
        var wordCount = WordRegex.Matches(normalized).Count;
        var nonWhitespaceCount = normalized.Count(ch => !char.IsWhiteSpace(ch));
        var signalCount = normalized.Count(char.IsLetterOrDigit);
        var suspiciousCount = normalized.Count(IsSuspiciousCharacter);
        var symbolCount = normalized.Count(ch => !char.IsLetterOrDigit(ch) && !char.IsWhiteSpace(ch));
        var replacementCharacterCount = normalized.Count(ch => ch == '\uFFFD');
        var shortTokenCount = WordRegex.Matches(normalized)
            .Select(match => match.Value)
            .Count(word => word.Length <= 2);
        var lines = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
        var digitOnlyLineCount = lines.Count(line => Regex.IsMatch(line, @"^\d{1,4}$"));
        var footnoteLineCount = lines.Count(LooksLikeFootnoteLine);
        var signalRatio = nonWhitespaceCount == 0 ? 0d : signalCount / (double)nonWhitespaceCount;
        var garbageRatio = nonWhitespaceCount == 0 ? 1d : suspiciousCount / (double)nonWhitespaceCount;
        var symbolRatio = nonWhitespaceCount == 0 ? 0d : symbolCount / (double)nonWhitespaceCount;
        var replacementCharacterRatio = nonWhitespaceCount == 0 ? 0d : replacementCharacterCount / (double)nonWhitespaceCount;
        var shortTokenRatio = wordCount == 0 ? 1d : shortTokenCount / (double)wordCount;
        var digitOnlyLineRatio = lines.Count == 0 ? 0d : digitOnlyLineCount / (double)lines.Count;
        var footnoteRatio = lines.Count == 0 ? 0d : footnoteLineCount / (double)lines.Count;
        var paragraphCoherenceScore = CalculateParagraphCoherenceScore(normalized, lines);
        var vietnameseDiacriticRatio = CalculateVietnameseDiacriticRatio(normalized);
        var pageRole = ClassifyPageRole(pageNumber, method, normalized, charCount, wordCount, footnoteRatio, digitOnlyLineRatio);
        var excluded = ShouldExcludeFromDocumentQualityAverage(pageRole, charCount);
        var noiseScore = TextCleanupUtility.EstimateNoiseScore(normalized);
        var qualityAdjustments = new List<string>();
        var qualityScore = CalculatePageQualityScore(
            charCount,
            wordCount,
            signalRatio,
            garbageRatio,
            symbolRatio,
            replacementCharacterRatio,
            shortTokenRatio,
            digitOnlyLineRatio,
            footnoteRatio,
            paragraphCoherenceScore,
            vietnameseDiacriticRatio,
            noiseScore,
            confidence,
            method,
            pageRole,
            qualityAdjustments);
        var warnings = BuildPageWarnings(
            method,
            pageRole,
            charCount,
            wordCount,
            signalRatio,
            garbageRatio,
            symbolRatio,
            replacementCharacterRatio,
            shortTokenRatio,
            digitOnlyLineRatio,
            footnoteRatio,
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
            PageRole = pageRole,
            ExcludedFromDocumentQualityAverage = excluded,
            QualityAdjustments = qualityAdjustments.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ArtifactRatio = Math.Round(Math.Max(garbageRatio, replacementCharacterRatio), 4),
            SymbolRatio = Math.Round(symbolRatio, 4),
            DigitOnlyLineRatio = Math.Round(digitOnlyLineRatio, 4),
            FootnoteRatio = Math.Round(footnoteRatio, 4),
            ParagraphCoherenceScore = Math.Round(paragraphCoherenceScore, 4),
            VietnameseDiacriticRatio = Math.Round(vietnameseDiacriticRatio, 4),
            SelectedVariant = selectedVariant,
            SelectedPass = selectedPass,
            PreprocessingSkipReasons = preprocessingSkipReasons?.ToList() ?? new List<string>(),
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
        var bodyPages = pageReports
            .Where(IsBodyQualityPage)
            .ToList();
        var weightedPages = pageReports
            .Select(report => new { Report = report, Weight = GetPageQualityWeight(report) })
            .Where(item => item.Weight > 0d)
            .ToList();
        var rawAverage = pageReports.Count == 0 ? 0d : Math.Round(pageReports.Average(report => report.QualityScore), 2);
        var weightedAverage = weightedPages.Count == 0
            ? rawAverage
            : Math.Round(weightedPages.Sum(item => item.Report.QualityScore * item.Weight) / weightedPages.Sum(item => item.Weight), 2);
        var bodyAverage = bodyPages.Count == 0
            ? weightedAverage
            : Math.Round(bodyPages.Average(report => report.QualityScore), 2);
        var directTextMajority = pageReports.Count > 0
            && pageReports.Count(report => report.Method == DocumentPageProcessingMethods.DirectText) >= pageReports.Count * 0.60d;
        var cleanBodyPages = bodyPages.Count(report =>
            report.QualityScore >= Math.Max(45, _settings.MinBodyPageQualityForNeedsReview)
            && report.WordCount >= 80
            && report.SignalRatio >= 0.50d);
        var documentStatus = ClassifyDocumentQualityStatus(bodyAverage, weightedAverage, bodyPages.Count, cleanBodyPages, directTextMajority, pageReports);

        if (lowQualityPages.Count > 0)
        {
            warnings.Add($"Low quality extraction detected on page(s): {string.Join(", ", lowQualityPages)}.");
        }

        if (pageReports.Any(report => string.Equals(report.Method, DocumentPageProcessingMethods.Failed, StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("One or more PDF pages failed extraction/OCR.");
        }

        if (directTextMajority)
        {
            warnings.Add("Readable text-layer PDF with extraction artifacts; prefer direct text cleanup and chunk gating before OCR fallback.");
        }

        return new DocumentInputQualityReport
        {
            TotalPages = totalPages,
            DirectTextPages = pageReports.Count(report => report.Method == DocumentPageProcessingMethods.DirectText),
            OcrPages = pageReports.Count(report => report.Method == DocumentPageProcessingMethods.Ocr),
            EmptyPages = pageReports.Count(report => report.Method == DocumentPageProcessingMethods.Empty),
            FailedPages = pageReports.Count(report => report.Method == DocumentPageProcessingMethods.Failed),
            LowQualityPages = lowQualityPages.Count,
            AveragePageQuality = weightedAverage,
            AveragePageQualityRaw = rawAverage,
            AveragePageQualityWeighted = weightedAverage,
            BodyPageQualityAverage = bodyAverage,
            ExcludedPageCount = pageReports.Count(report => report.ExcludedFromDocumentQualityAverage),
            BodyPageCount = bodyPages.Count,
            CoverTitlePageCount = pageReports.Count(report => report.PageRole is DocumentPageRoles.Cover or DocumentPageRoles.Title),
            FootnoteHeavyPageCount = pageReports.Count(report => report.PageRole == DocumentPageRoles.FootnoteHeavy),
            QualityStatus = documentStatus.Status,
            QualityDecisionReason = documentStatus.Reason,
            TopQualityPenalties = BuildTopQualityPenalties(pageReports),
            TotalEstimatedTokens = pageReports.Sum(report => report.EstimatedTokenCount),
            Pages = pageReports,
            PreprocessingEffectiveness = BuildPreprocessingEffectivenessSummary(pageReports),
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
            MarkPreprocessingSkipped(extraction, "retry-policy-disabled");
            return;
        }

        var pagesToRetry = extraction.PagesNeedingOcr
            .Where(pageNumber => extraction.PageReports.TryGetValue(pageNumber, out var report)
                && report.QualityScore < _settings.RetryThreshold)
            .ToList();
        foreach (var pageNumber in extraction.PagesNeedingOcr.Except(pagesToRetry))
        {
            if (extraction.PageReports.TryGetValue(pageNumber, out var report))
            {
                report.PreprocessingSkipReasons.Add("retry-policy-skipped-quality-above-threshold");
            }
        }

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

        var documentLowGainPreprocessingAttempts = 0;
        for (var pageIndex = 0; pageIndex < pagesToRetry.Count; pageIndex++)
        {
            var pageNumber = pagesToRetry[pageIndex];
            var initialReport = extraction.PageReports[pageNumber];
            initialOcrResultsByPage.TryGetValue(pageNumber, out var initialOcrResult);
            var bestReport = initialReport;
            var bestOcrResult = initialOcrResult;
            var preprocessingSkipReasons = new List<string>();
            var attempts = new List<DocumentPageOcrAttemptMetadata>
            {
                BuildAttemptMetadata("initial", initialReport, initialOcrResult, initialReport.QualityScore)
            };

            _logger.LogInformation(
                "OCR retry starting for page {PageNumber}: initialQuality={InitialQuality}, threshold={RetryThreshold}, retryDpi={RetryPdfDpi}, maxRetry={MaxRetryPerPage}",
                pageNumber,
                initialReport.QualityScore,
                _settings.RetryThreshold,
                _settings.RetryPdfDpi,
                maxRetryPerPage);

            var fallbackProfiles = ResolveFallbackProfiles();
            if (ShouldTryPreprocessingFallback(initialReport, documentLowGainPreprocessingAttempts, fallbackProfiles, preprocessingSkipReasons))
            {
                foreach (var profile in fallbackProfiles.Take(Math.Max(1, _settings.MaxPreprocessingVariantsPerPage)))
                {
                    var fallbackResults = await _ocrService.ExtractPageResultsFromPdfPagesAsync(
                        filePath,
                        new[] { pageNumber },
                        new OcrExtractionOptions
                        {
                            PreprocessingProfiles = new[] { profile },
                            IsPreprocessingFallback = true
                        });
                    fallbackResults.TryGetValue(pageNumber, out var fallbackResult);
                    var fallbackReport = BuildReportFromOcrResult(pageNumber, fallbackResult);
                    var fallbackAttempt = BuildAttemptMetadata($"preprocess-{profile}", fallbackReport, fallbackResult, initialReport.QualityScore);
                    attempts.Add(fallbackAttempt);

                    if (fallbackAttempt.IsLowGain)
                    {
                        documentLowGainPreprocessingAttempts++;
                    }

                    _logger.LogInformation(
                        "OCR preprocessing fallback {Profile} for page {PageNumber}: beforeQuality={BeforeQuality}, fallbackQuality={FallbackQuality}, gain={QualityGain}, durationMs={DurationMs}",
                        profile,
                        pageNumber,
                        initialReport.QualityScore,
                        fallbackReport.QualityScore,
                        fallbackAttempt.QualityGain,
                        fallbackAttempt.DurationMs);

                    if (fallbackReport.QualityScore > bestReport.QualityScore)
                    {
                        bestReport = fallbackReport;
                        bestOcrResult = fallbackResult;
                    }

                    if (bestReport.QualityScore >= _settings.RetryThreshold)
                    {
                        break;
                    }
                }
            }

            var shouldRunHighDpiRetry = ShouldRunHighDpiRetry(bestReport);
            if (!shouldRunHighDpiRetry)
            {
                preprocessingSkipReasons.Add("high-dpi-skipped-text-signal-enough");
            }

            for (var retryIndex = 1; shouldRunHighDpiRetry && retryIndex <= maxRetryPerPage; retryIndex++)
            {
                var retryResults = await _ocrService.ExtractPageResultsFromPdfPagesAsync(
                    filePath,
                    new[] { pageNumber },
                    new OcrExtractionOptions
                    {
                        PreprocessingProfiles = new[] { TesseractOcrService.ProfileOriginal }
                    },
                    _settings.RetryPdfDpi);
                retryResults.TryGetValue(pageNumber, out var retryResult);
                var retryReport = BuildReportFromOcrResult(pageNumber, retryResult);
                attempts.Add(BuildAttemptMetadata($"retry-{retryIndex}", retryReport, retryResult, initialReport.QualityScore));

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
            foreach (var attempt in attempts)
            {
                attempt.IsSelectedBest = string.Equals(attempt.Attempt, selectedAttempt, StringComparison.OrdinalIgnoreCase);
            }
            bestReport.OcrRetry = new DocumentPageOcrRetryMetadata
            {
                WasRetried = true,
                RetryThreshold = _settings.RetryThreshold,
                RetryPdfDpi = _settings.RetryPdfDpi,
                MaxRetryPerPage = maxRetryPerPage,
                InitialQualityScore = initialReport.QualityScore,
                SelectedQualityScore = bestReport.QualityScore,
                SelectedAttempt = selectedAttempt,
                Attempts = attempts,
                PreprocessingSkipReasons = preprocessingSkipReasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
            bestReport.PreprocessingSkipReasons = bestReport.OcrRetry.PreprocessingSkipReasons;

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

    private DocumentPageOcrAttemptMetadata BuildAttemptMetadata(
        string attempt,
        DocumentPageProcessingReport report,
        OcrPageExtractionResult? ocrResult,
        int initialQualityScore)
    {
        var qualityGain = report.QualityScore - initialQualityScore;
        var isPreprocessingFallback = ocrResult?.IsPreprocessingFallback == true;
        return new DocumentPageOcrAttemptMetadata
        {
            Attempt = attempt,
            PdfDpi = ocrResult?.PdfDpi,
            QualityScore = report.QualityScore,
            Confidence = report.Confidence,
            SelectedVariant = report.SelectedVariant,
            SelectedPass = report.SelectedPass,
            PreprocessingProfile = ocrResult?.PreprocessingProfile,
            IsPreprocessingFallback = isPreprocessingFallback,
            DurationMs = ocrResult?.DurationMs ?? 0,
            QualityGain = qualityGain,
            IsLowGain = isPreprocessingFallback && qualityGain < _settings.MinPreprocessingGainThreshold,
            IsSelectedBest = false,
            FailureReason = ocrResult?.FailureReason
        };
    }

    private List<string> ResolveFallbackProfiles()
    {
        var profiles = new List<string>();
        if (_settings.EnableCropBorder)
        {
            profiles.Add(TesseractOcrService.ProfileCropBorder);
        }

        if (_settings.EnableThresholdFallback)
        {
            profiles.Add(TesseractOcrService.ProfileThresholdSoft);
            profiles.Add(TesseractOcrService.ProfileBinaryStrong);
        }

        profiles.Add(TesseractOcrService.ProfileContrastEnhanced);
        return profiles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, _settings.MaxPreprocessingVariantsPerPage))
            .ToList();
    }

    private bool ShouldTryPreprocessingFallback(
        DocumentPageProcessingReport initialReport,
        int documentLowGainPreprocessingAttempts,
        IReadOnlyCollection<string> fallbackProfiles,
        List<string> skipReasons)
    {
        if (!_settings.EnablePreprocessingFallback)
        {
            skipReasons.Add("preprocessing-disabled");
            return false;
        }

        if (fallbackProfiles.Count == 0 || _settings.MaxPreprocessingVariantsPerPage <= 0)
        {
            skipReasons.Add("preprocessing-variant-cap-reached");
            return false;
        }

        if (HasEnoughTextSignal(initialReport))
        {
            skipReasons.Add("text-signal-enough");
            return false;
        }

        if (documentLowGainPreprocessingAttempts >= Math.Max(1, _settings.MaxLowGainPreprocessingAttemptsPerDocument))
        {
            skipReasons.Add("document-preprocessing-low-gain-limit");
            return false;
        }

        return true;
    }

    private bool ShouldRunHighDpiRetry(DocumentPageProcessingReport bestReport)
        => bestReport.QualityScore < _settings.RetryThreshold && !HasEnoughTextSignal(bestReport);

    private static bool HasEnoughTextSignal(DocumentPageProcessingReport report)
        => report.WordCount >= 80
            && report.CharCount >= 300
            && report.SignalRatio >= 0.62d
            && report.NoiseScore < 20
            && (report.Confidence == null || report.Confidence >= 0.55d);

    private DocumentPreprocessingEffectivenessSummary BuildPreprocessingEffectivenessSummary(
        IReadOnlyCollection<DocumentPageProcessingReport> reportsByPage)
    {
        var pages = reportsByPage.ToList();
        var attempts = pages
            .SelectMany(page => page.OcrRetry?.Attempts ?? new List<DocumentPageOcrAttemptMetadata>())
            .Where(attempt => attempt.IsPreprocessingFallback)
            .ToList();
        var profileGroups = attempts
            .Where(attempt => !string.IsNullOrWhiteSpace(attempt.PreprocessingProfile))
            .GroupBy(attempt => attempt.PreprocessingProfile!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Profile = group.Key,
                AverageGain = group.Average(attempt => attempt.QualityGain),
                Wins = group.Count(attempt => attempt.IsSelectedBest)
            })
            .ToList();

        return new DocumentPreprocessingEffectivenessSummary
        {
            AttemptCount = attempts.Count,
            SelectedAttemptCount = attempts.Count(attempt => attempt.IsSelectedBest),
            LowGainAttemptCount = attempts.Count(attempt => attempt.IsLowGain),
            AverageQualityGain = attempts.Count == 0 ? 0d : Math.Round(attempts.Average(attempt => attempt.QualityGain), 2),
            AverageDurationMs = attempts.Count == 0 ? 0d : Math.Round(attempts.Average(attempt => attempt.DurationMs), 2),
            BestProfile = profileGroups.OrderByDescending(group => group.AverageGain).ThenByDescending(group => group.Wins).FirstOrDefault()?.Profile,
            WorstProfile = profileGroups.OrderBy(group => group.AverageGain).FirstOrDefault()?.Profile,
            ProfileWinCounts = profileGroups
                .Where(group => group.Wins > 0)
                .ToDictionary(group => group.Profile, group => group.Wins, StringComparer.OrdinalIgnoreCase),
            SkipReasonCounts = pages
                .SelectMany(page => page.OcrRetry?.PreprocessingSkipReasons ?? page.PreprocessingSkipReasons)
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .GroupBy(reason => reason, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static void MarkPreprocessingSkipped(DirectPdfExtractionResult extraction, string reason)
    {
        foreach (var pageNumber in extraction.PagesNeedingOcr)
        {
            if (extraction.PageReports.TryGetValue(pageNumber, out var report))
            {
                report.PreprocessingSkipReasons.Add(reason);
            }
        }
    }

    private int CalculatePageQualityScore(
        int charCount,
        int wordCount,
        double signalRatio,
        double garbageRatio,
        double symbolRatio,
        double replacementCharacterRatio,
        double shortTokenRatio,
        double digitOnlyLineRatio,
        double footnoteRatio,
        double paragraphCoherenceScore,
        double vietnameseDiacriticRatio,
        int noiseScore,
        double? confidence,
        string method,
        string pageRole,
        List<string> qualityAdjustments)
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
        score -= (int)Math.Round(Math.Clamp(symbolRatio - 0.28d, 0d, 1d) * 16);
        score -= (int)Math.Round(Math.Clamp(replacementCharacterRatio, 0d, 1d) * 70);
        if (ShouldPenalizeShortTokens(wordCount, signalRatio, garbageRatio, symbolRatio, replacementCharacterRatio, shortTokenRatio))
        {
            score -= (int)Math.Round(Math.Clamp(shortTokenRatio - 0.45d, 0d, 1d) * 18);
        }
        else if (shortTokenRatio > 0.50d)
        {
            qualityAdjustments.Add("Short Vietnamese function words treated as signal because word count and signal ratio are healthy.");
        }

        score -= (int)Math.Round(Math.Clamp(digitOnlyLineRatio - 0.18d, 0d, 1d) * 12);
        score -= (int)Math.Round(Math.Clamp(0.35d - paragraphCoherenceScore, 0d, 0.35d) * 22);
        score -= Math.Min(35, noiseScore);

        if (confidence.HasValue)
        {
            score -= (int)Math.Round(Math.Clamp(0.78d - confidence.Value, 0d, 0.78d) * 25);
        }

        if (method == DocumentPageProcessingMethods.DirectText && wordCount >= 120 && signalRatio >= 0.55d)
        {
            score += 8;
            qualityAdjustments.Add("Direct text layer with enough readable words.");
        }

        if (vietnameseDiacriticRatio >= 0.12d && wordCount >= 80 && signalRatio >= 0.50d)
        {
            score += 6;
            qualityAdjustments.Add("Vietnamese diacritics indicate real text signal.");
        }

        if (pageRole == DocumentPageRoles.FootnoteHeavy && wordCount >= 80 && signalRatio >= 0.48d)
        {
            score += 6;
            qualityAdjustments.Add("Footnotes/citations are warnings, not hard quality failures.");
        }

        if (pageRole is DocumentPageRoles.Cover or DocumentPageRoles.Title && charCount < 250)
        {
            score = Math.Max(score, 55);
            qualityAdjustments.Add("Cover/title page excluded from document average.");
        }

        return Math.Clamp(score, 0, 100);
    }

    private List<string> BuildPageWarnings(
        string method,
        string pageRole,
        int charCount,
        int wordCount,
        double signalRatio,
        double garbageRatio,
        double symbolRatio,
        double replacementCharacterRatio,
        double shortTokenRatio,
        double digitOnlyLineRatio,
        double footnoteRatio,
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

        if (symbolRatio > 0.38d || replacementCharacterRatio > 0.02d)
        {
            warnings.Add("Page contains many extraction artifact symbols.");
        }

        if (digitOnlyLineRatio > 0.20d)
        {
            warnings.Add("Page contains many digit-only lines or page markers.");
        }

        if (footnoteRatio > 0.18d || pageRole == DocumentPageRoles.FootnoteHeavy)
        {
            warnings.Add("Page is footnote/citation heavy; treat as review signal, not hard rejection.");
        }

        if (shortTokenRatio > 0.58d && wordCount < 250 && signalRatio < 0.55d)
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

    private static bool ShouldPenalizeShortTokens(
        int wordCount,
        double signalRatio,
        double garbageRatio,
        double symbolRatio,
        double replacementCharacterRatio,
        double shortTokenRatio)
    {
        if (wordCount >= 250 && signalRatio >= 0.50d)
        {
            return garbageRatio > 0.12d
                || symbolRatio > 0.36d
                || replacementCharacterRatio > 0.01d
                || shortTokenRatio > 0.68d;
        }

        return shortTokenRatio > 0.45d;
    }

    private static string ClassifyPageRole(
        int pageNumber,
        string method,
        string text,
        int charCount,
        int wordCount,
        double footnoteRatio,
        double digitOnlyLineRatio)
    {
        if (method == DocumentPageProcessingMethods.Empty || charCount == 0)
        {
            return DocumentPageRoles.Empty;
        }

        var lowered = text.ToLowerInvariant();
        if (Regex.IsMatch(lowered, @"\b(mục lục|muc luc|table of contents|contents)\b", RegexOptions.IgnoreCase)
            || LooksLikeTableOfContents(text))
        {
            return DocumentPageRoles.TableOfContents;
        }

        if (Regex.IsMatch(lowered, @"\b(tài liệu tham khảo|tai lieu tham khao|tham khảo|references|bibliography)\b", RegexOptions.IgnoreCase))
        {
            return DocumentPageRoles.References;
        }

        if (pageNumber <= 2 && (charCount < 900 || Regex.IsMatch(lowered, @"\b(nhà xuất bản|nha xuat ban|giáo trình|giao trinh|bộ giáo dục|ban tuyên giáo|title)\b", RegexOptions.IgnoreCase)))
        {
            return pageNumber == 1 ? DocumentPageRoles.Cover : DocumentPageRoles.Title;
        }

        if (footnoteRatio >= 0.18d || (digitOnlyLineRatio >= 0.16d && wordCount >= 80))
        {
            return DocumentPageRoles.FootnoteHeavy;
        }

        return DocumentPageRoles.Body;
    }

    private bool ShouldExcludeFromDocumentQualityAverage(string pageRole, int charCount)
    {
        if (pageRole == DocumentPageRoles.Empty)
        {
            return true;
        }

        return _settings.ExcludeCoverPagesFromQualityAverage
            && pageRole is DocumentPageRoles.Cover or DocumentPageRoles.Title
            && charCount < 900;
    }

    private static double GetPageQualityWeight(DocumentPageProcessingReport report)
        => report.ExcludedFromDocumentQualityAverage
            ? 0d
            : report.PageRole switch
            {
                DocumentPageRoles.Body => 1.0d,
                DocumentPageRoles.TableOfContents => 0.6d,
                DocumentPageRoles.References => 0.5d,
                DocumentPageRoles.FootnoteHeavy => 0.5d,
                DocumentPageRoles.Cover or DocumentPageRoles.Title => 0.1d,
                DocumentPageRoles.Empty => 0d,
                _ => 1.0d
            };

    private static bool IsBodyQualityPage(DocumentPageProcessingReport report)
        => report.PageRole is DocumentPageRoles.Body or DocumentPageRoles.FootnoteHeavy
            || (string.IsNullOrWhiteSpace(report.PageRole)
                && report.Method is DocumentPageProcessingMethods.DirectText or DocumentPageProcessingMethods.Ocr);

    private (string Status, string Reason) ClassifyDocumentQualityStatus(
        double bodyAverage,
        double weightedAverage,
        int bodyPageCount,
        int cleanBodyPageCount,
        bool directTextMajority,
        IReadOnlyCollection<DocumentPageProcessingReport> pages)
    {
        var bodyCoverage = bodyPageCount == 0 ? 0d : cleanBodyPageCount / (double)bodyPageCount;
        var mostlyFailed = pages.Count > 0
            && pages.Count(page => page.Method is DocumentPageProcessingMethods.Empty or DocumentPageProcessingMethods.Failed) > pages.Count * 0.50d;

        if (bodyPageCount == 0 || mostlyFailed || bodyCoverage < 0.20d)
        {
            return (DocumentQualityStatuses.Rejected, "Body pages are mostly empty, failed, or too noisy for clean chunk coverage.");
        }

        if (bodyAverage >= _settings.MinBodyPageQualityForAccepted)
        {
            var status = pages.Any(page => page.QualityScore < _settings.MinAcceptablePageQuality)
                ? DocumentQualityStatuses.AcceptedWithWarnings
                : DocumentQualityStatuses.Accepted;
            var reason = directTextMajority
                ? "Readable text-layer PDF with extraction artifacts; OCR fallback not needed for most pages."
                : "Readable body pages pass weighted quality thresholds.";
            return (status, reason);
        }

        if (bodyAverage >= _settings.MinBodyPageQualityForNeedsReview && (bodyCoverage >= 0.35d || weightedAverage >= _settings.MinBodyPageQualityForNeedsReview))
        {
            return (DocumentQualityStatuses.NeedsReview, "Readable but noisy body pages have enough clean coverage for cautious AI processing.");
        }

        return (DocumentQualityStatuses.Rejected, "Body page quality and clean coverage are below configured thresholds.");
    }

    private static List<string> BuildTopQualityPenalties(IReadOnlyCollection<DocumentPageProcessingReport> pageReports)
        => pageReports
            .SelectMany(page => page.Warnings)
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .GroupBy(warning => warning, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(8)
            .Select(group => $"{group.Key} ({group.Count()} page(s))")
            .ToList();

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
                combinedPages[pageNumber] = NormalizeExtractedTextForChunking(ocrText);
                continue;
            }

            if (directTextByPage.TryGetValue(pageNumber, out var directText) && !string.IsNullOrWhiteSpace(directText))
            {
                combinedPages[pageNumber] = NormalizeExtractedTextForChunking(directText);
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

        return alphanumericCount >= 18
            && wordCount >= 3
            && signalRatio >= 0.35d
            && noiseScore <= Math.Max(8, wordCount / 10);
    }

    private static string NormalizeDirectPdfText(string text)
        => NormalizeExtractedTextForChunking(text);

    private static string NormalizeExtractedTextForQuality(string text)
    {
        var normalized = NormalizeVietnameseText(text);
        normalized = TextCleanupUtility.NormalizeForAi(normalized, preserveLineBreaks: true);
        normalized = Regex.Replace(normalized, @"(?m)^\s*(?:trang|page)\s+\d{1,4}(?:\s*/\s*\d{1,4})?\s*$", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"(?m)^\s*\d{1,4}\s*$", string.Empty);
        normalized = Regex.Replace(normalized, @"(?m)^\s*[\[\(]?\d{1,3}[\]\)]?\s+(?=\p{Ll})", string.Empty);
        normalized = Regex.Replace(normalized, @"(?<=\p{Ll})-\s*\n\s*(?=\p{Ll})", string.Empty);
        normalized = Regex.Replace(normalized, @"(?<=[\p{Ll},;])\s*\n\s*(?=\p{Ll})", " ");
        normalized = Regex.Replace(normalized, @"[ \t]{2,}", " ");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static string NormalizeExtractedTextForChunking(string text)
    {
        var normalized = NormalizeVietnameseText(text);
        normalized = TextCleanupUtility.NormalizeForAi(normalized, preserveLineBreaks: true);
        normalized = Regex.Replace(normalized, @"(?m)^\s*(?:trang|page)\s+\d{1,4}(?:\s*/\s*\d{1,4})?\s*$", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"(?m)^\s*\d{1,4}\s*$", string.Empty);
        normalized = Regex.Replace(normalized, @"(?<=\p{Ll})-\s*\n\s*(?=\p{Ll})", string.Empty);
        normalized = Regex.Replace(normalized, @"(?<=[\p{Ll},;])\s*\n\s*(?=\p{Ll})", " ");
        normalized = Regex.Replace(normalized, @"[ \t]{2,}", " ");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static string NormalizeVietnameseText(string text)
        => string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Normalize(NormalizationForm.FormC);

    private static bool LooksLikeFootnoteLine(string line)
        => Regex.IsMatch(line, @"^\s*(?:\d{1,3}|[\*\u2020])\s+[\p{L}\(\[""]", RegexOptions.IgnoreCase)
            || Regex.IsMatch(line, @"\b(sđd|ibid|tlđd|tr\.\s*\d+|nxb|xem:|xem thêm:)\b", RegexOptions.IgnoreCase);

    private static bool LooksLikeTableOfContents(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 6)
        {
            return false;
        }

        var tocLineCount = lines.Count(line => Regex.IsMatch(line, @"\.{2,}\s*\d{1,4}$|^\s*(chuong|chương|phan|phần|muc|mục)\b.*\d{1,4}$", RegexOptions.IgnoreCase));
        return tocLineCount >= Math.Max(4, lines.Length / 3);
    }

    private static double CalculateParagraphCoherenceScore(string text, IReadOnlyCollection<string> lines)
    {
        if (string.IsNullOrWhiteSpace(text) || lines.Count == 0)
        {
            return 0d;
        }

        var coherentLines = lines.Count(line =>
        {
            var words = WordRegex.Matches(line).Count;
            return words >= 5 && line.Count(char.IsLetterOrDigit) >= Math.Max(12, line.Length * 0.45d);
        });
        var sentenceCount = SentenceRegex.Split(text).Count(sentence => WordRegex.Matches(sentence).Count >= 5);
        var lineScore = coherentLines / (double)Math.Max(1, lines.Count);
        var sentenceScore = Math.Min(1d, sentenceCount / Math.Max(2d, lines.Count / 3d));
        return Math.Clamp((lineScore * 0.65d) + (sentenceScore * 0.35d), 0d, 1d);
    }

    private static double CalculateVietnameseDiacriticRatio(string text)
    {
        var letterCount = text.Count(char.IsLetter);
        if (letterCount == 0)
        {
            return 0d;
        }

        return VietnameseDiacriticRegex.Matches(text).Count / (double)letterCount;
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
