using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Options;
using ELearnGamePlatform.Infrastructure.Configuration;
using ELearnGamePlatform.Infrastructure.Data;
using ELearnGamePlatform.Infrastructure.Services;
using ELearnGamePlatform.Services.AI;
using ELearnGamePlatform.Services.DocumentProcessing;
using ELearnGamePlatform.Services.OCR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

const string sourcePdfPath = @"D:\DownLoad\Group 7.pdf";
var repoRoot = Directory.GetCurrentDirectory();
var apiRoot = Path.Combine(repoRoot, "src", "ELearnGamePlatform.API");
var uploadRoot = Path.Combine(apiRoot, "uploads");
var appSettingsPath = Path.Combine(apiRoot, "appsettings.json");

if (!File.Exists(sourcePdfPath))
{
    throw new FileNotFoundException("Source PDF was not found.", sourcePdfPath);
}

Directory.CreateDirectory(uploadRoot);

var configuration = new ConfigurationBuilder()
    .SetBasePath(apiRoot)
    .AddJsonFile(appSettingsPath, optional: false, reloadOnChange: false)
    .Build();

var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.ClearProviders();
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
    builder.SetMinimumLevel(LogLevel.Information);
});

services.Configure<OllamaSettings>(configuration.GetSection("OllamaSettings"));
services.Configure<LocalLlmSettings>(configuration.GetSection(LocalLlmSettings.SectionName));
services.Configure<OcrSettings>(configuration.GetSection(OcrSettings.SectionName));
services.Configure<DocumentUnderstandingOptions>(configuration.GetSection(DocumentUnderstandingOptions.SectionName));

var connectionString = configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection is missing.");

services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
services.AddHttpClient<IOllamaService, OllamaService>();
services.AddScoped<IOcrService, TesseractOcrService>();
services.AddScoped<IDocumentProcessor, PdfProcessor>();
services.AddScoped<IContentAnalyzer, ContentAnalyzerService>();
services.AddScoped<ITokenEstimator, TokenEstimator>();
services.AddScoped<ITokenBudgetPlanner, TokenBudgetPlanner>();
services.AddScoped<IPromptAssembler, PromptAssembler>();
services.AddScoped<IDocumentKnowledgeMapBuilder, DocumentKnowledgeMapBuilder>();
services.AddScoped<IDocumentInputQualityGate, DocumentInputQualityGate>();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();

var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("RepairGroup7");
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var processor = scope.ServiceProvider.GetServices<IDocumentProcessor>()
    .First(item => item.SupportedFileType("pdf"));
var qualityGate = scope.ServiceProvider.GetRequiredService<IDocumentInputQualityGate>();
var analyzer = scope.ServiceProvider.GetRequiredService<IContentAnalyzer>();
var tokenBudgetPlanner = scope.ServiceProvider.GetRequiredService<ITokenBudgetPlanner>();

var document = await db.Documents
    .FirstOrDefaultAsync(item => item.FileName == "Group 7.pdf");

if (document == null)
{
    throw new InvalidOperationException("Existing document row for Group 7.pdf was not found.");
}

var beforeSlideCount = await db.SlideItems.CountAsync();
var beforeQuestionCount = await db.Questions.CountAsync();
var beforeCoverageCount = document.GetCoverageMap().Count;
var beforeStructureCount = document.GetProcessingMetadata().Structure.Count;
var beforeTextLength = document.ExtractedText?.Length ?? 0;

logger.LogInformation(
    "Before repair: documentId={DocumentId}, extractedChars={ExtractedChars}, coverage={CoverageCount}, structure={StructureCount}, slideItems={SlideItems}, questions={Questions}",
    document.Id,
    beforeTextLength,
    beforeCoverageCount,
    beforeStructureCount,
    beforeSlideCount,
    beforeQuestionCount);

var copiedPdfPath = Path.Combine(uploadRoot, $"{Guid.NewGuid():N}.pdf");
File.Copy(sourcePdfPath, copiedPdfPath, overwrite: false);
logger.LogInformation("Copied PDF to {CopiedPdfPath}", copiedPdfPath);

var extractionProgress = new Progress<DocumentProcessingProgressUpdate>(update =>
{
    logger.LogInformation(
        "Extract {Percent}% {Stage}: {Message} {Detail}",
        update.Percent,
        update.Stage,
        update.Message,
        update.Detail);
});

var extractedText = await processor.ExtractTextAsync(copiedPdfPath, "pdf", extractionProgress);
var qualityResult = qualityGate.Evaluate(extractedText);
var pageQualityReport = processor is IDocumentInputQualityReportProvider reportProvider
    ? reportProvider.LastInputQualityReport
    : null;

logger.LogInformation(
    "Extraction complete: chars={CharCount}, quality={QualityClass}, score={QualityScore}, words={WordCount}",
    extractedText.Length,
    qualityResult.Classification,
    qualityResult.QualityScore,
    qualityResult.WordCount);

var analysisProgress = new Progress<DocumentProcessingProgressUpdate>(update =>
{
    logger.LogInformation(
        "Analyze {Percent}% {Stage}: {Message} {Detail}",
        update.Percent,
        update.Stage,
        update.Message,
        update.Detail);
});

var processedContent = await analyzer.AnalyzeContentAsync(extractedText, analysisProgress);
if (processedContent.Structure.Count == 0)
{
    logger.LogWarning("Analyzer returned zero structure sections; building page-level repair structure from OCR text.");
    var repairedCoverage = BuildPageLevelCoverage(extractedText, tokenBudgetPlanner);
    if (repairedCoverage.Count > 0)
    {
        processedContent.CoverageMap = repairedCoverage;
        processedContent.Structure = repairedCoverage
            .Select(chunk => new DocumentSectionDescriptor
            {
                SectionKey = chunk.SectionKey ?? chunk.ChunkId,
                Heading = chunk.HeadingText ?? chunk.Label,
                Classification = chunk.Classification,
                StartPage = chunk.StartPage,
                EndPage = chunk.EndPage,
                ChunkIds = new List<string> { chunk.ChunkId }
            })
            .ToList();
    }
}
var analysisChunkBudget = tokenBudgetPlanner.PlanChunks(processedContent.CoverageMap, "analysis");

document.FilePath = copiedPdfPath;
document.FileSize = new FileInfo(copiedPdfPath).Length;
document.FileType = "pdf";
document.ExtractedText = extractedText;
document.SetMainTopics(processedContent.MainTopics);
document.SetKeyPoints(processedContent.KeyPoints);
document.SetCoverageMap(processedContent.CoverageMap);
document.SetProcessingMetadata(new DocumentProcessingMetadata
{
    DocumentType = processedContent.DocumentType,
    Language = processedContent.Language,
    Title = processedContent.Title,
    MainContentStartPage = processedContent.MainContentStartPage,
    Structure = processedContent.Structure,
    ExcludedContent = processedContent.ExcludedContent,
    InputQuality = qualityResult,
    PageQualityReport = pageQualityReport,
    AnalysisTokenBudget = analysisChunkBudget,
    TotalChunks = analysisChunkBudget.TotalChunks,
    AverageChunkTokens = analysisChunkBudget.AverageChunkTokens,
    SelectedChunks = analysisChunkBudget.SelectedChunks.Count,
    SelectedTextTokens = analysisChunkBudget.SelectedTextTokens,
    BudgetFillRatio = analysisChunkBudget.BudgetFillRatio,
    IncludeFullChunkText = analysisChunkBudget.IncludeFullChunkText,
    OmittedChunks = analysisChunkBudget.OmittedChunks.Count
});
document.Summary = processedContent.Summary;
document.Language = processedContent.Language;
document.Status = DocumentStatus.Completed;
document.IncludeInFolderSlides = true;
document.UpdatedAt = DateTime.UtcNow;

await db.SaveChangesAsync();

var afterSlideCount = await db.SlideItems.CountAsync();
var afterQuestionCount = await db.Questions.CountAsync();
var refreshed = await db.Documents.AsNoTracking()
    .FirstAsync(item => item.Id == document.Id);

logger.LogInformation(
    "After repair: extractedChars={ExtractedChars}, coverage={CoverageCount}, structure={StructureCount}, slideItems={SlideItems}, questions={Questions}, filePath={FilePath}",
    refreshed.ExtractedText?.Length ?? 0,
    refreshed.GetCoverageMap().Count,
    refreshed.GetProcessingMetadata().Structure.Count,
    afterSlideCount,
    afterQuestionCount,
    refreshed.FilePath);

if ((refreshed.ExtractedText?.Length ?? 0) < 3000)
{
    throw new InvalidOperationException("Repair did not persist enough extracted text.");
}

if (refreshed.GetCoverageMap().Count <= 0)
{
    throw new InvalidOperationException("Repair did not persist any coverage chunks.");
}

if (refreshed.GetProcessingMetadata().Structure.Count <= 0)
{
    throw new InvalidOperationException("Repair did not persist any structure sections.");
}

if (afterSlideCount != beforeSlideCount || afterQuestionCount != beforeQuestionCount)
{
    throw new InvalidOperationException("Repair changed slide or question counts unexpectedly.");
}

logger.LogInformation("Repair completed successfully.");

static List<DocumentCoverageChunk> BuildPageLevelCoverage(string extractedText, ITokenBudgetPlanner tokenBudgetPlanner)
{
    var matches = Regex.Matches(
        extractedText,
        @"\[Page\s+(?<page>\d+)\]\s*(?<text>.*?)(?=\r?\n\[Page\s+\d+\]|\z)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);
    var chunks = new List<DocumentCoverageChunk>();

    foreach (Match match in matches)
    {
        var page = int.TryParse(match.Groups["page"].Value, out var parsedPage)
            ? parsedPage
            : chunks.Count + 1;
        var pageText = NormalizeRepairText(match.Groups["text"].Value);
        if (CountLettersOrDigits(pageText) < 20)
        {
            continue;
        }

        var heading = ResolveRepairHeading(pageText, page);
        var chunkId = $"P{page:00}";
        var estimatedTokens = Math.Max(1, tokenBudgetPlanner.PlanText(pageText, "repair-page").EstimatedInputTokens);
        var isLikelyNoise = EstimateSignalRatio(pageText) < 0.35d;
        var classification = isLikelyNoise ? ChunkClassifications.Noise : ChunkClassifications.LessonContent;
        var qualityScore = isLikelyNoise ? 35 : 72;

        chunks.Add(new DocumentCoverageChunk
        {
            ChunkNumber = chunks.Count + 1,
            ChunkId = chunkId,
            Zone = ResolveZone(page, matches.Count),
            CoverageZone = ResolveZone(page, matches.Count),
            Label = heading,
            HeadingKind = "slide-page",
            HeadingLevel = 1,
            HeadingText = heading,
            NormalizedHeading = heading,
            HeadingPath = heading,
            SectionKey = $"page-{page:00}",
            IsPrimarySection = true,
            Classification = classification,
            TeachabilityScore = qualityScore,
            ChunkQualityScore = qualityScore,
            EstimatedTokenCount = estimatedTokens,
            TextTokenCount = estimatedTokens,
            TokenEfficiencyScore = 70,
            KeyFactDensityScore = 65,
            PositiveSignals = new List<string> { "Recovered from OCR page text." },
            NegativeSignals = isLikelyNoise ? new List<string> { "Low OCR signal ratio." } : new List<string>(),
            SelectionReason = $"Recovered page-level section from OCR page {page}.",
            StartPage = page,
            EndPage = page,
            SourcePageStart = page,
            SourcePageEnd = page,
            IsEligibleForQuestionGeneration = !isLikelyNoise,
            Warnings = isLikelyNoise ? new List<string> { "Page-level OCR text may contain noise." } : new List<string>(),
            Summary = BuildRepairSummary(pageText),
            EvidenceExcerpt = Truncate(pageText, 420),
            Keywords = ExtractRepairKeywords(heading, pageText),
            ConceptAnchors = ExtractRepairKeywords(heading, pageText).Take(5).ToList(),
            ChunkingReason = "page-level-repair",
            KeyFacts = ExtractRepairFacts(pageText),
            Text = pageText,
            NormalizedText = pageText
        });
    }

    return chunks;
}

static string NormalizeRepairText(string value)
    => Regex.Replace(value.Replace("\\r", "\n").Replace("\r", "\n"), @"[ \t]+", " ").Trim();

static string ResolveRepairHeading(string pageText, int page)
{
    var lines = pageText
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(line => CountLettersOrDigits(line) >= 3)
        .ToList();
    var heading = lines.FirstOrDefault(line => line.Length <= 90)
        ?? lines.FirstOrDefault()
        ?? $"Page {page}";
    return Truncate(heading, 90);
}

static int CountLettersOrDigits(string value)
    => value.Count(char.IsLetterOrDigit);

static double EstimateSignalRatio(string value)
{
    var nonWhitespace = value.Count(ch => !char.IsWhiteSpace(ch));
    if (nonWhitespace == 0)
    {
        return 0d;
    }

    return value.Count(char.IsLetterOrDigit) / (double)nonWhitespace;
}

static string ResolveZone(int page, int totalPages)
{
    if (page <= Math.Max(1, totalPages / 3))
    {
        return "dau";
    }

    return page >= Math.Max(1, totalPages * 2 / 3) ? "cuoi" : "giua";
}

static string BuildRepairSummary(string text)
{
    var normalized = Regex.Replace(text, @"\s+", " ").Trim();
    var sentence = Regex.Match(normalized, @"^(.{80,260}?[.!?])(?:\s|$)");
    return sentence.Success ? sentence.Groups[1].Value : Truncate(normalized, 260);
}

static List<string> ExtractRepairFacts(string text)
    => Regex.Split(text, @"(?<=[.!?])\s+|\n+")
        .Select(item => Regex.Replace(item, @"\s+", " ").Trim())
        .Where(item => CountLettersOrDigits(item) >= 20)
        .Take(4)
        .ToList();

static List<string> ExtractRepairKeywords(string heading, string text)
{
    var source = $"{heading} {text}";
    return Regex.Matches(source, @"[\p{L}\p{N}][\p{L}\p{N}\-/]{2,}")
        .Select(match => match.Value.Trim())
        .Where(value => value.Length >= 3)
        .GroupBy(value => value.ToLowerInvariant())
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key)
        .Select(group => group.First())
        .Take(10)
        .ToList();
}

static string Truncate(string value, int maxLength)
{
    var normalized = Regex.Replace(value, @"\s+", " ").Trim();
    return normalized.Length <= maxLength
        ? normalized
        : normalized[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
}
