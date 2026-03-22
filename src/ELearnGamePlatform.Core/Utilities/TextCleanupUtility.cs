using System.Text;
using System.Text.RegularExpressions;

namespace ELearnGamePlatform.Core.Utilities;

public static class TextCleanupUtility
{
    public static string NormalizeForAi(string? text, bool preserveLineBreaks = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = NormalizeNewlines(text);
        normalized = normalized
            .Replace('\u00A0', ' ')
            .Replace("\u200B", string.Empty, StringComparison.Ordinal)
            .Replace("\u200C", string.Empty, StringComparison.Ordinal)
            .Replace("\u200D", string.Empty, StringComparison.Ordinal)
            .Replace("\uFEFF", string.Empty, StringComparison.Ordinal)
            .Replace("\u00AD", string.Empty, StringComparison.Ordinal);

        normalized = Regex.Replace(normalized, @"(?<=\p{L})-\s*\n\s*(?=\p{L})", string.Empty);
        normalized = Regex.Replace(normalized, @"(?<=\p{Ll})\s*\n\s*(?=[\p{Ll}\d\(\[])"," ");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");

        var lines = normalized
            .Split('\n')
            .Select(NormalizeInlineText)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !LooksLikeStandalonePageNoise(line))
            .ToList();

        if (!preserveLineBreaks)
        {
            return NormalizeInlineText(string.Join(" ", lines));
        }

        var paragraphs = MergeBrokenLines(lines);
        return string.Join(Environment.NewLine, paragraphs).Trim();
    }

    public static string NormalizeForDisplay(string? text)
    {
        return NormalizeInlineText(NormalizeForAi(text, preserveLineBreaks: false));
    }

    public static string CleanPageText(string? text)
    {
        return NormalizeForAi(text, preserveLineBreaks: true);
    }

    public static IReadOnlyDictionary<int, string> RemoveRepeatedPageArtifacts(IReadOnlyDictionary<int, string> pageTexts)
    {
        var cleaned = pageTexts
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .ToDictionary(entry => entry.Key, entry => CleanPageText(entry.Value));

        var pageLines = cleaned.ToDictionary(
            entry => entry.Key,
            entry => entry.Value
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList());

        if (pageLines.Count < 3)
        {
            return cleaned;
        }

        var repeatedFirstLines = pageLines.Values
            .Select(lines => lines.FirstOrDefault())
            .Where(IsRemovableRepeatedLine)
            .GroupBy(line => line!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 3)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var repeatedLastLines = pageLines.Values
            .Select(lines => lines.LastOrDefault())
            .Where(IsRemovableRepeatedLine)
            .GroupBy(line => line!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 3)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<int, string>(pageLines.Count);
        foreach (var page in pageLines.OrderBy(entry => entry.Key))
        {
            var lines = page.Value.ToList();
            if (lines.Count > 0 && repeatedFirstLines.Contains(lines[0]))
            {
                lines.RemoveAt(0);
            }

            if (lines.Count > 0 && repeatedLastLines.Contains(lines[^1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }

            var merged = string.Join(Environment.NewLine, lines).Trim();
            if (!string.IsNullOrWhiteSpace(merged))
            {
                result[page.Key] = merged;
            }
        }

        return result;
    }

    public static bool HasNoisyArtifacts(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return Regex.IsMatch(text, @"(?<=[\p{L}])\d(?=[\p{L}]|\b)")
            || Regex.IsMatch(text, @"(?<=\d)[\p{L}](?=[\p{L}]|\b)")
            || Regex.IsMatch(text, @"\b(cau hoi du phong|grounded|preferredchunk|chunk id|lua chon tham chieu|dang cho ai)\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"[^\p{L}\p{N}\s,.;:?!()\[\]""'/%+\-_:]{2,}");
    }

    private static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string NormalizeInlineText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(text, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"(?<=[\p{L}])(?=\d)", " ");
        normalized = Regex.Replace(normalized, @"(?<=\d)(?=[\p{L}])", " ");
        normalized = Regex.Replace(normalized, @"\s+([,.;:?!])", "$1");
        normalized = Regex.Replace(normalized, @"([,.;:?!])(?=[\p{L}\p{N}])", "$1 ");
        normalized = Regex.Replace(normalized, @"\(\s+", "(");
        normalized = Regex.Replace(normalized, @"\s+\)", ")");
        normalized = Regex.Replace(normalized, @"\[\s+", "[");
        normalized = Regex.Replace(normalized, @"\s+\]", "]");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized;
    }

    private static List<string> MergeBrokenLines(List<string> lines)
    {
        if (lines.Count == 0)
        {
            return new List<string>();
        }

        var paragraphs = new List<string>();
        var current = new StringBuilder(lines[0]);

        for (var index = 1; index < lines.Count; index++)
        {
            var previousLine = current.ToString();
            var nextLine = lines[index];

            if (ShouldMergeLines(previousLine, nextLine))
            {
                if (previousLine.EndsWith("-", StringComparison.Ordinal))
                {
                    current.Length -= 1;
                }
                else
                {
                    current.Append(' ');
                }

                current.Append(nextLine);
                continue;
            }

            paragraphs.Add(NormalizeInlineText(current.ToString()));
            current.Clear();
            current.Append(nextLine);
        }

        if (current.Length > 0)
        {
            paragraphs.Add(NormalizeInlineText(current.ToString()));
        }

        return paragraphs;
    }

    private static bool ShouldMergeLines(string previousLine, string nextLine)
    {
        if (string.IsNullOrWhiteSpace(previousLine) || string.IsNullOrWhiteSpace(nextLine))
        {
            return false;
        }

        if (previousLine.EndsWith("-", StringComparison.Ordinal))
        {
            return true;
        }

        if (Regex.IsMatch(previousLine, @"[.!?:;)\]""']$"))
        {
            return false;
        }

        return Regex.IsMatch(nextLine, @"^[\p{Ll}\d\(\[]");
    }

    private static bool LooksLikeStandalonePageNoise(string line)
    {
        return Regex.IsMatch(line, @"^(page|trang)\s*\d+(\s*/\s*\d+)?$", RegexOptions.IgnoreCase)
            || Regex.IsMatch(line, @"^\d{1,4}\s*/\s*\d{1,4}$")
            || Regex.IsMatch(line, @"^\d{1,4}$");
    }

    private static bool IsRemovableRepeatedLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.Length < 5 || trimmed.Length > 120)
        {
            return false;
        }

        return !LooksLikeStandalonePageNoise(trimmed);
    }
}
