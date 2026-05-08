using System.Text;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Interfaces;

namespace ELearnGamePlatform.Services.AI;

internal static class DocumentStructureChunker
{
    private static readonly Regex PageMarkerRegex = new(@"(?=\[Page\s+\d+\])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NumberedHeadingRegex = new(
        @"^(?:(?:\d+(?:\.\d+){0,4})|(?:[IVXLCM]+)|(?:[A-Z]))[\)\.]?\s+\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NamedSectionRegex = new(
        @"^(?:(?:chuong|chương|muc|mục|phan|phần|bai|bài|section|chapter|unit)\s+(?:\d+|[ivxlcm]+|[a-z])|(?:chuong|chương|phan|phần|muc|mục|bai|bài)\b)[\s:\-\.]*.+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<string> SplitIntoChunks(string content, int chunkSize, int overlap)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<string>();
        }

        var structureChunks = SplitByStructure(content, chunkSize, overlap);
        if (structureChunks.Count > 0)
        {
            return structureChunks;
        }

        var pageChunks = SplitIntoPageChunks(content, chunkSize);
        if (pageChunks.Count > 1)
        {
            return pageChunks;
        }

        return SplitByLength(content, chunkSize, overlap);
    }

    public static List<string> SplitIntoTokenChunks(
        string content,
        int targetTokens,
        int maxTokens,
        int overlapTokens,
        ITokenEstimator? tokenEstimator)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<string>();
        }

        targetTokens = Math.Max(200, targetTokens);
        maxTokens = Math.Max(targetTokens, maxTokens);
        overlapTokens = Math.Clamp(overlapTokens, 0, Math.Max(0, targetTokens / 2));

        var normalized = content.Trim();
        if (EstimateTokens(normalized, tokenEstimator) <= maxTokens)
        {
            return new List<string> { normalized };
        }

        var sections = SplitIntoHeadingSections(normalized);
        if (sections.Count <= 1)
        {
            sections = SplitIntoPageOrParagraphSections(normalized);
        }

        var chunks = new List<string>();
        var builder = new StringBuilder();

        foreach (var section in sections)
        {
            if (EstimateTokens(section, tokenEstimator) > maxTokens)
            {
                FlushBuilder(chunks, builder);
                chunks.AddRange(SplitOversizedSectionByTokens(section, targetTokens, maxTokens, overlapTokens, tokenEstimator));
                continue;
            }

            var candidate = builder.Length == 0
                ? section
                : $"{builder}{Environment.NewLine}{Environment.NewLine}{section}";
            if (builder.Length > 0 && EstimateTokens(candidate, tokenEstimator) > targetTokens)
            {
                FlushBuilder(chunks, builder);
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(section);
        }

        FlushBuilder(chunks, builder);
        return chunks.SelectMany(chunk => EnsureChunkWithinMaxTokens(chunk, maxTokens, overlapTokens, tokenEstimator)).ToList();
    }

    public static DocumentHeadingMetadata? AnalyzeHeading(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var lines = content.Split('\n');
        var headingLine = lines
            .Select(NormalizeHeadingCandidate)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line) && IsPotentialHeadingText(line));

        if (string.IsNullOrWhiteSpace(headingLine))
        {
            return null;
        }

        if (TryParseNamedSectionHeading(headingLine, out var named))
        {
            return named;
        }

        if (TryParseNumberedHeading(headingLine, out var numbered))
        {
            return numbered;
        }

        return new DocumentHeadingMetadata
        {
            Kind = "standalone",
            Level = 1,
            Marker = null,
            Title = headingLine,
            HeadingText = headingLine,
            NormalizedHeading = headingLine,
            Path = headingLine
        };
    }

    private static List<string> SplitIntoHeadingSections(string content)
    {
        var lines = content.Split('\n');
        var headingIndexes = new List<int>();

        for (var index = 0; index < lines.Length; index++)
        {
            if (IsHeadingLine(lines, index))
            {
                headingIndexes.Add(index);
            }
        }

        if (headingIndexes.Count < 2)
        {
            return new List<string>();
        }

        var sections = new List<string>();
        for (var index = 0; index < headingIndexes.Count; index++)
        {
            var start = headingIndexes[index];
            var end = index + 1 < headingIndexes.Count ? headingIndexes[index + 1] : lines.Length;
            var section = string.Join('\n', lines[start..end]).Trim();
            if (!string.IsNullOrWhiteSpace(section))
            {
                sections.Add(section);
            }
        }

        return sections;
    }

    private static List<string> SplitIntoPageOrParagraphSections(string content)
    {
        var pages = PageMarkerRegex.Split(content)
            .Select(page => page.Trim())
            .Where(page => !string.IsNullOrWhiteSpace(page))
            .ToList();

        if (pages.Count > 1)
        {
            return pages;
        }

        return content
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph))
            .ToList();
    }

    private static List<string> SplitOversizedSectionByTokens(
        string section,
        int targetTokens,
        int maxTokens,
        int overlapTokens,
        ITokenEstimator? tokenEstimator)
    {
        var lines = section.Split('\n').Select(line => line.TrimEnd()).ToList();
        var firstLine = lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;
        var hasHeading = !string.IsNullOrWhiteSpace(firstLine) && IsPotentialHeadingText(NormalizeHeadingCandidate(firstLine));
        var heading = hasHeading ? firstLine.Trim() : string.Empty;
        var body = hasHeading ? string.Join('\n', lines.Skip(1)).Trim() : section.Trim();
        var units = body
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(unit => EnsureChunkWithinMaxTokens(unit, Math.Max(200, maxTokens - EstimateTokens(heading, tokenEstimator)), overlapTokens, tokenEstimator))
            .Where(unit => !string.IsNullOrWhiteSpace(unit))
            .ToList();

        if (units.Count == 0)
        {
            return EnsureChunkWithinMaxTokens(section, maxTokens, overlapTokens, tokenEstimator).ToList();
        }

        var chunks = new List<string>();
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(heading))
        {
            builder.Append(heading);
        }

        foreach (var unit in units)
        {
            var candidate = builder.Length == 0 ? unit : $"{builder}{Environment.NewLine}{Environment.NewLine}{unit}";
            if (builder.Length > heading.Length && EstimateTokens(candidate, tokenEstimator) > targetTokens)
            {
                FlushBuilder(chunks, builder);
                var overlap = BuildTokenOverlap(chunks.LastOrDefault(), overlapTokens, tokenEstimator);
                if (!string.IsNullOrWhiteSpace(heading))
                {
                    builder.Append(heading);
                }

                if (!string.IsNullOrWhiteSpace(overlap))
                {
                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                        builder.AppendLine();
                    }

                    builder.Append(overlap);
                }
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(unit);
        }

        FlushBuilder(chunks, builder);
        return chunks.SelectMany(chunk => EnsureChunkWithinMaxTokens(chunk, maxTokens, overlapTokens, tokenEstimator)).ToList();
    }

    private static IEnumerable<string> EnsureChunkWithinMaxTokens(
        string text,
        int maxTokens,
        int overlapTokens,
        ITokenEstimator? tokenEstimator)
    {
        if (EstimateTokens(text, tokenEstimator) <= maxTokens)
        {
            yield return text.Trim();
            yield break;
        }

        var maxChars = Math.Max(700, (int)Math.Round(maxTokens * 3.5d));
        var overlapChars = Math.Max(0, (int)Math.Round(overlapTokens * 3.5d));
        foreach (var chunk in SplitByLength(text, maxChars, overlapChars))
        {
            yield return chunk;
        }
    }

    private static string BuildTokenOverlap(string? text, int overlapTokens, ITokenEstimator? tokenEstimator)
    {
        if (string.IsNullOrWhiteSpace(text) || overlapTokens <= 0)
        {
            return string.Empty;
        }

        var paragraphs = text
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Reverse()
            .ToList();
        var selected = new List<string>();
        var total = 0;

        foreach (var paragraph in paragraphs)
        {
            var tokens = EstimateTokens(paragraph, tokenEstimator);
            if (selected.Count > 0 && total + tokens > overlapTokens)
            {
                break;
            }

            selected.Add(paragraph);
            total += tokens;
            if (total >= overlapTokens)
            {
                break;
            }
        }

        selected.Reverse();
        return string.Join(Environment.NewLine + Environment.NewLine, selected);
    }

    private static int EstimateTokens(string? text, ITokenEstimator? tokenEstimator)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return tokenEstimator?.EstimateTokens(text) ?? (int)Math.Ceiling(text.Length / 3.5d);
    }

    private static List<string> SplitByStructure(string content, int chunkSize, int overlap)
    {
        var lines = content.Split('\n');
        var headingIndexes = new List<int>();

        for (var index = 0; index < lines.Length; index++)
        {
            if (IsHeadingLine(lines, index))
            {
                headingIndexes.Add(index);
            }
        }

        if (headingIndexes.Count < 2)
        {
            return new List<string>();
        }

        var sections = new List<string>();
        for (var index = 0; index < headingIndexes.Count; index++)
        {
            var start = headingIndexes[index];
            var end = index + 1 < headingIndexes.Count ? headingIndexes[index + 1] : lines.Length;
            var section = string.Join('\n', lines[start..end]).Trim();
            if (!string.IsNullOrWhiteSpace(section))
            {
                sections.Add(section);
            }
        }

        if (sections.Count < 2)
        {
            return new List<string>();
        }

        var chunks = new List<string>();
        var builder = new StringBuilder();

        foreach (var section in sections)
        {
            if (section.Length > chunkSize)
            {
                FlushBuilder(chunks, builder);

                foreach (var chunk in SplitOversizedSection(section, chunkSize, overlap))
                {
                    if (!string.IsNullOrWhiteSpace(chunk))
                    {
                        chunks.Add(chunk);
                    }
                }

                continue;
            }

            if (builder.Length > 0 && builder.Length + 2 + section.Length > chunkSize)
            {
                FlushBuilder(chunks, builder);
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(section);
        }

        FlushBuilder(chunks, builder);
        return chunks;
    }

    private static bool IsHeadingLine(IReadOnlyList<string> lines, int index)
    {
        var line = NormalizeHeadingCandidate(lines[index]);
        if (string.IsNullOrWhiteSpace(line) || line.Length < 4 || line.Length > 120)
        {
            return false;
        }

        if (line.StartsWith("[Page", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (NumberedHeadingRegex.IsMatch(line) || NamedSectionRegex.IsMatch(line))
        {
            return true;
        }

        if (!IsShortStandaloneHeading(lines, index, line))
        {
            return false;
        }

        return IsMostlyUppercase(line) || LooksLikeTitleCaseHeading(line);
    }

    private static bool IsPotentialHeadingText(string line)
    {
        if (line.StartsWith("[Page", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return NumberedHeadingRegex.IsMatch(line)
            || NamedSectionRegex.IsMatch(line)
            || IsMostlyUppercase(line)
            || LooksLikeTitleCaseHeading(line);
    }

    private static bool IsShortStandaloneHeading(IReadOnlyList<string> lines, int index, string line)
    {
        if (line.EndsWith(".", StringComparison.Ordinal) ||
            line.EndsWith(",", StringComparison.Ordinal) ||
            line.EndsWith(";", StringComparison.Ordinal))
        {
            return false;
        }

        if (line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 10)
        {
            return false;
        }

        var previous = index > 0 ? NormalizeHeadingCandidate(lines[index - 1]) : string.Empty;
        var next = index + 1 < lines.Count ? NormalizeHeadingCandidate(lines[index + 1]) : string.Empty;

        var previousBreak = string.IsNullOrWhiteSpace(previous) || previous.StartsWith("[Page", StringComparison.OrdinalIgnoreCase);
        var nextLooksBody = !string.IsNullOrWhiteSpace(next) && next.Length > 20;

        return previousBreak && nextLooksBody;
    }

    private static bool IsMostlyUppercase(string line)
    {
        var letters = line.Where(char.IsLetter).ToArray();
        if (letters.Length < 4)
        {
            return false;
        }

        var upperCount = letters.Count(char.IsUpper);
        return upperCount >= Math.Ceiling(letters.Length * 0.7d);
    }

    private static bool LooksLikeTitleCaseHeading(string line)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 10)
        {
            return false;
        }

        var capitalized = words.Count(word =>
        {
            var trimmed = word.Trim('(', ')', '[', ']', ':', '-', '"', '\'');
            return trimmed.Length > 0 && char.IsUpper(trimmed[0]);
        });

        return capitalized >= Math.Max(2, words.Length / 2);
    }

    private static bool TryParseNamedSectionHeading(string line, out DocumentHeadingMetadata metadata)
    {
        metadata = null!;

        var match = Regex.Match(
            line,
            @"^(?<kind>chuong|muc|phan|bai|section|chapter|unit)\s+(?<marker>\d+|[ivxlcm]+|[a-z])(?:[\s:\-\.]+(?<title>.+))?$",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return false;
        }

        var kind = match.Groups["kind"].Value.ToLowerInvariant();
        var marker = match.Groups["marker"].Value.Trim();
        var title = NormalizeHeadingCandidate(match.Groups["title"].Value);
        var level = kind switch
        {
            "chuong" or "chapter" or "unit" => 1,
            "phan" or "section" => 2,
            "muc" => 3,
            "bai" => 4,
            _ => 2
        };

        var normalizedHeading = string.IsNullOrWhiteSpace(title)
            ? $"{kind} {marker}"
            : $"{kind} {marker}: {title}";

        metadata = new DocumentHeadingMetadata
        {
            Kind = kind,
            Level = level,
            Marker = marker,
            Title = string.IsNullOrWhiteSpace(title) ? normalizedHeading : title,
            HeadingText = string.IsNullOrWhiteSpace(title) ? normalizedHeading : title,
            NormalizedHeading = normalizedHeading,
            Path = normalizedHeading
        };

        return true;
    }

    private static bool TryParseNumberedHeading(string line, out DocumentHeadingMetadata metadata)
    {
        metadata = null!;

        var match = Regex.Match(
            line,
            @"^(?<marker>(?:\d+(?:\.\d+){0,4})|(?:[IVXLCM]+)|(?:[A-Z]))[\)\.]?\s+(?<title>.+)$",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return false;
        }

        var marker = match.Groups["marker"].Value.Trim();
        var title = NormalizeHeadingCandidate(match.Groups["title"].Value);
        var level = marker.Contains('.', StringComparison.Ordinal)
            ? marker.Split('.', StringSplitOptions.RemoveEmptyEntries).Length
            : 1;
        var normalizedHeading = string.IsNullOrWhiteSpace(title)
            ? marker
            : $"{marker} {title}";

        metadata = new DocumentHeadingMetadata
        {
            Kind = "numbered",
            Level = level,
            Marker = marker,
            Title = string.IsNullOrWhiteSpace(title) ? normalizedHeading : title,
            HeadingText = string.IsNullOrWhiteSpace(title) ? normalizedHeading : title,
            NormalizedHeading = normalizedHeading,
            Path = normalizedHeading
        };

        return true;
    }

    private static List<string> SplitOversizedSection(string section, int chunkSize, int overlap)
    {
        var lines = section.Split('\n')
            .Select(line => line.TrimEnd())
            .ToList();

        var heading = lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;
        var body = string.Join('\n', lines.Skip(1)).Trim();

        if (string.IsNullOrWhiteSpace(body))
        {
            return SplitByLength(section, chunkSize, overlap);
        }

        var paragraphs = body
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (paragraphs.Count <= 1)
        {
            return SplitByLength(section, chunkSize, overlap);
        }

        var chunks = new List<string>();
        var builder = new StringBuilder(heading);
        var prefixedHeading = string.IsNullOrWhiteSpace(heading) ? string.Empty : heading;

        foreach (var paragraph in paragraphs)
        {
            var candidateLength = builder.Length + (builder.Length > 0 ? 2 : 0) + paragraph.Length;
            if (builder.Length > prefixedHeading.Length && candidateLength > chunkSize)
            {
                FlushBuilder(chunks, builder);
                if (!string.IsNullOrWhiteSpace(prefixedHeading))
                {
                    builder.Append(prefixedHeading);
                }
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(paragraph);
        }

        FlushBuilder(chunks, builder);

        if (chunks.Any(chunk => chunk.Length > chunkSize))
        {
            return chunks.SelectMany(chunk => SplitByLength(chunk, chunkSize, overlap)).ToList();
        }

        return chunks;
    }

    private static List<string> SplitIntoPageChunks(string content, int chunkSize)
    {
        var pages = PageMarkerRegex.Split(content)
            .Select(page => page.Trim())
            .Where(page => !string.IsNullOrWhiteSpace(page))
            .ToList();

        if (pages.Count <= 1)
        {
            return new List<string>();
        }

        var chunks = new List<string>();
        var builder = new StringBuilder();

        foreach (var page in pages)
        {
            if (builder.Length > 0 && builder.Length + page.Length > chunkSize)
            {
                FlushBuilder(chunks, builder);
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(page);
        }

        FlushBuilder(chunks, builder);
        return chunks;
    }

    private static List<string> SplitByLength(string content, int chunkSize, int overlap)
    {
        var chunks = new List<string>();
        var start = 0;

        while (start < content.Length)
        {
            var length = Math.Min(chunkSize, content.Length - start);
            var end = start + length;

            if (end < content.Length)
            {
                var searchWindow = Math.Min(length, 1000);
                var paragraphBreak = content.LastIndexOf("\n\n", end, searchWindow, StringComparison.Ordinal);
                if (paragraphBreak > start + (chunkSize / 2))
                {
                    end = paragraphBreak;
                    length = end - start;
                }
                else
                {
                    var lineBreak = content.LastIndexOf('\n', end, searchWindow);
                    if (lineBreak > start + (chunkSize / 2))
                    {
                        end = lineBreak;
                        length = end - start;
                    }
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

    private static void FlushBuilder(List<string> chunks, StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        var chunk = builder.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(chunk))
        {
            chunks.Add(chunk);
        }

        builder.Clear();
    }

    private static string NormalizeHeadingCandidate(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value, @"\s+", " ").Trim();
}

internal sealed class DocumentHeadingMetadata
{
    public string Kind { get; init; } = "standalone";
    public int Level { get; init; } = 1;
    public string? Marker { get; init; }
    public string Title { get; init; } = string.Empty;
    public string HeadingText { get; init; } = string.Empty;
    public string NormalizedHeading { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}
