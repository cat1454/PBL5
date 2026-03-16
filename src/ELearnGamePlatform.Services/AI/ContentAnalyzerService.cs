using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ELearnGamePlatform.Services.AI;

public class ContentAnalyzerService : IContentAnalyzer
{
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<ContentAnalyzerService> _logger;
    private const int ChunkSize = 3500;
    private const int ChunkOverlap = 250;
    private const int MaxChunksToAnalyze = 3;

    public ContentAnalyzerService(IOllamaService ollamaService, ILogger<ContentAnalyzerService> logger)
    {
        _ollamaService = ollamaService;
        _logger = logger;
    }

    public async Task<ProcessedContent> AnalyzeContentAsync(string text)
    {
        try
        {
            var normalizedText = NormalizeText(text);
            var chunks = LimitChunksForAnalysis(SplitIntoChunks(normalizedText, ChunkSize, ChunkOverlap));

            _logger.LogInformation("Analyzing document content using {ChunkCount} chunks", chunks.Count);

            var chunkAnalyses = new List<ChunkAnalysis>();
            for (int index = 0; index < chunks.Count; index++)
            {
                var chunkAnalysis = await AnalyzeChunkAsync(chunks[index], index + 1, chunks.Count);
                if (chunkAnalysis != null)
                {
                    chunkAnalyses.Add(chunkAnalysis);
                }
            }

            if (!chunkAnalyses.Any())
            {
                _logger.LogWarning("No chunk analyses were produced, using fallback");
                return CreateFallbackProcessedContent(text);
            }

            var result = await ConsolidateChunkAnalysesAsync(chunkAnalyses);
            
            if (result == null)
            {
                _logger.LogWarning("Failed to consolidate chunk analyses with AI, using local merge fallback");
                return MergeChunkAnalysesLocally(chunkAnalyses, normalizedText);
            }

            return EnsureProcessedContentQuality(result, chunkAnalyses, normalizedText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing content");
            return CreateFallbackProcessedContent(text);
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

    private async Task<ChunkAnalysis?> AnalyzeChunkAsync(string chunk, int chunkNumber, int totalChunks)
    {
        try
        {
            var systemPrompt = "You are an educational content analyzer. Extract precise study information in Vietnamese from the given chunk. Prioritize factual correctness and topic specificity.";

            var prompt = $@"Analyze chunk {chunkNumber}/{totalChunks} from a larger educational document.

Goals:
1. Extract 3-6 specific topics from this chunk (use concrete names, avoid generic labels)
2. Extract 5-10 concrete key points with factual details (definitions, formulas, rules, dates, steps, causes/effects when present)
3. Write a concise Vietnamese summary (2-4 sentences)
4. Identify the language of the chunk
5. Focus on what can later be used to generate quiz questions
6. Each topic should be a concise noun phrase (2-7 words), non-overlapping, and directly grounded in the chunk
7. Do NOT output vague labels like ""Tổng quan"", ""Nội dung chính"", ""Kiến thức cơ bản"" unless the chunk explicitly uses those exact terms

Chunk content:
{chunk}

Respond in valid JSON only:
{{
    ""topics"": [""chủ đề cụ thể 1"", ""chủ đề cụ thể 2""],
    ""keyPoints"": [""ý chính có dữ kiện 1"", ""ý chính có dữ kiện 2""],
    ""summary"": ""tóm tắt tiếng Việt ngắn gọn"",
  ""language"": ""Vietnamese or English or mixed""
}}";

            var result = await _ollamaService.GenerateStructuredResponseAsync<ChunkAnalysis>(prompt, systemPrompt);
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
        string normalizedText)
    {
        var localMerged = MergeChunkAnalysesLocally(chunkAnalyses, normalizedText);

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

        return processed;
    }

    private ProcessedContent MergeChunkAnalysesLocally(List<ChunkAnalysis> chunkAnalyses, string normalizedText)
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
            Language = language
        };
    }

    private static string NormalizeText(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Replace("\r\n", "\n").Trim();
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

    private List<string> LimitChunksForAnalysis(List<string> chunks)
    {
        if (chunks.Count <= MaxChunksToAnalyze)
        {
            return chunks;
        }

        var selectedChunks = new List<string>
        {
            chunks[0],
            chunks[chunks.Count / 2],
            chunks[^1]
        };

        _logger.LogInformation(
            "Document produced {OriginalChunkCount} chunks. Limiting analysis to {SelectedChunkCount} representative chunks for faster response.",
            chunks.Count,
            selectedChunks.Count);

        return selectedChunks;
    }

    private ProcessedContent CreateFallbackProcessedContent(string text)
    {
        // Simple fallback when AI processing fails
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        return new ProcessedContent
        {
            MainTopics = new List<string> { "General Content" },
            KeyPoints = lines.Take(5).ToList(),
            Summary = string.Join(" ", words.Take(50)) + "...",
            Language = "Unknown"
        };
    }

    private class ChunkAnalysis
    {
        public List<string>? Topics { get; set; }
        public List<string>? KeyPoints { get; set; }
        public string? Summary { get; set; }
        public string? Language { get; set; }
    }
}
