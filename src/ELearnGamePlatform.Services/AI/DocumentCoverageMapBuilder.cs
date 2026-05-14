using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;

namespace ELearnGamePlatform.Services.AI;

public static class DocumentCoverageMapBuilder
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "và", "là", "của", "cho", "với", "trong", "trên", "được", "một", "những", "các", "khi", "nếu",
        "thì", "tại", "theo", "về", "đến", "từ", "có", "không", "này", "đó", "đây", "sau", "trước",
        "hoặc", "như", "đã", "đang", "cần", "phần", "trang", "từ", "với", "khiến", "này", "có",
        "vào", "page", "from", "with", "that", "this", "have",
        "into", "about", "their", "there", "would", "should", "could", "while", "where", "which"
    };
    private static readonly HashSet<string> NormalizedStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "va", "la", "cua", "cho", "voi", "trong", "tren", "duoc", "mot", "nhung", "cac", "khi", "neu",
        "thi", "tai", "theo", "ve", "den", "tu", "co", "khong", "nay", "do", "day", "sau", "truoc",
        "hoac", "nhu", "da", "dang", "can", "phan", "trang", "vao", "page", "from", "with", "that",
        "this", "have", "into", "about", "their", "there", "would", "should", "could", "while",
        "where", "which", "and", "the", "for", "are", "was", "were", "toi", "cac"
    };

    public static List<DocumentCoverageChunk> Build(string content, int chunkSize = 2200, int overlap = 320)
        => Build(content, chunkSize, overlap, null);

    public static List<DocumentCoverageChunk> Build(string content, LocalLlmSettings settings, ITokenEstimator? tokenEstimator = null)
    {
        var normalized = NormalizeContent(content);
        var rawChunks = DocumentStructureChunker.SplitIntoStructuredTokenChunks(
            normalized,
            settings.TargetChunkTokens,
            settings.MaxChunkTokens,
            settings.ChunkOverlapTokens,
            tokenEstimator);

        return BuildFromRawChunks(rawChunks, tokenEstimator);
    }

    public static List<DocumentCoverageChunk> Build(string content, int chunkSize, int overlap, ITokenEstimator? tokenEstimator)
    {
        var normalized = NormalizeContent(content);
        var rawChunks = SplitIntoChunks(normalized, chunkSize, overlap)
            .Select(chunk => new StructuredDocumentChunk(chunk, "paragraph-boundary"))
            .ToList();
        return BuildFromRawChunks(rawChunks, tokenEstimator);
    }

    private static List<DocumentCoverageChunk> BuildFromRawChunks(List<StructuredDocumentChunk> rawChunks, ITokenEstimator? tokenEstimator)
    {
        var chunks = new List<DocumentCoverageChunk>(rawChunks.Count);
        var headingStack = new List<DocumentHeadingMetadata>();

        for (var index = 0; index < rawChunks.Count; index++)
        {
            var chunkNumber = index + 1;
            var rawChunk = rawChunks[index];
            var chunkText = rawChunk.Text;
            var normalizedChunkText = NormalizeContent(chunkText);
            var textTokenCount = EstimateTokens(normalizedChunkText, tokenEstimator);
            var keyFacts = ExtractHighSignalSentences(chunkText, 4);
            var heading = DocumentStructureChunker.AnalyzeHeading(chunkText);
            UpdateHeadingStack(headingStack, heading);
            var headingPath = BuildHeadingPath(headingStack, heading);
            var parentHeadingPath = BuildParentHeadingPath(headingStack, heading);
            var headingText = heading?.HeadingText ?? heading?.Title;
            var sectionKey = BuildSectionKey(headingPath, heading, chunkNumber);
            var coverageZone = ResolveCoverageZone(chunkNumber, rawChunks.Count);
            var keywords = ExtractKeywords(chunkText, headingText, headingPath);
            var conceptAnchors = ExtractConceptAnchors(chunkText, keywords, headingText, headingPath);
            var quality = ScoreLocalQuality(chunkText, headingPath, keywords, conceptAnchors, keyFacts);
            chunks.Add(new DocumentCoverageChunk
            {
                ChunkNumber = chunkNumber,
                ChunkId = $"C{chunkNumber:00}",
                Zone = coverageZone,
                CoverageZone = coverageZone,
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
                Keywords = keywords,
                ConceptAnchors = conceptAnchors,
                ChunkingReason = rawChunk.ChunkingReason,
                ChunkQualityScore = quality.Score,
                TeachabilityScore = quality.Score,
                PositiveSignals = quality.PositiveSignals,
                NegativeSignals = quality.NegativeSignals,
                IsEligibleForQuestionGeneration = quality.Score >= 35,
                Warnings = quality.NegativeSignals.ToList(),
                KeyFacts = keyFacts,
                Text = chunkText,
                NormalizedText = normalizedChunkText,
                TextTokenCount = textTokenCount,
                EstimatedTokenCount = textTokenCount
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
            string.Join(" ", chunk.Keywords),
            string.Join(" ", chunk.ConceptAnchors),
            chunk.NormalizedText,
            chunk.Text,
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

    private static List<string> ExtractKeywords(string text, string? headingText, string? headingPath)
    {
        var scores = new Dictionary<string, (string Display, int Score)>(StringComparer.OrdinalIgnoreCase);

        AddPhrases(headingPath, 8);
        AddPhrases(headingText, 10);
        AddPhrases(text, 1);

        foreach (Match match in Regex.Matches(text, @"\b(?:\d{4}|\d+(?:[.,]\d+)?%?)\b"))
        {
            AddKeyword(match.Value, match.Value, 5);
        }

        foreach (Match match in Regex.Matches(text, @"\b[A-Z][A-Za-z0-9]*(?:\s+[A-Z][A-Za-z0-9]*){0,3}\b"))
        {
            AddKeyword(match.Value, match.Value, 4);
        }

        return scores.Values
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Display.Length)
            .Select(item => item.Display)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        void AddPhrases(string? value, int weight)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var words = Regex.Matches(value, @"[\p{L}\p{N}][\p{L}\p{N}\-_/]*")
                .Select(match => match.Value.Trim())
                .Where(word => word.Length >= 3)
                .ToList();

            for (var index = 0; index < words.Count; index++)
            {
                AddKeyword(words[index], words[index], weight);
                if (index + 1 < words.Count)
                {
                    AddKeyword($"{words[index]} {words[index + 1]}", $"{words[index]} {words[index + 1]}", weight + 2);
                }

                if (index + 2 < words.Count)
                {
                    AddKeyword($"{words[index]} {words[index + 1]} {words[index + 2]}", $"{words[index]} {words[index + 1]} {words[index + 2]}", weight + 3);
                }
            }
        }

        void AddKeyword(string display, string normalizedSource, int score)
        {
            var normalized = NormalizeToken(normalizedSource);
            if (normalized.Length < 3 || IsStopWord(normalized) || LooksLikeGenericKeyword(normalized))
            {
                return;
            }

            if (scores.TryGetValue(normalized, out var existing))
            {
                scores[normalized] = (existing.Display, existing.Score + score);
            }
            else
            {
                scores[normalized] = (NormalizeDisplayPhrase(display), score);
            }
        }
    }

    private static List<string> ExtractConceptAnchors(string text, List<string> keywords, string? headingText, string? headingPath)
    {
        var anchors = new List<string>();
        foreach (var source in new[] { headingPath, headingText }.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            anchors.AddRange(ExtractKeywords(source!, source, source).Take(3));
        }

        anchors.AddRange(keywords.Where(keyword => keyword.Contains(' ', StringComparison.Ordinal)).Take(6));
        anchors.AddRange(ExtractHighSignalSentences(text, 2)
            .Select(sentence => Truncate(sentence, 120)));

        return anchors
            .Where(anchor => !string.IsNullOrWhiteSpace(anchor))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static (int Score, List<string> PositiveSignals, List<string> NegativeSignals) ScoreLocalQuality(
        string text,
        string? headingPath,
        List<string> keywords,
        List<string> conceptAnchors,
        List<string> keyFacts)
    {
        var positives = new List<string>();
        var negatives = new List<string>();
        var score = 35;
        var normalized = NormalizeToken(text);

        if (!string.IsNullOrWhiteSpace(headingPath))
        {
            score += 12;
            positives.Add("heading");
        }

        score += Math.Min(18, keywords.Count * 3);
        score += Math.Min(14, conceptAnchors.Count * 2);
        score += Math.Min(18, keyFacts.Count * 4);

        if (Regex.IsMatch(text, @"\d{4}|\d+(?:[.,]\d+)?%?"))
        {
            score += 8;
            positives.Add("numbers-or-dates");
        }

        if (Regex.IsMatch(text, @"\b[A-Z][A-Za-z0-9]*(?:\s+[A-Z][A-Za-z0-9]*)+\b"))
        {
            score += 6;
            positives.Add("named-entities");
        }

        var wordCount = Regex.Matches(text, @"[\p{L}\p{N}]+").Count;
        if (wordCount is >= 45 and <= 600)
        {
            score += 8;
            positives.Add("complete-paragraphs");
        }

        if (wordCount < 30)
        {
            score -= 18;
            negatives.Add("too-short");
        }

        if (LooksLikeFrontMatterOrToc(normalized))
        {
            score -= 35;
            negatives.Add("front-matter-or-toc");
        }

        if (LooksLikeReferenceSection(normalized))
        {
            score -= 22;
            negatives.Add("reference-like");
        }

        var noiseRatio = EstimateNoiseRatio(text);
        if (noiseRatio > 0.22d)
        {
            score -= 25;
            negatives.Add("ocr-noise");
        }
        else if (noiseRatio > 0.12d)
        {
            score -= 10;
            negatives.Add("possible-ocr-noise");
        }

        if (keywords.Count <= 1 && keyFacts.Count <= 1)
        {
            score -= 12;
            negatives.Add("generic");
        }

        return (Math.Clamp(score, 0, 100), positives, negatives);
    }

    public static string NormalizeContent(string content)
        => string.IsNullOrWhiteSpace(content)
            ? string.Empty
            : content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static List<string> SplitIntoChunks(string content, int chunkSize, int overlap)
        => DocumentStructureChunker.SplitIntoChunks(content, chunkSize, overlap);

    private static int EstimateTokens(string? text, ITokenEstimator? tokenEstimator)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return tokenEstimator?.EstimateTokens(text) ?? (int)Math.Ceiling(text.Length / 3.5d);
    }

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

    private static bool IsStopWord(string normalized)
        => StopWords.Contains(normalized)
            || NormalizedStopWords.Contains(normalized)
            || StopWords.Contains(normalized.Replace('-', ' '))
            || NormalizedStopWords.Contains(normalized.Replace('-', ' '))
            || normalized.Split('-', StringSplitOptions.RemoveEmptyEntries).All(token => StopWords.Contains(token) || NormalizedStopWords.Contains(token));

    private static bool LooksLikeGenericKeyword(string normalized)
    {
        var generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "noi-dung", "tai-lieu", "chuong", "phan", "muc", "bai", "trang", "page",
            "content", "document", "section", "chapter", "unit", "lesson"
        };
        return generic.Contains(normalized);
    }

    private static string NormalizeDisplayPhrase(string value)
        => Regex.Replace(value, @"\s+", " ").Trim(' ', '.', ',', ';', ':', '-', '_', '/', '|');

    private static bool LooksLikeFrontMatterOrToc(string normalized)
        => normalized.Contains("muc-luc", StringComparison.Ordinal)
            || normalized.Contains("table-of-contents", StringComparison.Ordinal)
            || normalized.Contains("loi-noi-dau", StringComparison.Ordinal)
            || normalized.Contains("preface", StringComparison.Ordinal)
            || normalized.Contains("copyright", StringComparison.Ordinal)
            || Regex.IsMatch(normalized, @"(^|-)isbn(-|$)")
            || Regex.IsMatch(normalized, @"\b\d+-{2,}\d+\b");

    private static bool LooksLikeReferenceSection(string normalized)
        => normalized.Contains("tai-lieu-tham-khao", StringComparison.Ordinal)
            || normalized.Contains("bibliography", StringComparison.Ordinal)
            || normalized.Contains("references", StringComparison.Ordinal)
            || normalized.Contains("doi", StringComparison.Ordinal)
            || normalized.Contains("retrieved-from", StringComparison.Ordinal);

    private static double EstimateNoiseRatio(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 1d;
        }

        var nonWhitespace = text.Count(ch => !char.IsWhiteSpace(ch));
        if (nonWhitespace == 0)
        {
            return 1d;
        }

        var noisy = text.Count(ch =>
            !char.IsLetterOrDigit(ch) &&
            !char.IsWhiteSpace(ch) &&
            ch is not '.' and not ',' and not ':' and not ';' and not '-' and not '_' and not '/' and not '%' and not '(' and not ')' and not '[' and not ']');
        var replacement = text.Count(ch => ch == '�');
        return (noisy + replacement * 2) / (double)nonWhitespace;
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
