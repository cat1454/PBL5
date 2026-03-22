using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace ELearnGamePlatform.Services.AI;

public class ContentAnalyzerService : IContentAnalyzer
{
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<ContentAnalyzerService> _logger;
    private const int ChunkSize = 3500;
    private const int ChunkOverlap = 250;
    private const int MaxParallelChunkAnalyses = 3;
    private const int ChunkCompactionBatchSize = 4;
    private const int MaxChunkAnalysesBeforeCompaction = 6;

    public ContentAnalyzerService(IOllamaService ollamaService, ILogger<ContentAnalyzerService> logger)
    {
        _ollamaService = ollamaService;
        _logger = logger;
    }

    public async Task<ProcessedContent> AnalyzeContentAsync(string text, IProgress<DocumentProcessingProgressUpdate>? progress = null)
    {
        try
        {
            var normalizedText = NormalizeText(text);
            var coverageMap = DocumentCoverageMapBuilder.Build(normalizedText);
            var chunks = SplitIntoChunks(normalizedText, ChunkSize, ChunkOverlap);

            _logger.LogInformation(
                "Analyzing document content using {ChunkCount} chunks with max parallelism {MaxParallelChunkAnalyses}",
                chunks.Count,
                Math.Min(MaxParallelChunkAnalyses, Math.Max(1, chunks.Count)));

            var chunkAnalyses = await AnalyzeChunksInParallelAsync(chunks, progress);

            if (!chunkAnalyses.Any())
            {
                _logger.LogWarning("No chunk analyses were produced, using fallback");
                return CreateFallbackProcessedContent(text, coverageMap);
            }

            var preparedAnalyses = CompactChunkAnalysesLocally(chunkAnalyses, progress);

            ReportAnalysisProgress(progress, "consolidating-analysis", "Tong hop ket qua", "Dang hop nhat ket qua phan tich toan tai lieu", preparedAnalyses.Count, preparedAnalyses.Count, "cum phan tich", 92);
            var result = await ConsolidateChunkAnalysesAsync(preparedAnalyses);

            if (result == null)
            {
                _logger.LogWarning("Failed to consolidate chunk analyses with AI, using local merge fallback");
                return MergeChunkAnalysesLocally(preparedAnalyses, normalizedText, coverageMap);
            }

            ReportAnalysisProgress(progress, "consolidating-analysis", "Tong hop ket qua", "Dang hoan thien tom tat, topics va key points", preparedAnalyses.Count, preparedAnalyses.Count, "cum phan tich", 97);
            return EnsureProcessedContentQuality(result, preparedAnalyses, normalizedText, coverageMap);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing content");
            return CreateFallbackProcessedContent(text, DocumentCoverageMapBuilder.Build(NormalizeText(text)));
        }
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
            var systemPrompt = "You are an educational content analyzer. Extract precise study information in Vietnamese from the given chunk. Prioritize factual correctness, concrete topic naming, and usefulness for later quiz generation.";

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

        var systemPrompt = "You are an educational content analyst. Merge chunk analyses into one full-document analysis in Vietnamese with clear topic coverage.";
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

        processed.CoverageMap = coverageMap;

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

        return new ProcessedContent
        {
            MainTopics = topics.Any() ? topics : new List<string> { "Tong quan noi dung" },
            KeyPoints = keyPoints.Any() ? keyPoints : normalizedText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(8).ToList(),
            Summary = !string.IsNullOrWhiteSpace(summary) ? summary : string.Join(" ", normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(120)) + "...",
            Language = language,
            CoverageMap = coverageMap
        };
    }

    private static string NormalizeText(string text)
    {
        return TextCleanupUtility.NormalizeForAi(text, preserveLineBreaks: true);
    }

    private static List<string> SplitIntoChunks(string content, int chunkSize, int overlap)
    {
        var chunks = new List<string>();
        var start = 0;

        while (start < content.Length)
        {
            var length = Math.Min(chunkSize, content.Length - start);
            var end = start + length;

            if (end < content.Length)
            {
                var searchWindow = Math.Min(length, 800);
                var paragraphBreak = content.LastIndexOf("\n\n", end, searchWindow);
                if (paragraphBreak > start + (chunkSize / 2))
                {
                    end = paragraphBreak;
                    length = end - start;
                }
            }

            var chunk = content.Substring(start, length).Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }

            if (end >= content.Length)
            {
                break;
            }

            start = Math.Max(end - overlap, start + 1);
        }

        return chunks;
    }

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
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return new ProcessedContent
        {
            MainTopics = new List<string> { "General Content" },
            KeyPoints = lines.Take(5).ToList(),
            Summary = string.Join(" ", words.Take(50)) + "...",
            Language = "Unknown",
            CoverageMap = coverageMap
        };
    }

    private sealed class ChunkAnalysis
    {
        public List<string>? Topics { get; set; }
        public List<string>? KeyPoints { get; set; }
        public string? Summary { get; set; }
        public string? Language { get; set; }
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
