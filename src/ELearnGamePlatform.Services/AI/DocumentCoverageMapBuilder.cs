using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Services.AI;

internal static class DocumentCoverageMapBuilder
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "va", "la", "cua", "cho", "voi", "trong", "tren", "duoc", "mot", "nhung", "cac", "khi", "neu",
        "thi", "tai", "theo", "ve", "den", "tu", "co", "khong", "nay", "do", "day", "sau", "truoc",
        "hoac", "nhu", "da", "dang", "can", "phan", "page", "from", "with", "that", "this", "have",
        "into", "about", "their", "there", "would", "should", "could", "while", "where", "which"
    };

    public static List<DocumentCoverageChunk> Build(string content, int chunkSize = 2200, int overlap = 320)
    {
        var normalized = NormalizeContent(content);
        var rawChunks = SplitIntoChunks(normalized, chunkSize, overlap);
        var chunks = new List<DocumentCoverageChunk>(rawChunks.Count);
        var headingStack = new List<DocumentHeadingMetadata>();

        for (var index = 0; index < rawChunks.Count; index++)
        {
            var chunkNumber = index + 1;
            var chunkText = rawChunks[index];
            var keyFacts = ExtractHighSignalSentences(chunkText, 4);
            var heading = DocumentStructureChunker.AnalyzeHeading(chunkText);
            UpdateHeadingStack(headingStack, heading);
            var headingPath = BuildHeadingPath(headingStack, heading);
            var parentHeadingPath = BuildParentHeadingPath(headingStack, heading);
            var headingText = heading?.HeadingText ?? heading?.Title;
            var sectionKey = BuildSectionKey(headingPath, heading, chunkNumber);
            chunks.Add(new DocumentCoverageChunk
            {
                ChunkNumber = chunkNumber,
                ChunkId = $"C{chunkNumber:00}",
                Zone = ResolveCoverageZone(chunkNumber, rawChunks.Count),
                Label = BuildChunkLabel(chunkText, chunkNumber, rawChunks.Count),
                HeadingKind = heading?.Kind,
                HeadingLevel = heading?.Level,
                HeadingMarker = heading?.Marker,
                HeadingText = headingText,
                NormalizedHeading = heading?.NormalizedHeading,
                HeadingPath = headingPath,
                ParentHeadingPath = parentHeadingPath,
                SectionKey = sectionKey,
                IsPrimarySection = IsPrimarySection(heading),
                Summary = BuildChunkSummary(chunkText, keyFacts),
                EvidenceExcerpt = BuildEvidenceExcerpt(chunkText, keyFacts),
                KeyFacts = keyFacts
            });
        }

        return chunks;
    }

    public static HashSet<string> BuildSearchTokens(DocumentCoverageChunk chunk)
        => BuildSearchTokens(
            chunk.Label,
            chunk.HeadingKind,
            chunk.HeadingMarker,
            chunk.HeadingText,
            chunk.NormalizedHeading,
            chunk.HeadingPath,
            chunk.ParentHeadingPath,
            chunk.SectionKey,
            chunk.Summary,
            chunk.EvidenceExcerpt,
            string.Join(" ", chunk.KeyFacts));

    public static HashSet<string> BuildSearchTokens(params string?[] values)
    {
        var normalized = NormalizeToken(string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value))));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return normalized
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3 && !StopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static string NormalizeContent(string content)
        => string.IsNullOrWhiteSpace(content)
            ? string.Empty
            : content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static List<string> SplitIntoChunks(string content, int chunkSize, int overlap)
        => DocumentStructureChunker.SplitIntoChunks(content, chunkSize, overlap);

    private static void UpdateHeadingStack(List<DocumentHeadingMetadata> stack, DocumentHeadingMetadata? heading)
    {
        if (heading == null)
        {
            return;
        }

        while (stack.Count >= heading.Level)
        {
            stack.RemoveAt(stack.Count - 1);
        }

        stack.Add(heading);
    }

    private static string? BuildHeadingPath(List<DocumentHeadingMetadata> stack, DocumentHeadingMetadata? heading)
    {
        if (heading == null)
        {
            return stack.LastOrDefault()?.Path;
        }

        return string.Join(" > ", stack.Select(item => item.NormalizedHeading));
    }

    private static string? BuildParentHeadingPath(List<DocumentHeadingMetadata> stack, DocumentHeadingMetadata? heading)
    {
        if (heading == null)
        {
            return stack.Count > 1 ? string.Join(" > ", stack.Take(stack.Count - 1).Select(item => item.NormalizedHeading)) : null;
        }

        return stack.Count > 1 ? string.Join(" > ", stack.Take(stack.Count - 1).Select(item => item.NormalizedHeading)) : null;
    }

    private static string BuildSectionKey(string? headingPath, DocumentHeadingMetadata? heading, int chunkNumber)
    {
        if (!string.IsNullOrWhiteSpace(headingPath))
        {
            return NormalizeToken(headingPath);
        }

        if (!string.IsNullOrWhiteSpace(heading?.NormalizedHeading))
        {
            return NormalizeToken(heading.NormalizedHeading);
        }

        return $"section-{chunkNumber:00}";
    }

    private static bool IsPrimarySection(DocumentHeadingMetadata? heading)
    {
        if (heading == null)
        {
            return false;
        }

        if (heading.Kind is "chuong" or "chapter" or "unit" or "phan" or "section")
        {
            return true;
        }

        return heading.Level <= 2;
    }

    private static string ResolveCoverageZone(int chunkNumber, int totalChunks)
    {
        if (totalChunks <= 2)
        {
            return chunkNumber == 1 ? "dau" : "cuoi";
        }

        var ratio = chunkNumber / (double)Math.Max(1, totalChunks);
        return ratio <= 0.34d ? "dau" : ratio <= 0.67d ? "giua" : "cuoi";
    }

    private static string BuildChunkLabel(string chunkText, int chunkNumber, int totalChunks)
    {
        var candidate = chunkText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("[Page", StringComparison.OrdinalIgnoreCase) && line.Length >= 12);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = ExtractHighSignalSentences(chunkText, 1).FirstOrDefault() ?? $"Phan {chunkNumber}/{totalChunks}";
        }

        return Truncate(candidate, 90);
    }

    private static string BuildChunkSummary(string chunkText, List<string> keyFacts)
    {
        if (keyFacts.Any())
        {
            return string.Join(" ", keyFacts.Take(2));
        }

        return Truncate(BuildEvidenceExcerpt(chunkText, keyFacts), 220);
    }

    private static string BuildEvidenceExcerpt(string chunkText, List<string> keyFacts)
    {
        if (keyFacts.Any())
        {
            return Truncate(string.Join(" ", keyFacts.Take(2)), 420);
        }

        var cleaned = Regex.Replace(chunkText.Replace('\n', ' '), @"\s+", " ").Trim();
        return Truncate(cleaned, 420);
    }

    private static List<string> ExtractHighSignalSentences(string text, int maxCount)
    {
        var candidates = Regex.Split(text, @"(?<=[\.\?\!])\s+|\n+")
            .Select(sentence => Regex.Replace(sentence, @"\s+", " ").Trim())
            .Where(sentence =>
                !string.IsNullOrWhiteSpace(sentence) &&
                !sentence.StartsWith("[Page", StringComparison.OrdinalIgnoreCase) &&
                sentence.Length >= 18)
            .Select(sentence => new
            {
                Sentence = sentence,
                Score = ScoreSentence(sentence)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Sentence.Length)
            .ToList();

        var selected = new List<string>();
        foreach (var candidate in candidates)
        {
            if (selected.Count >= maxCount)
            {
                break;
            }

            var normalized = NormalizeSentence(candidate.Sentence) ?? candidate.Sentence;
            if (selected.Any(existing => string.Equals(NormalizeSentence(existing), normalized, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            selected.Add(Truncate(candidate.Sentence, 220));
        }

        return selected;
    }

    private static int ScoreSentence(string sentence)
    {
        var score = 0;
        var wordCount = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        if (wordCount is >= 6 and <= 28)
        {
            score += 6;
        }

        if (sentence.Any(char.IsDigit))
        {
            score += 4;
        }

        if (sentence.Contains(':', StringComparison.Ordinal))
        {
            score += 3;
        }

        if (sentence.Contains("la ", StringComparison.OrdinalIgnoreCase) ||
            sentence.Contains("gom", StringComparison.OrdinalIgnoreCase) ||
            sentence.Contains("bao gom", StringComparison.OrdinalIgnoreCase) ||
            sentence.Contains("nguyen nhan", StringComparison.OrdinalIgnoreCase) ||
            sentence.Contains("ket qua", StringComparison.OrdinalIgnoreCase) ||
            sentence.Contains("buoc", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        return score;
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is ':' or '-' or '_' or ' ' or '/' or '|')
            {
                builder.Append('-');
            }
        }

        var collapsed = builder.ToString();
        while (collapsed.Contains("--", StringComparison.Ordinal))
        {
            collapsed = collapsed.Replace("--", "-", StringComparison.Ordinal);
        }

        return collapsed.Trim('-');
    }

    private static string? NormalizeSentence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : Truncate(normalized, 260);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "...";
}
