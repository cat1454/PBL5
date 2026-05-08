using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Utilities;
using ELearnGamePlatform.Services.AI;
using ELearnGamePlatform.Services.DocumentProcessing;
using ELearnGamePlatform.Services.OCR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
var inputDirectory = Path.Combine(repositoryRoot, "benchmarks", "input-documents");
var outputDirectory = Path.Combine(repositoryRoot, "benchmarks", "output");
Directory.CreateDirectory(inputDirectory);
Directory.CreateDirectory(outputDirectory);

var settings = LoadOcrSettings(repositoryRoot);
var tokenEstimator = new TokenEstimator();
var inputFiles = Directory
    .EnumerateFiles(inputDirectory, "*.*", SearchOption.AllDirectories)
    .Where(IsSupportedDocument)
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToList();

var startedAt = DateTimeOffset.UtcNow;
var runStopwatch = Stopwatch.StartNew();
var documents = new List<OcrBenchmarkDocumentResult>();

foreach (var filePath in inputFiles)
{
    Console.WriteLine($"Benchmarking {Path.GetRelativePath(inputDirectory, filePath)}...");
    documents.Add(await BenchmarkDocumentAsync(filePath, inputDirectory, settings, tokenEstimator));
}

runStopwatch.Stop();

var run = OcrBenchmarkRun.Build(startedAt, runStopwatch.ElapsedMilliseconds, settings, inputFiles.Count, documents);
var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
var jsonPath = Path.Combine(outputDirectory, $"ocr-benchmark-{timestamp}.json");
var markdownPath = Path.Combine(outputDirectory, $"ocr-benchmark-{timestamp}.md");
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(run, jsonOptions), Encoding.UTF8);
await File.WriteAllTextAsync(markdownPath, RenderMarkdown(run), Encoding.UTF8);

Console.WriteLine($"Processed {run.DocumentCount} document(s).");
Console.WriteLine($"JSON report: {Path.GetRelativePath(repositoryRoot, jsonPath)}");
Console.WriteLine($"Markdown report: {Path.GetRelativePath(repositoryRoot, markdownPath)}");

static async Task<OcrBenchmarkDocumentResult> BenchmarkDocumentAsync(
    string filePath,
    string inputDirectory,
    OcrSettings settings,
    ITokenEstimator tokenEstimator)
{
    var extension = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
    var processor = CreateProcessor(extension, settings, tokenEstimator);
    var progress = new BenchmarkProgressCollector();
    var stopwatch = Stopwatch.StartNew();
    string extractedText;
    string? error = null;

    try
    {
        extractedText = await processor.ExtractTextAsync(filePath, extension, progress);
    }
    catch (Exception ex)
    {
        extractedText = string.Empty;
        error = ex.Message;
    }

    stopwatch.Stop();

    var pageQualityReport = processor is IDocumentInputQualityReportProvider provider
        ? provider.LastInputQualityReport
        : BuildSinglePageQualityReport(extension, extractedText, tokenEstimator, settings, error);

    return OcrBenchmarkDocumentResult.Build(
        Path.GetRelativePath(inputDirectory, filePath),
        extension,
        new FileInfo(filePath).Length,
        stopwatch.ElapsedMilliseconds,
        extractedText,
        error,
        pageQualityReport,
        progress.StageTimings);
}

static IDocumentProcessor CreateProcessor(string extension, OcrSettings settings, ITokenEstimator tokenEstimator)
{
    var ocrService = new TesseractOcrService(
        NullLogger<TesseractOcrService>.Instance,
        Options.Create(settings));

    return extension switch
    {
        "pdf" => new PdfProcessor(
            NullLogger<PdfProcessor>.Instance,
            ocrService,
            tokenEstimator,
            Options.Create(settings)),
        "docx" => new DocxProcessor(NullLogger<DocxProcessor>.Instance),
        "png" or "jpg" or "jpeg" => new ImageProcessor(
            NullLogger<ImageProcessor>.Instance,
            ocrService),
        _ => throw new NotSupportedException($"Unsupported benchmark file type: {extension}")
    };
}

static DocumentInputQualityReport BuildSinglePageQualityReport(
    string extension,
    string text,
    ITokenEstimator tokenEstimator,
    OcrSettings settings,
    string? error)
{
    var normalized = TextCleanupUtility.NormalizeForAi(text, preserveLineBreaks: true);
    var charCount = normalized.Length;
    var wordCount = Regex.Matches(normalized, @"\b[\p{L}\p{N}]+\b").Count;
    var nonWhitespaceCount = normalized.Count(ch => !char.IsWhiteSpace(ch));
    var signalCount = normalized.Count(char.IsLetterOrDigit);
    var suspiciousCount = normalized.Count(IsSuspiciousCharacter);
    var signalRatio = nonWhitespaceCount == 0 ? 0d : signalCount / (double)nonWhitespaceCount;
    var noiseScore = TextCleanupUtility.EstimateNoiseScore(normalized);
    var qualityScore = CalculateSimpleQualityScore(charCount, wordCount, signalRatio, suspiciousCount, nonWhitespaceCount, noiseScore);
    var method = error != null
        ? DocumentPageProcessingMethods.Failed
        : extension is "png" or "jpg" or "jpeg"
            ? DocumentPageProcessingMethods.Ocr
            : DocumentPageProcessingMethods.DirectText;
    var warnings = new List<string>();

    if (error != null)
    {
        warnings.Add(error);
    }

    if (charCount == 0)
    {
        warnings.Add("Document produced no extracted text.");
    }

    if (qualityScore < settings.MinAcceptablePageQuality)
    {
        warnings.Add($"Document quality score is {qualityScore}/100.");
    }

    var page = new DocumentPageProcessingReport
    {
        PageNumber = 1,
        Method = method,
        CharCount = charCount,
        WordCount = wordCount,
        SignalRatio = Math.Round(signalRatio, 4),
        NoiseScore = noiseScore,
        EstimatedTokenCount = tokenEstimator.EstimateTokens(normalized),
        QualityScore = qualityScore,
        Warnings = warnings
    };

    return new DocumentInputQualityReport
    {
        TotalPages = 1,
        DirectTextPages = method == DocumentPageProcessingMethods.DirectText ? 1 : 0,
        OcrPages = method == DocumentPageProcessingMethods.Ocr ? 1 : 0,
        EmptyPages = charCount == 0 && error == null ? 1 : 0,
        FailedPages = error != null ? 1 : 0,
        LowQualityPages = qualityScore < settings.MinAcceptablePageQuality ? 1 : 0,
        AveragePageQuality = qualityScore,
        TotalEstimatedTokens = page.EstimatedTokenCount,
        Pages = new List<DocumentPageProcessingReport> { page },
        Warnings = warnings
    };
}

static int CalculateSimpleQualityScore(
    int charCount,
    int wordCount,
    double signalRatio,
    int suspiciousCount,
    int nonWhitespaceCount,
    int noiseScore)
{
    if (charCount == 0)
    {
        return 0;
    }

    var score = 100;
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
    var garbageRatio = nonWhitespaceCount == 0 ? 1d : suspiciousCount / (double)nonWhitespaceCount;
    score -= (int)Math.Round(Math.Clamp(garbageRatio, 0d, 1d) * 40);
    score -= Math.Min(35, noiseScore);
    return Math.Clamp(score, 0, 100);
}

static OcrSettings LoadOcrSettings(string repositoryRoot)
{
    var settings = new OcrSettings();
    var appsettingsPath = Path.Combine(repositoryRoot, "src", "ELearnGamePlatform.API", "appsettings.json");
    if (!File.Exists(appsettingsPath))
    {
        return settings;
    }

    using var document = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
    if (!document.RootElement.TryGetProperty(OcrSettings.SectionName, out var section))
    {
        return settings;
    }

    settings.DefaultPdfDpi = ReadInt(section, nameof(OcrSettings.DefaultPdfDpi), settings.DefaultPdfDpi);
    settings.RetryPdfDpi = ReadInt(section, nameof(OcrSettings.RetryPdfDpi), settings.RetryPdfDpi);
    settings.MinAcceptablePageQuality = ReadInt(section, nameof(OcrSettings.MinAcceptablePageQuality), settings.MinAcceptablePageQuality);
    settings.RetryThreshold = ReadInt(section, nameof(OcrSettings.RetryThreshold), settings.RetryThreshold);
    settings.MaxRetryPerPage = ReadInt(section, nameof(OcrSettings.MaxRetryPerPage), settings.MaxRetryPerPage);
    settings.EnableQualityProfile = ReadBool(section, nameof(OcrSettings.EnableQualityProfile), settings.EnableQualityProfile);
    settings.EnablePreprocessingFallback = ReadBool(section, nameof(OcrSettings.EnablePreprocessingFallback), settings.EnablePreprocessingFallback);
    settings.EnableCropBorder = ReadBool(section, nameof(OcrSettings.EnableCropBorder), settings.EnableCropBorder);
    settings.EnableThresholdFallback = ReadBool(section, nameof(OcrSettings.EnableThresholdFallback), settings.EnableThresholdFallback);
    settings.MaxPreprocessingVariantsPerPage = ReadInt(section, nameof(OcrSettings.MaxPreprocessingVariantsPerPage), settings.MaxPreprocessingVariantsPerPage);
    settings.MinPreprocessingGainThreshold = ReadInt(section, nameof(OcrSettings.MinPreprocessingGainThreshold), settings.MinPreprocessingGainThreshold);
    settings.MaxLowGainPreprocessingAttemptsPerDocument = ReadInt(section, nameof(OcrSettings.MaxLowGainPreprocessingAttemptsPerDocument), settings.MaxLowGainPreprocessingAttemptsPerDocument);
    return settings;
}

static int ReadInt(JsonElement section, string propertyName, int fallback)
    => section.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var parsed)
        ? parsed
        : fallback;

static bool ReadBool(JsonElement section, string propertyName, bool fallback)
    => section.TryGetProperty(propertyName, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
        ? value.GetBoolean()
        : fallback;

static bool IsSupportedDocument(string path)
{
    var extension = Path.GetExtension(path).ToLowerInvariant();
    return extension is ".pdf" or ".docx" or ".png" or ".jpg" or ".jpeg";
}

static bool IsSuspiciousCharacter(char ch)
    => !char.IsLetterOrDigit(ch)
        && !char.IsWhiteSpace(ch)
        && ",.;:?!()[]\"'/%+-_:".IndexOf(ch) < 0;

static string ResolveRepositoryRoot(string startDirectory)
{
    var directory = new DirectoryInfo(startDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "ELearnGamePlatform.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Directory.GetCurrentDirectory();
}

static string RenderMarkdown(OcrBenchmarkRun run)
{
    var builder = new StringBuilder();
    builder.AppendLine("# OCR Benchmark Report");
    builder.AppendLine();
    builder.AppendLine($"Generated: {run.StartedAt:yyyy-MM-dd HH:mm:ss} UTC");
    builder.AppendLine($"Duration: {run.TotalDurationMs} ms");
    builder.AppendLine($"Documents: {run.DocumentCount}");
    builder.AppendLine();
    builder.AppendLine("## Summary");
    builder.AppendLine();
    builder.AppendLine($"- Pages: {run.Summary.TotalPages}");
    builder.AppendLine($"- Direct text pages: {run.Summary.DirectTextPages}");
    builder.AppendLine($"- OCR pages: {run.Summary.OcrPages}");
    builder.AppendLine($"- Empty pages: {run.Summary.EmptyPages}");
    builder.AppendLine($"- Failed pages: {run.Summary.FailedPages}");
    builder.AppendLine($"- Low quality pages: {run.Summary.LowQualityPages}");
    builder.AppendLine($"- Average page quality: {run.Summary.AveragePageQuality}");
    builder.AppendLine($"- Estimated tokens: {run.Summary.TotalEstimatedTokens}");
    builder.AppendLine($"- Retried pages: {run.Summary.RetriedPages}");
    builder.AppendLine($"- Retry improvements: {run.Summary.RetryImprovedPages}");
    builder.AppendLine($"- Average retry gain: {run.Summary.AverageRetryQualityGain}");
    builder.AppendLine($"- Preprocessing attempts: {run.Summary.PreprocessingAttemptCount}");
    builder.AppendLine($"- Selected preprocessing attempts: {run.Summary.SelectedPreprocessingAttemptCount}");
    builder.AppendLine($"- Low-gain preprocessing attempts: {run.Summary.LowGainPreprocessingAttemptCount}");
    builder.AppendLine($"- Average preprocessing gain: {run.Summary.AveragePreprocessingGain}");
    builder.AppendLine($"- Average preprocessing duration: {run.Summary.AveragePreprocessingDurationMs} ms");
    builder.AppendLine($"- Top preprocessing profiles: {FormatCounts(run.Summary.ProfileWinCounts)}");
    builder.AppendLine($"- Preprocessing skip reasons: {FormatCounts(run.Summary.SkipReasonCounts)}");
    builder.AppendLine();
    builder.AppendLine("## Threshold Recommendations");
    builder.AppendLine();
    foreach (var recommendation in run.ThresholdRecommendations)
    {
        builder.AppendLine($"- {recommendation}");
    }

    builder.AppendLine();
    builder.AppendLine("## Documents");
    builder.AppendLine();

    foreach (var document in run.Documents)
    {
        builder.AppendLine($"### {document.FileName}");
        builder.AppendLine();
        builder.AppendLine($"- Type: {document.FileType}");
        builder.AppendLine($"- Size: {document.FileSizeBytes} bytes");
        builder.AppendLine($"- Duration: {document.DurationMs} ms");
        builder.AppendLine($"- Chars: {document.CharCount}");
        builder.AppendLine($"- Words: {document.WordCount}");
        builder.AppendLine($"- Estimated tokens: {document.EstimatedTokenCount}");
        builder.AppendLine($"- Average quality: {document.AveragePageQuality}");
        builder.AppendLine($"- Low quality pages: {document.LowQualityPages}");
        builder.AppendLine($"- Preprocessing attempts: {document.PreprocessingEffectiveness.AttemptCount}");
        builder.AppendLine($"- Selected preprocessing attempts: {document.PreprocessingEffectiveness.SelectedAttemptCount}");
        builder.AppendLine($"- Low-gain preprocessing attempts: {document.PreprocessingEffectiveness.LowGainAttemptCount}");
        builder.AppendLine($"- Average preprocessing gain: {document.PreprocessingEffectiveness.AverageQualityGain}");
        builder.AppendLine($"- Average preprocessing duration: {document.PreprocessingEffectiveness.AverageDurationMs} ms");
        builder.AppendLine($"- Best preprocessing profile: {document.PreprocessingEffectiveness.BestProfile ?? ""}");
        builder.AppendLine($"- Worst preprocessing profile: {document.PreprocessingEffectiveness.WorstProfile ?? ""}");
        builder.AppendLine($"- Preprocessing skip reasons: {FormatCounts(document.PreprocessingEffectiveness.SkipReasonCounts)}");
        if (!string.IsNullOrWhiteSpace(document.Error))
        {
            builder.AppendLine($"- Error: {document.Error}");
        }

        builder.AppendLine();
        builder.AppendLine("| Page | Method | Quality | Confidence | Chars | Words | Tokens | Retry | Variant | Pass | Preprocess | Warnings |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | --- | --- | --- | --- | --- |");
        foreach (var page in document.Pages)
        {
            builder.AppendLine(
                $"| {page.PageNumber} | {page.Method} | {page.QualityScore} | {FormatNullable(page.Confidence)} | {page.CharCount} | {page.WordCount} | {page.EstimatedTokenCount} | {page.RetrySummary} | {page.SelectedVariant ?? ""} | {page.SelectedPass ?? ""} | {page.PreprocessingSummary} | {string.Join("<br>", page.Warnings)} |");
        }

        if (document.StageTimings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("| Stage | Updates | First ms | Last ms |");
            builder.AppendLine("| --- | ---: | ---: | ---: |");
            foreach (var stage in document.StageTimings)
            {
                builder.AppendLine($"| {stage.Stage} | {stage.UpdateCount} | {stage.FirstSeenMs} | {stage.LastSeenMs} |");
            }
        }

        builder.AppendLine();
    }

    return builder.ToString();
}

static string FormatNullable(double? value)
    => value.HasValue ? value.Value.ToString("0.####") : "";

static string FormatCounts(IReadOnlyDictionary<string, int> counts)
    => counts.Count == 0
        ? ""
        : string.Join(", ", counts.OrderByDescending(item => item.Value).ThenBy(item => item.Key).Select(item => $"{item.Key}={item.Value}"));

sealed class BenchmarkProgressCollector : IProgress<DocumentProcessingProgressUpdate>
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly Dictionary<string, StageTiming> _stages = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<StageTiming> StageTimings => _stages.Values.OrderBy(stage => stage.FirstSeenMs).ToList();

    public void Report(DocumentProcessingProgressUpdate value)
    {
        var key = string.IsNullOrWhiteSpace(value.Stage) ? "unknown" : value.Stage;
        var elapsedMs = _stopwatch.ElapsedMilliseconds;
        if (!_stages.TryGetValue(key, out var timing))
        {
            timing = new StageTiming
            {
                Stage = key,
                StageLabel = value.StageLabel,
                FirstSeenMs = elapsedMs,
                LastSeenMs = elapsedMs,
                LastPercent = value.Percent,
                UpdateCount = 0
            };
            _stages[key] = timing;
        }

        timing.LastSeenMs = elapsedMs;
        timing.LastPercent = value.Percent;
        timing.UpdateCount++;
    }
}

sealed class StageTiming
{
    public string Stage { get; set; } = string.Empty;
    public string? StageLabel { get; set; }
    public long FirstSeenMs { get; set; }
    public long LastSeenMs { get; set; }
    public int LastPercent { get; set; }
    public int UpdateCount { get; set; }
}

sealed class OcrBenchmarkRun
{
    public DateTimeOffset StartedAt { get; set; }
    public long TotalDurationMs { get; set; }
    public OcrBenchmarkSettingsSnapshot Settings { get; set; } = new();
    public int InputDocumentCount { get; set; }
    public int DocumentCount { get; set; }
    public OcrBenchmarkSummary Summary { get; set; } = new();
    public List<string> ThresholdRecommendations { get; set; } = new();
    public List<OcrBenchmarkDocumentResult> Documents { get; set; } = new();

    public static OcrBenchmarkRun Build(
        DateTimeOffset startedAt,
        long totalDurationMs,
        OcrSettings settings,
        int inputDocumentCount,
        List<OcrBenchmarkDocumentResult> documents)
    {
        var summary = OcrBenchmarkSummary.Build(documents);
        return new OcrBenchmarkRun
        {
            StartedAt = startedAt,
            TotalDurationMs = totalDurationMs,
            Settings = OcrBenchmarkSettingsSnapshot.From(settings),
            InputDocumentCount = inputDocumentCount,
            DocumentCount = documents.Count,
            Summary = summary,
            ThresholdRecommendations = BuildThresholdRecommendations(settings, summary, documents),
            Documents = documents
        };
    }

    private static List<string> BuildThresholdRecommendations(
        OcrSettings settings,
        OcrBenchmarkSummary summary,
        IReadOnlyCollection<OcrBenchmarkDocumentResult> documents)
    {
        var recommendations = new List<string>();
        var totalPages = Math.Max(1, summary.TotalPages);
        var lowQualityRate = summary.LowQualityPages / (double)totalPages;
        var retriedPages = Math.Max(1, summary.RetriedPages);
        var retryImprovementRate = summary.RetryImprovedPages / (double)retriedPages;
        var pageQualities = documents.SelectMany(document => document.Pages).Select(page => page.QualityScore).OrderBy(score => score).ToList();
        var percentile25 = pageQualities.Count == 0 ? 0 : pageQualities[(int)Math.Floor((pageQualities.Count - 1) * 0.25d)];

        if (documents.Count == 0)
        {
            recommendations.Add("Add representative PDFs/images/DOCX files under benchmarks/input-documents before tuning thresholds.");
            return recommendations;
        }

        if (summary.FailedPages > 0 || summary.EmptyPages > 0)
        {
            recommendations.Add("Review failed or empty pages before changing thresholds; missing Poppler/tessdata or unreadable inputs can skew quality scores.");
        }

        if (lowQualityRate > 0.30d)
        {
            recommendations.Add($"More than 30% of pages are below MinAcceptablePageQuality={settings.MinAcceptablePageQuality}; inspect samples before raising the threshold.");
        }
        else if (lowQualityRate < 0.05d && summary.AveragePageQuality >= settings.MinAcceptablePageQuality + 10)
        {
            recommendations.Add($"Current MinAcceptablePageQuality={settings.MinAcceptablePageQuality} is conservative for this corpus; consider raising it by 5 if manual review confirms output quality.");
        }
        else
        {
            recommendations.Add($"Keep MinAcceptablePageQuality={settings.MinAcceptablePageQuality} until the corpus grows; current low-quality rate is {lowQualityRate:P1}.");
        }

        if (summary.RetriedPages == 0)
        {
            recommendations.Add($"RetryThreshold={settings.RetryThreshold} did not trigger retries; consider testing with noisier scanned pages before lowering MaxRetryPerPage.");
        }
        else if (retryImprovementRate >= 0.50d && summary.AverageRetryQualityGain >= 5)
        {
            recommendations.Add($"Retries are effective for this corpus; keep RetryThreshold={settings.RetryThreshold}, RetryPdfDpi={settings.RetryPdfDpi}, and MaxRetryPerPage={settings.MaxRetryPerPage}.");
        }
        else
        {
            recommendations.Add($"Retries improved few pages; consider lowering RetryThreshold from {settings.RetryThreshold} or reviewing image preprocessing before increasing retries.");
        }

        if (!settings.EnablePreprocessingFallback)
        {
            recommendations.Add("Preprocessing fallback is disabled; enable it only after adding representative low-quality scanned pages to the benchmark corpus.");
        }
        else if (summary.PreprocessingAttemptCount == 0)
        {
            recommendations.Add("Preprocessing fallback did not run; current pages either had enough signal or retry policy did not allow fallback.");
        }
        else if (summary.SelectedPreprocessingAttemptCount > 0 && summary.AveragePreprocessingGain >= settings.MinPreprocessingGainThreshold)
        {
            recommendations.Add($"Keep preprocessing fallback enabled; average gain is {summary.AveragePreprocessingGain} point(s) over {summary.PreprocessingAttemptCount} attempt(s).");
        }
        else if (summary.LowGainPreprocessingAttemptCount >= Math.Max(1, summary.PreprocessingAttemptCount * 0.75d))
        {
            recommendations.Add("Preprocessing fallback is mostly low-gain on this corpus; keep it selective or lower MaxPreprocessingVariantsPerPage before increasing high-DPI retries.");
        }
        else
        {
            recommendations.Add("Preprocessing fallback produced mixed results; compare profile win counts before enabling more variants.");
        }

        if (percentile25 > settings.MinAcceptablePageQuality)
        {
            recommendations.Add($"The 25th percentile quality score is {percentile25}; a stricter minimum may be reasonable after manual spot checks.");
        }

        return recommendations;
    }
}

sealed class OcrBenchmarkSettingsSnapshot
{
    public int DefaultPdfDpi { get; set; }
    public int RetryPdfDpi { get; set; }
    public int MinAcceptablePageQuality { get; set; }
    public int RetryThreshold { get; set; }
    public int MaxRetryPerPage { get; set; }
    public bool EnableQualityProfile { get; set; }
    public bool EnablePreprocessingFallback { get; set; }
    public bool EnableCropBorder { get; set; }
    public bool EnableThresholdFallback { get; set; }
    public int MaxPreprocessingVariantsPerPage { get; set; }
    public int MinPreprocessingGainThreshold { get; set; }
    public int MaxLowGainPreprocessingAttemptsPerDocument { get; set; }

    public static OcrBenchmarkSettingsSnapshot From(OcrSettings settings)
        => new()
        {
            DefaultPdfDpi = settings.DefaultPdfDpi,
            RetryPdfDpi = settings.RetryPdfDpi,
            MinAcceptablePageQuality = settings.MinAcceptablePageQuality,
            RetryThreshold = settings.RetryThreshold,
            MaxRetryPerPage = settings.MaxRetryPerPage,
            EnableQualityProfile = settings.EnableQualityProfile,
            EnablePreprocessingFallback = settings.EnablePreprocessingFallback,
            EnableCropBorder = settings.EnableCropBorder,
            EnableThresholdFallback = settings.EnableThresholdFallback,
            MaxPreprocessingVariantsPerPage = settings.MaxPreprocessingVariantsPerPage,
            MinPreprocessingGainThreshold = settings.MinPreprocessingGainThreshold,
            MaxLowGainPreprocessingAttemptsPerDocument = settings.MaxLowGainPreprocessingAttemptsPerDocument
        };
}

sealed class OcrBenchmarkSummary
{
    public int TotalPages { get; set; }
    public int DirectTextPages { get; set; }
    public int OcrPages { get; set; }
    public int EmptyPages { get; set; }
    public int FailedPages { get; set; }
    public int LowQualityPages { get; set; }
    public double AveragePageQuality { get; set; }
    public int TotalEstimatedTokens { get; set; }
    public int RetriedPages { get; set; }
    public int RetryImprovedPages { get; set; }
    public double AverageRetryQualityGain { get; set; }
    public int PreprocessingAttemptCount { get; set; }
    public int SelectedPreprocessingAttemptCount { get; set; }
    public int LowGainPreprocessingAttemptCount { get; set; }
    public double AveragePreprocessingGain { get; set; }
    public double AveragePreprocessingDurationMs { get; set; }
    public Dictionary<string, int> ProfileWinCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> SkipReasonCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static OcrBenchmarkSummary Build(IReadOnlyCollection<OcrBenchmarkDocumentResult> documents)
    {
        var pages = documents.SelectMany(document => document.Pages).ToList();
        var retriedPages = pages.Where(page => page.OcrRetry?.WasRetried == true).ToList();
        var retryGains = retriedPages
            .Select(page => Math.Max(0, (page.OcrRetry?.SelectedQualityScore ?? page.QualityScore) - (page.OcrRetry?.InitialQualityScore ?? page.QualityScore)))
            .ToList();
        var preprocessingAttempts = pages
            .SelectMany(page => page.OcrRetry?.Attempts ?? new List<DocumentPageOcrAttemptMetadata>())
            .Where(attempt => attempt.IsPreprocessingFallback)
            .ToList();

        return new OcrBenchmarkSummary
        {
            TotalPages = documents.Sum(document => document.TotalPages),
            DirectTextPages = documents.Sum(document => document.DirectTextPages),
            OcrPages = documents.Sum(document => document.OcrPages),
            EmptyPages = documents.Sum(document => document.EmptyPages),
            FailedPages = documents.Sum(document => document.FailedPages),
            LowQualityPages = documents.Sum(document => document.LowQualityPages),
            AveragePageQuality = pages.Count == 0 ? 0d : Math.Round(pages.Average(page => page.QualityScore), 2),
            TotalEstimatedTokens = documents.Sum(document => document.EstimatedTokenCount),
            RetriedPages = retriedPages.Count,
            RetryImprovedPages = retryGains.Count(gain => gain > 0),
            AverageRetryQualityGain = retryGains.Count == 0 ? 0d : Math.Round(retryGains.Average(), 2),
            PreprocessingAttemptCount = preprocessingAttempts.Count,
            SelectedPreprocessingAttemptCount = preprocessingAttempts.Count(attempt => attempt.IsSelectedBest),
            LowGainPreprocessingAttemptCount = preprocessingAttempts.Count(attempt => attempt.IsLowGain),
            AveragePreprocessingGain = preprocessingAttempts.Count == 0 ? 0d : Math.Round(preprocessingAttempts.Average(attempt => attempt.QualityGain), 2),
            AveragePreprocessingDurationMs = preprocessingAttempts.Count == 0 ? 0d : Math.Round(preprocessingAttempts.Average(attempt => attempt.DurationMs), 2),
            ProfileWinCounts = preprocessingAttempts
                .Where(attempt => attempt.IsSelectedBest && !string.IsNullOrWhiteSpace(attempt.PreprocessingProfile))
                .GroupBy(attempt => attempt.PreprocessingProfile!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            SkipReasonCounts = pages
                .SelectMany(page => page.OcrRetry?.PreprocessingSkipReasons ?? page.PreprocessingSkipReasons)
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .GroupBy(reason => reason, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase)
        };
    }
}

sealed class OcrBenchmarkDocumentResult
{
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public long DurationMs { get; set; }
    public int CharCount { get; set; }
    public int WordCount { get; set; }
    public int EstimatedTokenCount { get; set; }
    public int TotalPages { get; set; }
    public int DirectTextPages { get; set; }
    public int OcrPages { get; set; }
    public int EmptyPages { get; set; }
    public int FailedPages { get; set; }
    public int LowQualityPages { get; set; }
    public double AveragePageQuality { get; set; }
    public string? Error { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<OcrBenchmarkPageResult> Pages { get; set; } = new();
    public DocumentPreprocessingEffectivenessSummary PreprocessingEffectiveness { get; set; } = new();
    public IReadOnlyCollection<StageTiming> StageTimings { get; set; } = Array.Empty<StageTiming>();

    public static OcrBenchmarkDocumentResult Build(
        string fileName,
        string fileType,
        long fileSizeBytes,
        long durationMs,
        string extractedText,
        string? error,
        DocumentInputQualityReport? pageQualityReport,
        IReadOnlyCollection<StageTiming> stageTimings)
    {
        var normalized = TextCleanupUtility.NormalizeForAi(extractedText, preserveLineBreaks: true);
        var report = pageQualityReport ?? new DocumentInputQualityReport();
        return new OcrBenchmarkDocumentResult
        {
            FileName = fileName,
            FileType = fileType,
            FileSizeBytes = fileSizeBytes,
            DurationMs = durationMs,
            CharCount = normalized.Length,
            WordCount = Regex.Matches(normalized, @"\b[\p{L}\p{N}]+\b").Count,
            EstimatedTokenCount = report.TotalEstimatedTokens,
            TotalPages = report.TotalPages,
            DirectTextPages = report.DirectTextPages,
            OcrPages = report.OcrPages,
            EmptyPages = report.EmptyPages,
            FailedPages = report.FailedPages,
            LowQualityPages = report.LowQualityPages,
            AveragePageQuality = report.AveragePageQuality,
            Error = error,
            Warnings = report.Warnings,
            Pages = report.Pages.Select(OcrBenchmarkPageResult.From).ToList(),
            PreprocessingEffectiveness = report.PreprocessingEffectiveness,
            StageTimings = stageTimings
        };
    }
}

sealed class OcrBenchmarkPageResult
{
    public int PageNumber { get; set; }
    public string Method { get; set; } = string.Empty;
    public int CharCount { get; set; }
    public int WordCount { get; set; }
    public double SignalRatio { get; set; }
    public int NoiseScore { get; set; }
    public int EstimatedTokenCount { get; set; }
    public double? Confidence { get; set; }
    public int QualityScore { get; set; }
    public string? SelectedVariant { get; set; }
    public string? SelectedPass { get; set; }
    public string RetrySummary { get; set; } = "not-retried";
    public string PreprocessingSummary { get; set; } = string.Empty;
    public List<string> PreprocessingSkipReasons { get; set; } = new();
    public DocumentPageOcrRetryMetadata? OcrRetry { get; set; }
    public List<string> Warnings { get; set; } = new();

    public static OcrBenchmarkPageResult From(DocumentPageProcessingReport page)
    {
        var retrySummary = page.OcrRetry?.WasRetried == true
            ? $"{page.OcrRetry.InitialQualityScore}->{page.OcrRetry.SelectedQualityScore} ({page.OcrRetry.SelectedAttempt})"
            : "not-retried";
        var preprocessingAttempts = page.OcrRetry?.Attempts
            .Where(attempt => attempt.IsPreprocessingFallback)
            .Select(attempt => $"{attempt.PreprocessingProfile}:{attempt.QualityScore} ({attempt.QualityGain:+#;-#;0}, {attempt.DurationMs}ms{(attempt.IsSelectedBest ? ", selected" : "")}{(attempt.IsLowGain ? ", low-gain" : "")})")
            .ToList() ?? new List<string>();
        var skipReasons = page.OcrRetry?.PreprocessingSkipReasons ?? page.PreprocessingSkipReasons;

        return new OcrBenchmarkPageResult
        {
            PageNumber = page.PageNumber,
            Method = page.Method,
            CharCount = page.CharCount,
            WordCount = page.WordCount,
            SignalRatio = page.SignalRatio,
            NoiseScore = page.NoiseScore,
            EstimatedTokenCount = page.EstimatedTokenCount,
            Confidence = page.Confidence,
            QualityScore = page.QualityScore,
            SelectedVariant = page.SelectedVariant,
            SelectedPass = page.SelectedPass,
            RetrySummary = retrySummary,
            PreprocessingSummary = preprocessingAttempts.Count == 0
                ? string.Join("<br>", skipReasons)
                : string.Join("<br>", preprocessingAttempts),
            PreprocessingSkipReasons = skipReasons,
            OcrRetry = page.OcrRetry,
            Warnings = page.Warnings
        };
    }
}
