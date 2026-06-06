using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using ELearnGamePlatform.Core.Options;
using ELearnGamePlatform.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ELearnGamePlatform.Services.AI;

public class ContentAnalyzerService : IContentAnalyzer
{
    private readonly IOllamaService _ollamaService;
    private readonly ITokenBudgetPlanner _tokenBudgetPlanner;
    private readonly IPromptAssembler _promptAssembler;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly LocalLlmSettings _localLlmSettings;
    private readonly DocumentUnderstandingOptions _documentUnderstandingOptions;
    private readonly IDocumentKnowledgeMapBuilder _knowledgeMapBuilder;
    private readonly ILogger<ContentAnalyzerService> _logger;
    private const int ChunkSize = 1800;
    private const int ChunkOverlap = 260;
    private const int MaxParallelChunkAnalyses = 3;
    private const int ChunkCompactionBatchSize = 4;
    private const int MaxChunkAnalysesBeforeCompaction = 6;
    private static readonly Regex PageRegex = new(@"\[Page\s+(\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ContentAnalyzerService(
        IOllamaService ollamaService,
        ITokenBudgetPlanner tokenBudgetPlanner,
        IPromptAssembler promptAssembler,
        ITokenEstimator tokenEstimator,
        IOptions<LocalLlmSettings> localLlmSettings,
        IOptions<DocumentUnderstandingOptions> documentUnderstandingOptions,
        IDocumentKnowledgeMapBuilder knowledgeMapBuilder,
        ILogger<ContentAnalyzerService> logger)
    {
        _ollamaService = ollamaService;
        _tokenBudgetPlanner = tokenBudgetPlanner;
        _promptAssembler = promptAssembler;
        _tokenEstimator = tokenEstimator;
        _localLlmSettings = localLlmSettings.Value;
        _documentUnderstandingOptions = documentUnderstandingOptions.Value;
        _knowledgeMapBuilder = knowledgeMapBuilder;
        _logger = logger;
    }

    public async Task<ProcessedContent> AnalyzeContentAsync(string text, IProgress<DocumentProcessingProgressUpdate>? progress = null)
        => await AnalyzeContentCoreAsync(text, null, null, progress);

    public async Task<ProcessedContent> AnalyzeContentAsync(
        string text,
        DocumentUnderstandingResult? understandingResult,
        IProgress<DocumentProcessingProgressUpdate>? progress = null)
        => await AnalyzeContentCoreAsync(text, understandingResult, null, progress);

    public async Task<ProcessedContent> AnalyzeContentAsync(
        string text,
        DocumentUnderstandingResult? understandingResult,
        int? pageCount,
        IProgress<DocumentProcessingProgressUpdate>? progress = null)
        => await AnalyzeContentCoreAsync(text, understandingResult, pageCount, progress);

    public async Task<ProcessedContent> AnalyzeContentAsync(
        string text,
        DocumentUnderstandingRun? understandingRun,
        IProgress<DocumentProcessingProgressUpdate>? progress = null)
        => await AnalyzeContentCoreAsync(text, understandingRun, null, progress);

    private async Task<ProcessedContent> AnalyzeContentCoreAsync(
        string text,
        object? understandingSource,
        int? pageCount,
        IProgress<DocumentProcessingProgressUpdate>? progress = null)
    {
        var analysisStopwatch = Stopwatch.StartNew();
        var coverageStopwatch = Stopwatch.StartNew();
        var aiRefineCalled = false;
        var aiRefineSkippedReason = "not-evaluated";
        var fallbackUsed = false;
        var coverageChunkCount = 0;
        var averageChunkTokens = 0d;
        var contextSelection = ResolveAnalysisContext(text, understandingSource);
        var analysisInput = contextSelection.ContextText;

        try
        {
            var normalizedText = NormalizeText(analysisInput);
            var rawCoverageMap = DocumentCoverageMapBuilder.Build(normalizedText, _localLlmSettings, _tokenEstimator);
            var enrichedCoverageMap = EnrichCoverageMap(rawCoverageMap, normalizedText);
            var cleanCoverageMap = BuildCleanCoverageMap(enrichedCoverageMap);
            coverageChunkCount = cleanCoverageMap.Count;
            averageChunkTokens = cleanCoverageMap.Count > 0
                ? Math.Round(cleanCoverageMap.Average(chunk => chunk.TextTokenCount > 0 ? chunk.TextTokenCount : chunk.EstimatedTokenCount), 2)
                : 0d;
            coverageStopwatch.Stop();
            var budgetPlan = _tokenBudgetPlanner.PlanChunks(cleanCoverageMap, "analysis");
            var selectedCoverageMap = budgetPlan.SelectedChunks.Any() || budgetPlan.OmittedChunks.Any()
                ? budgetPlan.SelectedChunks
                : cleanCoverageMap;
            var coverageMapWithBudgetSelection = MarkBudgetSelection(enrichedCoverageMap, budgetPlan);
            var documentBudgetPlan = _tokenBudgetPlanner.PlanText(NormalizeText(text), "analysis");

            ReportAnalysisProgress(progress, "building-local-analysis", "Lap coverage map", "Dang tao tom tat va y chinh tu evidence cuc bo", cleanCoverageMap.Count, cleanCoverageMap.Count, "chunk", 85);
            var localResult = BuildLocalProcessedContent(normalizedText, coverageMapWithBudgetSelection);
            localResult.PresentationContract = ResolvePresentationContract(understandingSource);
            var refineCandidates = selectedCoverageMap.Any() ? selectedCoverageMap : cleanCoverageMap;

            if (ShouldRunAnalysisRefine(
                    normalizedText,
                    cleanCoverageMap,
                    pageCount,
                    documentBudgetPlan,
                    out aiRefineSkippedReason))
            {
                try
                {
                    aiRefineCalled = true;
                    ReportAnalysisProgress(progress, "refining-analysis", "Tinh chinh AI", "Dang tinh chinh mot lan tu compact evidence", refineCandidates.Count, refineCandidates.Count, "chunk", 93);
                    var refined = await RefineAnalysisFromEvidenceAsync(localResult, refineCandidates);
                    if (refined != null)
                    {
                        localResult = MergeRefinedProcessedContent(localResult, refined, normalizedText, coverageMapWithBudgetSelection);
                        aiRefineSkippedReason = "called";
                    }
                    else
                    {
                        aiRefineSkippedReason = "empty-ai-result";
                    }
                }
                catch (Exception ex)
                {
                    aiRefineSkippedReason = "ollama-unavailable-or-failed";
                    _logger.LogWarning(ex, "AI analysis refine failed; keeping local coverage-based analysis.");
                }
            }
            else if (aiRefineSkippedReason is "page-count-over-limit" or "analysis-token-budget-exceeded")
            {
                _logger.LogInformation(
                    "AI refine skipped for large document: pageCount={PageCount}, estimatedTokens={EstimatedTokens}, maxInputTokens={MaxInputTokens}, reason={Reason}",
                    pageCount,
                    documentBudgetPlan.EstimatedInputTokens,
                    documentBudgetPlan.MaxInputTokens,
                    aiRefineSkippedReason);
            }

            ReportAnalysisProgress(progress, "completed-analysis", "Hoan tat phan tich", "Da tao analysis grounded tu coverage map", cleanCoverageMap.Count, cleanCoverageMap.Count, "chunk", 97);
            return localResult;
        }
        catch (Exception ex)
        {
            fallbackUsed = true;
            coverageStopwatch.Stop();
            _logger.LogError(ex, "Error analyzing content");
            return CreateFallbackProcessedContent(text, EnrichCoverageMap(DocumentCoverageMapBuilder.Build(NormalizeText(text), _localLlmSettings, _tokenEstimator), NormalizeText(text)));
        }
        finally
        {
            analysisStopwatch.Stop();
            if (coverageStopwatch.IsRunning)
            {
                coverageStopwatch.Stop();
            }

            var normalizedLength = string.IsNullOrWhiteSpace(text) ? 0 : NormalizeText(text).Length;
            _logger.LogInformation(
                "Document analysis metrics: DocumentId={DocumentId} ExtractedTextLength={ExtractedTextLength} AnalysisContextPath={AnalysisContextPath} KnowledgeMapTokens={KnowledgeMapTokens} KnowledgeMapReason={KnowledgeMapReason} CoverageChunkCount={CoverageChunkCount} AverageChunkTokens={AverageChunkTokens} AIRefineCalled={AIRefineCalled} AIRefineSkippedReason={AIRefineSkippedReason} AnalysisDurationMs={AnalysisDurationMs} CoverageBuildDurationMs={CoverageBuildDurationMs} FallbackUsed={FallbackUsed}",
                "unknown",
                normalizedLength,
                contextSelection.Path,
                contextSelection.KnowledgeMapTokens,
                contextSelection.Reason,
                coverageChunkCount,
                averageChunkTokens,
                aiRefineCalled,
                aiRefineSkippedReason,
                analysisStopwatch.ElapsedMilliseconds,
                coverageStopwatch.ElapsedMilliseconds,
                fallbackUsed);
        }
    }

    private AnalysisContextSelection ResolveAnalysisContext(string legacyText, object? understandingSource)
    {
        if (!_documentUnderstandingOptions.Enabled || understandingSource == null)
        {
            return new AnalysisContextSelection(legacyText, "LegacyText", null, null);
        }

        try
        {
            var map = understandingSource switch
            {
                DocumentUnderstandingResult result => _knowledgeMapBuilder.Build(result),
                DocumentUnderstandingRun run => _knowledgeMapBuilder.Build(run),
                _ => new KnowledgeMapBuildResult
                {
                    IsUsable = false,
                    UnusableReason = "unsupported-understanding-source"
                }
            };

            if (map.IsUsable && !string.IsNullOrWhiteSpace(map.Text))
            {
                _logger.LogInformation(
                    "Document analysis context path selected: {AnalysisContextPath}; knowledgeMapTokens={KnowledgeMapTokens}; warnings={WarningCount}",
                    "KnowledgeMap",
                    map.EstimatedTokens,
                    map.Warnings.Count);
                return new AnalysisContextSelection(map.Text, "KnowledgeMap", map.EstimatedTokens, null);
            }

            _logger.LogInformation(
                "Document analysis context path selected: {AnalysisContextPath}; reason={Reason}",
                "KnowledgeMapFallback",
                map.UnusableReason ?? "knowledge-map-unusable");
            return new AnalysisContextSelection(legacyText, "KnowledgeMapFallback", map.EstimatedTokens, map.UnusableReason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Knowledge Map build failed; falling back to legacy extracted text.");
            return new AnalysisContextSelection(legacyText, "KnowledgeMapFallback", null, ex.Message);
        }
    }

    private static PresentationExtractionContract? ResolvePresentationContract(object? understandingSource)
    {
        if (understandingSource is DocumentUnderstandingResult result)
        {
            return result.PresentationContract;
        }

        if (understandingSource is DocumentUnderstandingRun run && !string.IsNullOrWhiteSpace(run.ResultJson))
        {
            try
            {
                var payload = System.Text.Json.JsonSerializer.Deserialize<UnderstandingContractPayload>(
                    run.ResultJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return payload?.PresentationContract;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public async Task<string> SummarizeTextAsync(string text)
    {
        try
        {
            var analyzed = await AnalyzeContentAsync(text);
            return analyzed.Summary ?? "Khong tao duoc tom tat";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error summarizing text");
            return "Error generating summary";
        }
    }

    public async Task<List<string>> ExtractKeyPointsAsync(string text)
    {
        try
        {
            var analyzed = await AnalyzeContentAsync(text);
            return analyzed.KeyPoints.Any()
                ? analyzed.KeyPoints
                : new List<string> { "Unable to extract key points" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting key points");
            return new List<string> { "Error extracting key points" };
        }
    }

    private ProcessedContent BuildLocalProcessedContent(string normalizedText, List<DocumentCoverageChunk> coverageMap)
    {
        var cleanCoverage = BuildCleanCoverageMap(coverageMap);
        var localFacts = cleanCoverage
            .OrderByDescending(chunk => chunk.ChunkQualityScore)
            .ThenBy(chunk => chunk.ChunkNumber)
            .SelectMany(chunk => chunk.KeyFacts)
            .Where(fact => !string.IsNullOrWhiteSpace(fact))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(18)
            .ToList();
        var topics = cleanCoverage
            .SelectMany(chunk => chunk.ConceptAnchors.Concat(chunk.Keywords).Concat(new[] { chunk.HeadingText, chunk.NormalizedHeading, chunk.Label }))
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Select(topic => NormalizeTopic(topic!))
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        var keyPoints = localFacts.Any()
            ? localFacts
            : cleanCoverage
                .Select(chunk => !string.IsNullOrWhiteSpace(chunk.Summary) ? chunk.Summary : chunk.EvidenceExcerpt)
                .Where(point => !string.IsNullOrWhiteSpace(point))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
        var summary = BuildLocalSummary(cleanCoverage, normalizedText);
        var metadata = BuildProcessingMetadata(normalizedText, coverageMap, null);

        return new ProcessedContent
        {
            MainTopics = topics.Any() ? topics : new List<string> { "Noi dung tai lieu" },
            KeyPoints = keyPoints.Any() ? keyPoints : normalizedText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(8).ToList(),
            Summary = summary,
            Language = metadata.Language ?? DetectLanguage(normalizedText),
            DocumentType = metadata.DocumentType,
            Title = metadata.Title,
            MainContentStartPage = metadata.MainContentStartPage,
            Structure = metadata.Structure,
            ExcludedContent = metadata.ExcludedContent,
            CoverageMap = cleanCoverage
        };
    }

    private bool ShouldRunAnalysisRefine(
        string normalizedText,
        IReadOnlyCollection<DocumentCoverageChunk> cleanCoverageMap,
        int? pageCount,
        TokenBudgetPlan documentBudgetPlan,
        out string reason)
    {
        if (!_localLlmSettings.EnableAnalysisRefine)
        {
            reason = "disabled";
            return false;
        }

        if (pageCount > 50)
        {
            reason = "page-count-over-limit";
            return false;
        }

        if (!documentBudgetPlan.IsWithinBudget)
        {
            reason = "analysis-token-budget-exceeded";
            return false;
        }

        if (normalizedText.Length < _localLlmSettings.MinTextLengthForAIRefine)
        {
            reason = "text-too-short";
            return false;
        }

        if (cleanCoverageMap.Count < _localLlmSettings.MinCoverageChunksForAIRefine)
        {
            reason = "not-enough-coverage-chunks";
            return false;
        }

        reason = "eligible";
        return true;
    }

    private async Task<ProcessedContent?> RefineAnalysisFromEvidenceAsync(ProcessedContent localContent, IReadOnlyList<DocumentCoverageChunk> selectedChunks)
    {
        var compactEvidence = BuildCompactAnalysisEvidenceBlock(selectedChunks);
        if (string.IsNullOrWhiteSpace(compactEvidence))
        {
            return null;
        }

        var prompt = $@"Refine this local educational document analysis using only the compact evidence below.

Do not add facts, topics, or claims unsupported by the evidence.
Preserve concrete local key facts. If evidence is thin, return conservative output.
If evidence mentions low confidence or needs review, mark that uncertainty instead of treating it as verified.
Figure and diagram descriptions are AI-generated visual descriptions; use them carefully and do not infer details beyond the description.

Local analysis:
Summary: {localContent.Summary}
Topics: {string.Join("; ", localContent.MainTopics)}
Key points:
- {string.Join(Environment.NewLine + "- ", localContent.KeyPoints.Take(12))}

Compact evidence:
{compactEvidence}

Return JSON only:
{{
  ""mainTopics"": [""supported topic""],
  ""keyPoints"": [""supported key point""],
  ""summary"": ""2-4 sentence Vietnamese summary grounded in evidence"",
  ""language"": ""Vietnamese or English or mixed""
}}";

        return await _ollamaService.GenerateStructuredResponseAsync<ProcessedContent>(
            prompt,
            "You refine local document analysis from compact evidence only. Never invent unsupported facts. Mark low-confidence evidence as needing review and treat figure descriptions as cautious AI-generated observations.",
            OllamaModelProfile.Analysis);
    }

    private static string BuildCompactAnalysisEvidenceBlock(IReadOnlyList<DocumentCoverageChunk> chunks)
        => string.Join(
            Environment.NewLine + Environment.NewLine,
            chunks
                .OrderByDescending(chunk => chunk.ChunkQualityScore)
                .ThenBy(chunk => chunk.ChunkNumber)
                .Take(12)
                .OrderBy(chunk => chunk.ChunkNumber)
                .Select(chunk => $@"[{chunk.ChunkId}]
heading: {chunk.HeadingPath ?? chunk.HeadingText ?? chunk.NormalizedHeading ?? "none"}
keywords: {string.Join(", ", chunk.Keywords.Take(8))}
conceptAnchors: {string.Join(", ", chunk.ConceptAnchors.Take(6))}
keyFacts:
- {string.Join(Environment.NewLine + "- ", chunk.KeyFacts.Take(4).DefaultIfEmpty("No key facts extracted."))}
excerpt: {chunk.EvidenceExcerpt}"));

    private ProcessedContent MergeRefinedProcessedContent(
        ProcessedContent localContent,
        ProcessedContent refined,
        string normalizedText,
        List<DocumentCoverageChunk> coverageMap)
    {
        var supportTokens = BuildCoverageSupportTokens(coverageMap);
        var refinedTopics = (refined.MainTopics ?? new List<string>())
            .Where(topic => IsSupportedByCoverage(topic, supportTokens))
            .ToList();
        var refinedKeyPoints = (refined.KeyPoints ?? new List<string>())
            .Where(point => IsSupportedByCoverage(point, supportTokens))
            .ToList();

        localContent.MainTopics = localContent.MainTopics
            .Concat(refinedTopics)
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        localContent.KeyPoints = localContent.KeyPoints
            .Concat(refinedKeyPoints)
            .Where(point => !string.IsNullOrWhiteSpace(point))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(18)
            .ToList();

        if (!string.IsNullOrWhiteSpace(refined.Summary) && IsSupportedByCoverage(refined.Summary, supportTokens))
        {
            localContent.Summary = refined.Summary;
        }

        if (!string.IsNullOrWhiteSpace(refined.Language))
        {
            localContent.Language = refined.Language;
        }

        var metadata = BuildProcessingMetadata(normalizedText, coverageMap, localContent.Language);
        localContent.DocumentType = metadata.DocumentType;
        localContent.Title = metadata.Title;
        localContent.MainContentStartPage = metadata.MainContentStartPage;
        localContent.Structure = metadata.Structure;
        localContent.ExcludedContent = metadata.ExcludedContent;
        localContent.CoverageMap = BuildCleanCoverageMap(coverageMap);
        return localContent;
    }

    private static HashSet<string> BuildCoverageSupportTokens(IEnumerable<DocumentCoverageChunk> coverageMap)
        => DocumentCoverageMapBuilder.BuildSearchTokens(coverageMap.Select(chunk => string.Join(
            " ",
            chunk.HeadingPath,
            chunk.HeadingText,
            chunk.NormalizedHeading,
            string.Join(" ", chunk.Keywords),
            string.Join(" ", chunk.ConceptAnchors),
            string.Join(" ", chunk.KeyFacts),
            chunk.EvidenceExcerpt)).ToArray());

    private static bool IsSupportedByCoverage(string? value, HashSet<string> supportTokens)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var tokens = DocumentCoverageMapBuilder.BuildSearchTokens(value);
        if (tokens.Count == 0)
        {
            return false;
        }

        return tokens.Intersect(supportTokens, StringComparer.OrdinalIgnoreCase).Count() >= Math.Min(2, tokens.Count);
    }

    private static string BuildLocalSummary(IReadOnlyList<DocumentCoverageChunk> cleanCoverage, string normalizedText)
    {
        var facts = cleanCoverage
            .OrderByDescending(chunk => chunk.ChunkQualityScore)
            .ThenBy(chunk => chunk.ChunkNumber)
            .SelectMany(chunk => chunk.KeyFacts)
            .Where(fact => !string.IsNullOrWhiteSpace(fact))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        if (facts.Any())
        {
            return string.Join(" ", facts);
        }

        var words = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(90);
        return string.Join(" ", words) + (normalizedText.Length > 0 ? "..." : string.Empty);
    }

    private static string NormalizeTopic(string value)
    {
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        return normalized.Length <= 90 ? normalized : normalized[..90].TrimEnd() + "...";
    }

    private async Task<List<ChunkAnalysis>> AnalyzeChunksInParallelAsync(
        IReadOnlyList<string> chunks,
        IProgress<DocumentProcessingProgressUpdate>? progress)
    {
        if (chunks.Count == 0)
        {
            return new List<ChunkAnalysis>();
        }

        var maxParallelism = Math.Min(MaxParallelChunkAnalyses, Math.Max(1, chunks.Count));
        var results = new ChunkAnalysis?[chunks.Count];
        var completed = 0;
        using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);

        ReportAnalysisProgress(
            progress,
            "analyzing-chunks",
            "Phan tich noi dung",
            $"Dang phan tich {chunks.Count} chunk voi toi da {maxParallelism} luong song song",
            0,
            chunks.Count,
            "chunk",
            5);

        var tasks = chunks.Select((chunk, index) => AnalyzeChunkWithProgressAsync(
            chunk,
            index,
            chunks.Count,
            semaphore,
            results,
            () => Interlocked.Increment(ref completed),
            progress,
            maxParallelism));

        await Task.WhenAll(tasks);

        return results
            .Where(result => result != null)
            .Cast<ChunkAnalysis>()
            .ToList();
    }

    private async Task AnalyzeChunkWithProgressAsync(
        string chunk,
        int index,
        int totalChunks,
        SemaphoreSlim semaphore,
        ChunkAnalysis?[] results,
        Func<int> onCompleted,
        IProgress<DocumentProcessingProgressUpdate>? progress,
        int maxParallelism)
    {
        await semaphore.WaitAsync();
        try
        {
            results[index] = await AnalyzeChunkAsync(chunk, index + 1, totalChunks);
        }
        finally
        {
            semaphore.Release();

            var completedCount = onCompleted();
            ReportAnalysisProgress(
                progress,
                "analyzing-chunks",
                "Phan tich noi dung",
                $"Da xong {completedCount}/{totalChunks} chunk, dang chay toi da {maxParallelism} luong song song",
                completedCount,
                totalChunks,
                "chunk",
                MapProgress(5, 78, completedCount, totalChunks));
        }
    }

    private async Task<ChunkAnalysis?> AnalyzeChunkAsync(string chunk, int chunkNumber, int totalChunks)
    {
        try
        {
            var systemPrompt = "You are an educational content analyzer. Use only the information explicitly present in the provided text chunk. Do not invent, supplement, or resolve missing facts with outside knowledge. Write concise Vietnamese output that is factual, grounded, and useful for later quiz generation.";

            var prompt = $@"Analyze chunk {chunkNumber}/{totalChunks} from a larger educational document.

Goals:
1. Extract 3-6 specific topics from this chunk (use concrete names, avoid generic labels)
2. Extract 5-10 concrete key points with factual details such as definitions, formulas, rules, dates, ordered steps, causes, and effects when present
3. Write a concise Vietnamese summary (2-4 sentences)
4. Identify the language of the chunk
5. Focus on what can later be used to generate accurate quiz questions
6. Each topic should be a concise noun phrase (2-7 words), non-overlapping, and directly grounded in the chunk
7. Do NOT output vague labels like ""Tong quan"", ""Noi dung chinh"", ""Kien thuc co ban"" unless the chunk explicitly uses those exact terms
8. Preserve dates, formulas, named entities, ordered steps, and definitions whenever they appear
9. Use only information from the chunk content below
10. Do not infer facts, fill missing details, or add outside knowledge
11. If a statement in the chunk is incomplete, unclear, or internally inconsistent, mention it conservatively in summary or key points instead of guessing
12. Keep wording accurate, clear, and concise

Chunk content:
{chunk}

Respond in valid JSON only:
{{
    ""topics"": [""chu de cu the 1"", ""chu de cu the 2""],
    ""keyPoints"": [""y chinh co du kien 1"", ""y chinh co du kien 2""],
    ""summary"": ""tom tat tieng Viet ngan gon"",
    ""language"": ""Vietnamese or English or mixed""
}}";

            var result = await _ollamaService.GenerateStructuredResponseAsync<ChunkAnalysis>(prompt, systemPrompt, OllamaModelProfile.Analysis);
            if (result == null)
            {
                _logger.LogWarning("Failed to analyze chunk {ChunkNumber}/{TotalChunks}", chunkNumber, totalChunks);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error analyzing chunk {ChunkNumber}/{TotalChunks}", chunkNumber, totalChunks);
            return null;
        }
    }

    private async Task<ProcessedContent?> ConsolidateChunkAnalysesAsync(List<ChunkAnalysis> chunkAnalyses)
    {
        var consolidatedInput = string.Join("\n\n", chunkAnalyses.Select((chunk, index) => $@"Chunk {index + 1}:
Topics: {string.Join(", ", chunk.Topics ?? new List<string>())}
Key points:
- {string.Join("\n- ", chunk.KeyPoints ?? new List<string>())}
Summary: {chunk.Summary}
Language: {chunk.Language}"));

        var systemPrompt = "You are an educational content analyst. Merge chunk analyses into one grounded full-document analysis in Vietnamese. Use only the information present in the supplied chunk analyses. Do not invent facts or resolve ambiguity by guessing.";
        var prompt = $@"The following notes were extracted from multiple chunks of the SAME full document.
Merge them into one complete analysis.

Requirements:
1. Main topics: 4-8 specific and non-overlapping topics covering the full document
2. Key points: 10-18 important points without duplicates and with broad section coverage
3. Summary: a coherent Vietnamese summary (4-8 sentences) covering the overall document, not just one part
4. Language: identify the main language of the full document
5. Preserve important information from later chunks too, not only the first chunks
6. Prefer concrete domain concepts over generic wording
7. Merge synonymous topics into one canonical topic name
8. Each main topic should be 2-7 words and suitable for downstream topic-tag mapping
9. Use only the information found in the chunk analyses below
10. If chunk analyses are incomplete, unclear, or slightly inconsistent, keep the wording conservative and do not guess the missing facts
11. Prefer precision and brevity over broad but vague summaries

Chunk analyses:
{consolidatedInput}

Respond in JSON format:
{{
  ""mainTopics"": [""topic1"", ""topic2""],
  ""keyPoints"": [""point1"", ""point2""],
  ""summary"": ""summary text"",
  ""language"": ""language name""
}}";

        return await _ollamaService.GenerateStructuredResponseAsync<ProcessedContent>(prompt, systemPrompt);
    }

    private ProcessedContent EnsureProcessedContentQuality(
        ProcessedContent processed,
        List<ChunkAnalysis> chunkAnalyses,
        string normalizedText,
        List<DocumentCoverageChunk> coverageMap)
    {
        var localMerged = MergeChunkAnalysesLocally(chunkAnalyses, normalizedText, coverageMap);

        processed.MainTopics = processed.MainTopics
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        processed.KeyPoints = processed.KeyPoints
            .Where(point => !string.IsNullOrWhiteSpace(point))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(18)
            .ToList();

        if (!processed.MainTopics.Any())
        {
            processed.MainTopics = localMerged.MainTopics;
        }

        if (!processed.KeyPoints.Any())
        {
            processed.KeyPoints = localMerged.KeyPoints;
        }

        if (string.IsNullOrWhiteSpace(processed.Summary))
        {
            processed.Summary = localMerged.Summary;
        }

        if (string.IsNullOrWhiteSpace(processed.Language))
        {
            processed.Language = localMerged.Language;
        }

        var metadata = BuildProcessingMetadata(normalizedText, coverageMap, processed.Language);
        processed.Language = metadata.Language ?? processed.Language;
        processed.DocumentType = metadata.DocumentType;
        processed.Title = metadata.Title;
        processed.MainContentStartPage = metadata.MainContentStartPage;
        processed.Structure = metadata.Structure;
        processed.ExcludedContent = metadata.ExcludedContent;
        processed.CoverageMap = BuildCleanCoverageMap(coverageMap);

        return processed;
    }

    private ProcessedContent MergeChunkAnalysesLocally(
        List<ChunkAnalysis> chunkAnalyses,
        string normalizedText,
        List<DocumentCoverageChunk> coverageMap)
    {
        var topics = chunkAnalyses
            .SelectMany(chunk => chunk.Topics ?? new List<string>())
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        var keyPoints = chunkAnalyses
            .SelectMany(chunk => chunk.KeyPoints ?? new List<string>())
            .Where(point => !string.IsNullOrWhiteSpace(point))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .ToList();

        var summary = string.Join(" ", chunkAnalyses
            .Select(chunk => chunk.Summary)
            .Where(summaryPart => !string.IsNullOrWhiteSpace(summaryPart))
            .Take(6));

        var language = chunkAnalyses
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk.Language))
            .GroupBy(chunk => chunk.Language!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault() ?? "Unknown";

        var metadata = BuildProcessingMetadata(normalizedText, coverageMap, language);

        return new ProcessedContent
        {
            MainTopics = topics.Any() ? topics : new List<string> { "Tong quan noi dung" },
            KeyPoints = keyPoints.Any() ? keyPoints : normalizedText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(8).ToList(),
            Summary = !string.IsNullOrWhiteSpace(summary) ? summary : string.Join(" ", normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(120)) + "...",
            Language = metadata.Language ?? language,
            DocumentType = metadata.DocumentType,
            Title = metadata.Title,
            MainContentStartPage = metadata.MainContentStartPage,
            Structure = metadata.Structure,
            ExcludedContent = metadata.ExcludedContent,
            CoverageMap = BuildCleanCoverageMap(coverageMap)
        };
    }

    private static string NormalizeText(string text)
    {
        return TextCleanupUtility.NormalizeForAi(text, preserveLineBreaks: true);
    }

    private static List<string> SplitIntoChunks(string content, int chunkSize, int overlap)
        => DocumentStructureChunker.SplitIntoChunks(content, chunkSize, overlap);

    private List<ChunkAnalysis> CompactChunkAnalysesLocally(List<ChunkAnalysis> chunkAnalyses, IProgress<DocumentProcessingProgressUpdate>? progress)
    {
        var workingSet = chunkAnalyses;
        while (workingSet.Count > MaxChunkAnalysesBeforeCompaction)
        {
            var groups = workingSet.Chunk(ChunkCompactionBatchSize).ToList();
            var compacted = new List<ChunkAnalysis>(groups.Count);

            for (var index = 0; index < groups.Count; index++)
            {
                ReportAnalysisProgress(progress, "compacting-analysis", "Nen ket qua phan tich", $"Dang nen cum phan tich {index + 1}/{groups.Count}", index + 1, groups.Count, "cum", MapProgress(80, 90, index + 1, groups.Count));
                compacted.Add(ConvertProcessedToChunkAnalysis(MergeChunkAnalysesLocally(groups[index].ToList(), string.Empty, new List<DocumentCoverageChunk>())));
            }

            workingSet = compacted;
        }

        return workingSet;
    }

    private ProcessedContent CreateFallbackProcessedContent(string text, List<DocumentCoverageChunk> coverageMap)
    {
        return BuildLocalProcessedContent(NormalizeText(text), coverageMap);
    }

    private static List<string> BuildAnalysisChunks(List<DocumentCoverageChunk> cleanCoverageMap, bool includeFullText)
        => cleanCoverageMap
            .OrderBy(chunk => chunk.ChunkNumber)
            .Select(chunk => string.Join(
                Environment.NewLine,
                new[]
                {
                    $"[{chunk.ChunkId}] sectionKey={chunk.SectionKey ?? chunk.ChunkId}; coverageZone={chunk.CoverageZone}; pageRange={BuildPageRange(chunk)}; qualityScore={chunk.ChunkQualityScore}; estimatedTokens={chunk.EstimatedTokenCount}",
                    chunk.Label,
                    chunk.Summary,
                    chunk.EvidenceExcerpt,
                    string.Join(" ", chunk.KeyFacts),
                    includeFullText
                        ? $"text:\n<<<\n{GetChunkPromptText(chunk)}\n>>>"
                        : null
                }.Where(value => !string.IsNullOrWhiteSpace(value))))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

    private static List<DocumentCoverageChunk> MarkBudgetSelection(List<DocumentCoverageChunk> coverageMap, TokenBudgetPlan budgetPlan)
    {
        if (!budgetPlan.SelectedChunks.Any() && !budgetPlan.OmittedChunks.Any())
        {
            return coverageMap;
        }

        var omittedIds = budgetPlan.OmittedChunks
            .Select(chunk => chunk.ChunkId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plannedById = budgetPlan.SelectedChunks
            .Concat(budgetPlan.OmittedChunks)
            .GroupBy(chunk => chunk.ChunkId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return coverageMap
            .Select(chunk =>
            {
                var cloned = CloneChunk(chunk);
                if (plannedById.TryGetValue(cloned.ChunkId, out var planned))
                {
                    cloned.EstimatedTokenCount = planned.EstimatedTokenCount;
                    cloned.TokenEfficiencyScore = planned.TokenEfficiencyScore;
                    cloned.ChunkQualityScore = planned.ChunkQualityScore;
                    cloned.KeyFactDensityScore = planned.KeyFactDensityScore;
                    cloned.Text = planned.Text ?? cloned.Text;
                    cloned.NormalizedText = planned.NormalizedText ?? cloned.NormalizedText;
                    cloned.TextTokenCount = planned.TextTokenCount > 0 ? planned.TextTokenCount : cloned.TextTokenCount;
                    cloned.IsEligibleForQuestionGeneration = planned.IsEligibleForQuestionGeneration;
                }

                if (omittedIds.Contains(cloned.ChunkId))
                {
                    cloned.Warnings.Add("Chunk omitted from local Qwen analysis prompt because of quality or token budget.");
                }

                return cloned;
            })
            .ToList();
    }

    private static string BuildPageRange(DocumentCoverageChunk chunk)
    {
        var start = chunk.SourcePageStart ?? chunk.StartPage;
        var end = chunk.SourcePageEnd ?? chunk.EndPage;
        if (start.HasValue && end.HasValue)
        {
            return start == end ? start.Value.ToString() : $"{start}-{end}";
        }

        return start?.ToString() ?? end?.ToString() ?? "unknown";
    }

    private static string GetChunkPromptText(DocumentCoverageChunk chunk)
        => !string.IsNullOrWhiteSpace(chunk.NormalizedText)
            ? chunk.NormalizedText!
            : !string.IsNullOrWhiteSpace(chunk.Text)
                ? chunk.Text!
                : chunk.EvidenceExcerpt;

    private static List<DocumentCoverageChunk> BuildCleanCoverageMap(List<DocumentCoverageChunk> coverageMap)
    {
        var clean = coverageMap
            .Where(chunk => ShouldIncludeChunkInCoverage(chunk))
            .OrderBy(chunk => chunk.ChunkNumber)
            .ToList();

        if (clean.Count > 0)
        {
            return clean;
        }

        return coverageMap
            .OrderByDescending(chunk => chunk.TeachabilityScore)
            .ThenBy(chunk => chunk.ChunkNumber)
            .Take(3)
            .ToList();
    }

    private static bool ShouldIncludeChunkInCoverage(DocumentCoverageChunk chunk)
    {
        if (chunk.TeachabilityScore < 34)
        {
            return false;
        }

        return chunk.Classification is ChunkClassifications.LessonContent
            or ChunkClassifications.Example
            or ChunkClassifications.Exercise;
    }

    private static (int Score, int KeyFactDensityScore, List<string> Warnings) ScoreChunkQuality(DocumentCoverageChunk chunk, string classification, int teachabilityScore)
    {
        var text = $"{chunk.Label} {chunk.Summary} {chunk.EvidenceExcerpt} {string.Join(" ", chunk.KeyFacts)} {GetChunkPromptText(chunk)}";
        var warnings = new List<string>();
        var lengthScore = ScoreTextLength(text.Length);
        var signalRatio = ScoreSignalRatio(text);
        var noiseScore = ScoreNoise(text, classification);
        var keyFactDensity = ScoreKeyFactDensity(chunk, text);
        var metadataScore = ScoreMetadata(chunk);
        var score = (int)Math.Round(
            (0.26d * lengthScore)
            + (0.24d * signalRatio)
            + (0.20d * (100 - noiseScore))
            + (0.18d * keyFactDensity)
            + (0.12d * metadataScore));

        score = (int)Math.Round((score * 0.75d) + (teachabilityScore * 0.25d));
        if (text.Length < 120)
        {
            warnings.Add("Chunk text is short.");
        }

        if (noiseScore >= 45)
        {
            warnings.Add("Chunk has likely OCR or formatting noise.");
        }

        if (keyFactDensity < 25)
        {
            warnings.Add("Chunk has low key fact density.");
        }

        if (classification is ChunkClassifications.Noise or ChunkClassifications.FrontMatter or ChunkClassifications.TableOfContents or ChunkClassifications.Reference)
        {
            warnings.Add($"Chunk classified as {classification}.");
        }

        if (ContainsLowConfidenceTableOrFormula(text))
        {
            warnings.Add("Chunk contains low-confidence table or formula evidence; do not use for exact calculation without review.");
        }

        return (Math.Clamp(score, 0, 100), keyFactDensity, warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static bool ContainsLowConfidenceTableOrFormula(string text)
        => text.Contains("TableLowConfidence", StringComparison.OrdinalIgnoreCase)
            || text.Contains("FormulaCandidate", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Formula Candidates", StringComparison.OrdinalIgnoreCase)
            || text.Contains("LOW CONFIDENCE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("REVIEW REQUIRED", StringComparison.OrdinalIgnoreCase)
            || text.Contains("do not infer missing table cells", StringComparison.OrdinalIgnoreCase)
            || text.Contains("do not repair notation", StringComparison.OrdinalIgnoreCase);

    private static int ScoreTextLength(int length)
    {
        if (length < 80)
        {
            return 10;
        }

        if (length < 240)
        {
            return 45;
        }

        if (length <= 1400)
        {
            return 100;
        }

        if (length <= 2600)
        {
            return 82;
        }

        return 60;
    }

    private static int ScoreSignalRatio(string text)
    {
        var words = Regex.Matches(text, @"\b[\p{L}\p{N}]{2,}\b").Count;
        if (words == 0)
        {
            return 0;
        }

        var signalMatches = Regex.Matches(text, @"\b(la|gom|bao gom|dinh nghia|concept|definition|nguyen nhan|ket qua|because|therefore|formula|cong thuc|vi du|example|step|buoc)\b|\d", RegexOptions.IgnoreCase).Count;
        var ratio = Math.Min(1d, signalMatches / Math.Max(8d, words * 0.08d));
        return Math.Clamp((int)Math.Round(ratio * 100), 0, 100);
    }

    private static int ScoreNoise(string text, string classification)
    {
        var score = 0;
        if (TextCleanupUtility.HasNoisyArtifacts(text))
        {
            score += 42;
        }

        if (Regex.IsMatch(text, @"(?:[_=~|]{3,}|[^\p{L}\p{N}\s\.,;:\-\(\)\[\]/%]{5,})", RegexOptions.IgnoreCase))
        {
            score += 22;
        }

        if (LooksMostlyNames(text))
        {
            score += 16;
        }

        if (classification is ChunkClassifications.Noise or ChunkClassifications.FrontMatter)
        {
            score += 28;
        }

        return Math.Clamp(score, 0, 100);
    }

    private static int ScoreKeyFactDensity(DocumentCoverageChunk chunk, string text)
    {
        var words = Math.Max(1, Regex.Matches(text, @"\b[\p{L}\p{N}]{2,}\b").Count);
        var factWeight = chunk.KeyFacts.Count * 18;
        var numericWeight = Math.Min(24, Regex.Matches(text, @"\d").Count * 2);
        var definitionWeight = Regex.IsMatch(text, @"\b(la|dinh nghia|definition|concept|formula|cong thuc|nguyen nhan|ket qua)\b", RegexOptions.IgnoreCase) ? 24 : 0;
        var density = Math.Min(100d, (factWeight + numericWeight + definitionWeight) / Math.Max(1d, words / 90d));
        return Math.Clamp((int)Math.Round(density), 0, 100);
    }

    private static int ScoreMetadata(DocumentCoverageChunk chunk)
    {
        var score = 35;
        if (!string.IsNullOrWhiteSpace(chunk.SectionKey))
        {
            score += 20;
        }

        if (!string.IsNullOrWhiteSpace(chunk.HeadingText) || !string.IsNullOrWhiteSpace(chunk.HeadingPath))
        {
            score += 20;
        }

        if (chunk.StartPage.HasValue || chunk.SourcePageStart.HasValue)
        {
            score += 15;
        }

        if (chunk.IsPrimarySection)
        {
            score += 10;
        }

        return Math.Clamp(score, 0, 100);
    }

    private static List<DocumentCoverageChunk> EnrichCoverageMap(List<DocumentCoverageChunk> coverageMap, string normalizedText)
    {
        var documentType = DetectDocumentType(normalizedText, coverageMap);
        var headingBySection = coverageMap
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk.SectionKey))
            .GroupBy(chunk => chunk.SectionKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(chunk => chunk.ChunkNumber).First().HeadingText ?? group.OrderBy(chunk => chunk.ChunkNumber).First().NormalizedHeading ?? group.Key,
                StringComparer.OrdinalIgnoreCase);

        var result = new List<DocumentCoverageChunk>(coverageMap.Count);
        foreach (var chunk in coverageMap)
        {
            var classification = ClassifyChunk(chunk, documentType);
            var (score, positives, negatives) = ScoreTeachability(chunk, classification, documentType);
            var cloned = CloneChunk(chunk);
            cloned.Classification = classification;
            cloned.TeachabilityScore = Math.Clamp(score, 0, 100);
            var quality = ScoreChunkQuality(cloned, classification, cloned.TeachabilityScore);
            cloned.ChunkQualityScore = quality.Score;
            cloned.KeyFactDensityScore = quality.KeyFactDensityScore;
            cloned.PositiveSignals = positives;
            cloned.NegativeSignals = negatives;
            cloned.SelectionReason = BuildSelectionReason(cloned, headingBySection.TryGetValue(chunk.SectionKey ?? string.Empty, out var heading) ? heading : null);
            cloned.StartPage = TryGetChunkStartPage(chunk);
            cloned.EndPage = TryGetChunkEndPage(chunk);
            cloned.SourcePageStart = cloned.StartPage;
            cloned.SourcePageEnd = cloned.EndPage;
            cloned.CoverageZone = string.IsNullOrWhiteSpace(cloned.CoverageZone) ? cloned.Zone : cloned.CoverageZone;
            cloned.IsEligibleForQuestionGeneration = ShouldIncludeChunkInCoverage(cloned) && cloned.ChunkQualityScore >= 35;
            cloned.Warnings = quality.Warnings;
            result.Add(cloned);
        }

        return result;
    }

    private static DocumentCoverageChunk CloneChunk(DocumentCoverageChunk chunk)
    {
        return new DocumentCoverageChunk
        {
            ChunkNumber = chunk.ChunkNumber,
            ChunkId = chunk.ChunkId,
            Zone = chunk.Zone,
            CoverageZone = string.IsNullOrWhiteSpace(chunk.CoverageZone) ? chunk.Zone : chunk.CoverageZone,
            Label = chunk.Label,
            HeadingKind = chunk.HeadingKind,
            HeadingLevel = chunk.HeadingLevel,
            HeadingMarker = chunk.HeadingMarker,
            HeadingText = chunk.HeadingText,
            NormalizedHeading = chunk.NormalizedHeading,
            HeadingPath = chunk.HeadingPath,
            ParentHeadingPath = chunk.ParentHeadingPath,
            SectionKey = chunk.SectionKey,
            IsPrimarySection = chunk.IsPrimarySection,
            Classification = chunk.Classification,
            TeachabilityScore = chunk.TeachabilityScore,
            ChunkQualityScore = chunk.ChunkQualityScore,
            EstimatedTokenCount = chunk.EstimatedTokenCount,
            TokenEfficiencyScore = chunk.TokenEfficiencyScore,
            KeyFactDensityScore = chunk.KeyFactDensityScore,
            PositiveSignals = chunk.PositiveSignals.ToList(),
            NegativeSignals = chunk.NegativeSignals.ToList(),
            SelectionReason = chunk.SelectionReason,
            StartPage = chunk.StartPage,
            EndPage = chunk.EndPage,
            SourcePageStart = chunk.SourcePageStart,
            SourcePageEnd = chunk.SourcePageEnd,
            IsEligibleForQuestionGeneration = chunk.IsEligibleForQuestionGeneration,
            Warnings = chunk.Warnings.ToList(),
            Summary = chunk.Summary,
            EvidenceExcerpt = chunk.EvidenceExcerpt,
            Keywords = chunk.Keywords.ToList(),
            ConceptAnchors = chunk.ConceptAnchors.ToList(),
            ChunkingReason = chunk.ChunkingReason,
            KeyFacts = chunk.KeyFacts.ToList(),
            Text = chunk.Text,
            NormalizedText = chunk.NormalizedText,
            TextTokenCount = chunk.TextTokenCount
        };
    }

    private static string DetectDocumentType(string text, IReadOnlyCollection<DocumentCoverageChunk> chunks)
    {
        var lowered = text.ToLowerInvariant();
        if (Regex.IsMatch(lowered, @"\b(doi moi|quan diem|nguyen ly|chuong|chương|bai|bài|unit|lesson|exercise|ví dụ|vi du)\b", RegexOptions.IgnoreCase))
        {
            return DocumentTypes.Textbook;
        }

        if (Regex.IsMatch(lowered, @"\b(abstract|introduction|methodology|results|discussion|references|doi|et al\.)\b", RegexOptions.IgnoreCase))
        {
            return DocumentTypes.ResearchPaper;
        }

        if (Regex.IsMatch(lowered, @"\b(report|bao cao|executive summary|findings|recommendation)\b", RegexOptions.IgnoreCase))
        {
            return DocumentTypes.Report;
        }

        if (Regex.IsMatch(lowered, @"\b(user guide|manual|huong dan|hướng dẫn|installation|troubleshooting)\b", RegexOptions.IgnoreCase))
        {
            return DocumentTypes.Manual;
        }

        if (chunks.Any(chunk => chunk.HeadingKind is "chapter" or "chuong" or "unit" or "phan" or "bai"))
        {
            return DocumentTypes.Textbook;
        }

        return DocumentTypes.LectureNote;
    }

    private static string DetectLanguage(string text)
    {
        if (Regex.IsMatch(text, @"\b(và|của|những|được|trong|không|bài|chương|phần|ví dụ)\b", RegexOptions.IgnoreCase))
        {
            return "Vietnamese";
        }

        if (Regex.IsMatch(text, @"\b(the|and|with|chapter|section|example|definition|therefore)\b", RegexOptions.IgnoreCase))
        {
            return "English";
        }

        return "Unknown";
    }

    private static string? DetectTitle(string text)
    {
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = line.Trim();
            if (normalized.Length < 12 || normalized.Length > 180)
            {
                continue;
            }

            if (normalized.StartsWith("[Page", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Regex.IsMatch(normalized, @"^(?:muc luc|table of contents|noi dung|nha xuat ban|publisher)", RegexOptions.IgnoreCase))
            {
                continue;
            }

            return normalized;
        }

        return null;
    }

    private static string ClassifyChunk(DocumentCoverageChunk chunk, string documentType)
    {
        var heading = $"{chunk.HeadingText} {chunk.NormalizedHeading} {chunk.Label}".ToLowerInvariant();
        var text = $"{chunk.Summary} {chunk.EvidenceExcerpt} {string.Join(" ", chunk.KeyFacts)} {GetChunkPromptText(chunk)}".ToLowerInvariant();

        if (Regex.IsMatch(heading + " " + text, @"\b(muc luc|table of contents|contents)\b"))
        {
            return ChunkClassifications.TableOfContents;
        }

        if (Regex.IsMatch(heading + " " + text, @"\b(loi noi dau|preface|foreword|introduction)\b") && chunk.ChunkNumber <= 4)
        {
            return ChunkClassifications.Preface;
        }

        if (Regex.IsMatch(heading + " " + text, @"\b(tai lieu tham khao|tham khao|references|bibliography)\b"))
        {
            return ChunkClassifications.Reference;
        }

        if (Regex.IsMatch(heading + " " + text, @"\b(phu luc|appendix)\b"))
        {
            return ChunkClassifications.Appendix;
        }

        if (Regex.IsMatch(heading + " " + text, @"\b(vi du|ví dụ|example|case study|ung dung|ứng dụng)\b"))
        {
            return ChunkClassifications.Example;
        }

        if (Regex.IsMatch(heading + " " + text, @"\b(bai tap|bài tập|cau hoi|câu hỏi|exercise|review question|quiz)\b"))
        {
            return ChunkClassifications.Exercise;
        }

        if (LooksLikeFrontMatter(chunk, text, documentType))
        {
            return ChunkClassifications.FrontMatter;
        }

        if (TextCleanupUtility.HasNoisyArtifacts(text) || Regex.IsMatch(text, @"\b(ocr|garbled|lorem ipsum)\b", RegexOptions.IgnoreCase))
        {
            return ChunkClassifications.Noise;
        }

        return ChunkClassifications.LessonContent;
    }

    private static bool LooksLikeFrontMatter(DocumentCoverageChunk chunk, string loweredText, string documentType)
    {
        if (chunk.ChunkNumber <= 2)
        {
            return true;
        }

        if (Regex.IsMatch(loweredText, @"\b(tac gia|tác giả|nha xuat ban|nhà xuất bản|publisher|copyright|isbn|all rights reserved)\b"))
        {
            return true;
        }

        if (documentType == DocumentTypes.Textbook && chunk.Zone == "dau" && !chunk.IsPrimarySection && (chunk.HeadingLevel ?? 10) > 2)
        {
            return true;
        }

        return false;
    }

    private static (int Score, List<string> Positives, List<string> Negatives) ScoreTeachability(DocumentCoverageChunk chunk, string classification, string documentType)
    {
        var score = 50;
        var positives = new List<string>();
        var negatives = new List<string>();
        var text = $"{chunk.Label} {chunk.Summary} {chunk.EvidenceExcerpt} {string.Join(" ", chunk.KeyFacts)} {GetChunkPromptText(chunk)}";
        var lowered = text.ToLowerInvariant();

        void AddPositive(string signal, int points)
        {
            positives.Add(signal);
            score += points;
        }

        void AddNegative(string signal, int points)
        {
            negatives.Add(signal);
            score -= points;
        }

        if (classification is ChunkClassifications.LessonContent or ChunkClassifications.Example)
        {
            AddPositive("classified as teachable content", 12);
        }

        if (chunk.IsPrimarySection || (chunk.HeadingLevel ?? 10) <= 2)
        {
            AddPositive("section heading", 10);
        }

        if (Regex.IsMatch(lowered, @"\b(la\s+|dinh nghia|định nghĩa|concept|khai niem|khái niệm)\b", RegexOptions.IgnoreCase))
        {
            AddPositive("definition or concept wording", 12);
        }

        if (Regex.IsMatch(lowered, @"\b(nguyen nhan|ket qua|vi vay|do do|because|therefore|cause|effect)\b", RegexOptions.IgnoreCase))
        {
            AddPositive("cause effect explanation", 8);
        }

        if (Regex.IsMatch(lowered, @"\b(so sanh|phan loai|classification|compared|khac nhau|giong nhau)\b", RegexOptions.IgnoreCase))
        {
            AddPositive("comparison or classification", 8);
        }

        if (Regex.IsMatch(lowered, @"\b(cong thuc|công thức|formula|principle|nguyen ly|định luật|law)\b", RegexOptions.IgnoreCase) || text.Any(char.IsDigit))
        {
            AddPositive("formula or principle", 8);
        }

        if (Regex.IsMatch(lowered, @"\b(vi du|ví dụ|ung dung|ứng dụng|example|application)\b", RegexOptions.IgnoreCase))
        {
            AddPositive("example or application", 8);
        }

        if (Regex.IsMatch(lowered, @"\b(tac gia|tác giả|publisher|nha xuat ban|nhà xuất bản|isbn|copyright)\b", RegexOptions.IgnoreCase))
        {
            AddNegative("author or publisher information", 28);
        }

        if (classification == ChunkClassifications.TableOfContents)
        {
            AddNegative("table of contents", 26);
        }

        if (classification == ChunkClassifications.Reference)
        {
            AddNegative("reference list", 24);
        }

        if (classification is ChunkClassifications.FrontMatter or ChunkClassifications.Noise)
        {
            AddNegative("front matter or noise", 24);
        }

        if (text.Length < 120)
        {
            AddNegative("too short", 14);
        }

        if (TextCleanupUtility.HasNoisyArtifacts(text))
        {
            AddNegative("ocr artifacts", 18);
        }

        if (LooksMostlyNames(text))
        {
            AddNegative("mostly names", 14);
        }

        if (documentType == DocumentTypes.Textbook && classification == ChunkClassifications.LessonContent)
        {
            AddPositive("textbook learning section", 6);
        }

        return (Math.Clamp(score, 0, 100), positives.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), negatives.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static bool LooksMostlyNames(string text)
    {
        var words = Regex.Matches(text, @"\b\p{L}{2,}\b")
            .Select(match => match.Value)
            .ToList();

        if (words.Count < 8)
        {
            return false;
        }

        var titleCaseWords = words.Count(word => char.IsUpper(word[0]));
        return titleCaseWords >= words.Count * 0.65;
    }

    private static int? TryGetChunkStartPage(DocumentCoverageChunk chunk)
    {
        var candidate = $"{chunk.Label}\n{chunk.Summary}\n{chunk.EvidenceExcerpt}\n{GetChunkPromptText(chunk)}";
        var match = PageRegex.Match(candidate);
        return match.Success && int.TryParse(match.Groups[1].Value, out var page) ? page : null;
    }

    private static int? TryGetChunkEndPage(DocumentCoverageChunk chunk)
    {
        var candidate = $"{GetChunkPromptText(chunk)}\n{chunk.EvidenceExcerpt}\n{chunk.Summary}\n{chunk.Label}";
        var matches = PageRegex.Matches(candidate);
        if (matches.Count == 0)
        {
            return null;
        }

        var last = matches[^1];
        return int.TryParse(last.Groups[1].Value, out var page) ? page : null;
    }

    private static string BuildSelectionReason(DocumentCoverageChunk chunk, string? sectionHeading)
    {
        if (chunk.TeachabilityScore < 35)
        {
            return $"Excluded as {chunk.Classification} due to low teachability score {chunk.TeachabilityScore}.";
        }

        var heading = !string.IsNullOrWhiteSpace(sectionHeading)
            ? sectionHeading
            : chunk.HeadingText ?? chunk.NormalizedHeading ?? chunk.Label;
        return $"Selected from {chunk.Classification} under '{heading}' with teachability score {chunk.TeachabilityScore}.";
    }

    private static DocumentProcessingMetadata BuildProcessingMetadata(string normalizedText, IReadOnlyCollection<DocumentCoverageChunk> coverageMap, string? detectedLanguage)
    {
        var language = !string.IsNullOrWhiteSpace(detectedLanguage) ? detectedLanguage : DetectLanguage(normalizedText);
        var documentType = DetectDocumentType(normalizedText, coverageMap.ToList());
        var title = DetectTitle(normalizedText);
        var mainContentStartPage = coverageMap
            .Where(chunk => chunk.Classification is ChunkClassifications.LessonContent or ChunkClassifications.Example)
            .OrderBy(chunk => chunk.ChunkNumber)
            .Select(chunk => chunk.StartPage)
            .FirstOrDefault(page => page.HasValue);

        return new DocumentProcessingMetadata
        {
            DocumentType = documentType,
            Language = language,
            Title = title,
            MainContentStartPage = mainContentStartPage,
            Structure = BuildStructureDescriptors(coverageMap),
            ExcludedContent = BuildExcludedDescriptors(coverageMap),
            TotalChunks = coverageMap.Count,
            AverageChunkTokens = coverageMap.Count > 0
                ? Math.Round(coverageMap.Average(chunk => chunk.TextTokenCount > 0 ? chunk.TextTokenCount : chunk.EstimatedTokenCount), 2)
                : 0d
        };
    }

    private static List<DocumentSectionDescriptor> BuildStructureDescriptors(IReadOnlyCollection<DocumentCoverageChunk> coverageMap)
    {
        return coverageMap
            .Where(chunk => ShouldIncludeChunkInCoverage(chunk))
            .GroupBy(chunk => !string.IsNullOrWhiteSpace(chunk.SectionKey) ? chunk.SectionKey : chunk.ChunkId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group.OrderBy(chunk => chunk.ChunkNumber).ToList();
                var first = ordered[0];
                return new DocumentSectionDescriptor
                {
                    SectionKey = first.SectionKey ?? first.ChunkId,
                    Heading = first.HeadingText ?? first.NormalizedHeading ?? first.Label,
                    Classification = first.Classification,
                    StartPage = ordered.Select(chunk => chunk.StartPage).FirstOrDefault(page => page.HasValue),
                    EndPage = ordered.Select(chunk => chunk.EndPage).LastOrDefault(page => page.HasValue),
                    ChunkIds = ordered.Select(chunk => chunk.ChunkId).ToList()
                };
            })
            .OrderBy(section => section.ChunkIds.FirstOrDefault())
            .ToList();
    }

    private static List<ExcludedContentDescriptor> BuildExcludedDescriptors(IReadOnlyCollection<DocumentCoverageChunk> coverageMap)
    {
        return coverageMap
            .Where(chunk => !ShouldIncludeChunkInCoverage(chunk))
            .OrderBy(chunk => chunk.ChunkNumber)
            .Select(chunk => new ExcludedContentDescriptor
            {
                ChunkId = chunk.ChunkId,
                Page = chunk.StartPage,
                Classification = chunk.Classification,
                Reason = !string.IsNullOrWhiteSpace(chunk.SelectionReason)
                    ? chunk.SelectionReason
                    : string.Join("; ", chunk.NegativeSignals)
            })
            .ToList();
    }

    private sealed class ChunkAnalysis
    {
        public List<string>? Topics { get; set; }
        public List<string>? KeyPoints { get; set; }
        public string? Summary { get; set; }
        public string? Language { get; set; }
    }

    private sealed record AnalysisContextSelection(
        string ContextText,
        string Path,
        int? KnowledgeMapTokens,
        string? Reason);

    private sealed class UnderstandingContractPayload
    {
        public PresentationExtractionContract? PresentationContract { get; set; }
    }

    private static void ReportAnalysisProgress(
        IProgress<DocumentProcessingProgressUpdate>? progress,
        string stage,
        string stageLabel,
        string message,
        int? current,
        int? total,
        string? unitLabel,
        int percent)
    {
        progress?.Report(new DocumentProcessingProgressUpdate
        {
            Percent = percent,
            Stage = stage,
            StageLabel = stageLabel,
            Message = message,
            Detail = message,
            Current = current,
            Total = total,
            UnitLabel = unitLabel,
            StageIndex = stage == "analyzing-chunks" ? 4 : 5,
            StageCount = 6
        });
    }

    private static int MapProgress(int startPercent, int endPercent, int current, int total)
    {
        if (total <= 0)
        {
            return endPercent;
        }

        var ratio = Math.Clamp(current / (double)total, 0d, 1d);
        return startPercent + (int)Math.Round((endPercent - startPercent) * ratio);
    }

    private static ChunkAnalysis ConvertProcessedToChunkAnalysis(ProcessedContent processed)
    {
        return new ChunkAnalysis
        {
            Topics = processed.MainTopics,
            KeyPoints = processed.KeyPoints,
            Summary = processed.Summary,
            Language = processed.Language
        };
    }
}
