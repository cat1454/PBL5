using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ELearnGamePlatform.API.Services.QuestionStudio;
public sealed class QuestionSourceUnitExtractor : IQuestionSourceUnitExtractor
{
    private const int ChunkSize = 3600;
    private const int ChunkOverlap = 450;

    public Task<List<QuestionSourceUnit>> ExtractAsync(Document document, int generationRunId, CancellationToken cancellationToken = default)
    {
        var units = new List<QuestionSourceUnit>();
        var topics = document.GetMainTopics();
        var keyPoints = document.GetKeyPoints();

        foreach (var keyPoint in keyPoints.Take(20))
        {
            AddUnit(units, document.Id, generationRunId, "SummaryPoint", keyPoint, ResolveTopic(topics, keyPoint), 0, keyPoint.Length);
        }

        var text = NormalizeWhitespace(document.ExtractedText ?? string.Empty);
        foreach (var (chunk, start, end) in SplitChunks(text, ChunkSize, ChunkOverlap).Take(40))
        {
            var sentences = Regex.Split(chunk, @"(?<=[.!?])\s+|\n+")
                .Select(NormalizeWhitespace)
                .Where(x => x.Length >= 45)
                .OrderByDescending(ScoreSentence)
                .Take(4)
                .ToList();

            foreach (var sentence in sentences)
            {
                AddUnit(units, document.Id, generationRunId, ClassifyUnit(sentence), sentence, ResolveTopic(topics, sentence), start, end);
            }
        }

        if (units.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            AddUnit(units, document.Id, generationRunId, "SummaryPoint", Truncate(text, 700), ResolveTopic(topics, text), 0, Math.Min(text.Length, 700));
        }

        return Task.FromResult(units
            .GroupBy(x => x.SourceHash)
            .Select(x => x.First())
            .Take(80)
            .ToList());
    }

    private static void AddUnit(List<QuestionSourceUnit> units, int documentId, int generationRunId, string unitType, string content, string topicTag, int start, int end)
    {
        var normalized = NormalizeWhitespace(content);
        if (normalized.Length < 24)
        {
            return;
        }

        units.Add(new QuestionSourceUnit
        {
            DocumentId = documentId,
            GenerationRunId = generationRunId,
            UnitType = unitType,
            Content = Truncate(normalized, 1100),
            TopicTag = Truncate(NormalizeTag(topicTag), 180),
            SourceHash = QuestionStudioText.Hash(normalized),
            StartOffset = Math.Max(0, start),
            EndOffset = Math.Max(start, end),
            Confidence = EstimateConfidence(normalized),
            MetadataJson = JsonSerializer.Serialize(new { length = normalized.Length })
        });
    }

    private static string ClassifyUnit(string value)
    {
        var lower = value.ToLowerInvariant();
        if (Regex.IsMatch(value, @"[\uFFFD]{1,}|[^\w\s.,;:?!()\-/\[\]\p{L}\p{N}]{4,}"))
        {
            return "OCRRisk";
        }

        if (lower.Contains(" la ") || lower.Contains(" is "))
        {
            return "Definition";
        }

        if (lower.Contains(" buoc ") || lower.Contains(" quy trinh ") || lower.Contains(" process "))
        {
            return "Process";
        }

        if (lower.Contains(" so sanh ") || lower.Contains(" khac ") || lower.Contains(" compare "))
        {
            return "Comparison";
        }

        return "Concept";
    }

    private static double EstimateConfidence(string value)
    {
        var noisy = Regex.Matches(value, @"[\uFFFD]|[^\w\s.,;:?!()\-/\[\]\p{L}\p{N}]").Count;
        var ratio = value.Length == 0 ? 0 : noisy / (double)value.Length;
        return Math.Clamp(1.0 - (ratio * 5), 0.25, 1.0);
    }

    private static string ResolveTopic(IReadOnlyList<string> topics, string content)
        => topics.FirstOrDefault(topic => content.Contains(topic, StringComparison.OrdinalIgnoreCase))
            ?? topics.FirstOrDefault()
            ?? "general";

    private static int ScoreSentence(string sentence)
    {
        var score = 0;
        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (words is >= 8 and <= 40) score += 8;
        if (sentence.Contains(':')) score += 4;
        if (sentence.Any(char.IsDigit)) score += 3;
        if (sentence.Contains(" la ", StringComparison.OrdinalIgnoreCase)) score += 3;
        return score;
    }

    private static IEnumerable<(string Chunk, int Start, int End)> SplitChunks(string content, int chunkSize, int overlap)
    {
        var start = 0;
        while (start < content.Length)
        {
            var end = Math.Min(content.Length, start + chunkSize);
            var chunk = content[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                yield return (chunk, start, end);
            }

            if (end >= content.Length)
            {
                break;
            }

            start = Math.Max(start + 1, end - overlap);
        }
    }

    private static string NormalizeTag(string value)
        => Regex.Replace(NormalizeWhitespace(value).ToLowerInvariant(), @"[^\p{L}\p{N}]+", "-").Trim('-');

    private static string NormalizeWhitespace(string value)
        => Regex.Replace(value.Replace('\u00A0', ' '), @"\s+", " ").Trim();

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
}

