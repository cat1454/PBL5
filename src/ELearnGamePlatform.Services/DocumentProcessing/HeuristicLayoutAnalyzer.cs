using System.Text;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public class HeuristicLayoutAnalyzer : ILayoutAnalyzer
{
    private static readonly Regex PageMarkerRegex = new(@"^\[Page\s+(?<page>\d+)\]\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MultiSpaceColumnRegex = new(@"\S\s{2,}\S\s{2,}\S", RegexOptions.Compiled);
    private static readonly Regex HeaderFooterRegex = new(@"^(?:page|trang)\s+\d{1,4}(?:\s*/\s*\d{1,4})?$|^\d{1,4}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WordRegex = new(@"\b[\p{L}\p{N}]+\b", RegexOptions.Compiled);
    private static readonly Regex NumericValueRegex = new(@"(?<!\w)[-+]?\d+(?:[.,]\d+)?\s*(?:%|k|m|b|tr|ty|tỷ|triệu|million|billion)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ChartKeywordRegex = new(@"\b(?:chart|graph|axis|trend|compare|comparison|metric|score|accuracy|recall|precision|revenue|growth|rate|kpi|bar|line|pie|donut|bieu\s*do|doanh\s*thu|tang\s*truong|ti\s*le|ty\s*le)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TimelineKeywordRegex = new(@"\b(?:timeline|roadmap|process|pipeline|workflow|step|phase|stage|giai\s*doan|quy\s*trinh|buoc|moc|nam|year)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FormulaKeywordRegex = new(@"\b(?:formula|equation|calculate|calculation|where|cong\s*thuc|phuong\s*trinh|tinh)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MathSymbolRegex = new(@"[=+\-*/^√∑∫≈≠≤≥<>]|\\(?:frac|sum|int|sqrt|alpha|beta|gamma|Delta|theta)", RegexOptions.Compiled);
    private static readonly Regex VariableDigitMixRegex = new(@"\b[A-Za-z]\w*\s*(?:=|≈|<=|>=|<|>)\s*[-+]?\d|\d\s*(?:[A-Za-z]\w*|\^)|[A-Za-z]\w*\s*\^\s*\d", RegexOptions.Compiled);

    public IReadOnlyList<PageUnderstandingResult> Analyze(
        string filePath,
        string? legacyExtractedText,
        DocumentInputQualityReport? pageQualityReport = null)
    {
        var pages = SplitPages(legacyExtractedText);
        AddMissingReportedPages(pages, pageQualityReport);

        return pages
            .OrderBy(page => page.Key)
            .Select(page => BuildPageResult(filePath, page.Key, page.Value, pageQualityReport))
            .ToList();
    }

    private static PageUnderstandingResult BuildPageResult(
        string filePath,
        int pageNumber,
        string pageText,
        DocumentInputQualityReport? pageQualityReport)
    {
        var report = pageQualityReport?.Pages.FirstOrDefault(page => page.PageNumber == pageNumber);
        var regions = new List<DocumentRegion>();
        var lines = pageText
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        AddHeaderFooterRegions(pageNumber, lines, regions);
        AddTitleRegion(pageNumber, lines, regions);
        AddTableRegions(pageNumber, lines, regions);
        AddChartAndNumericRegions(pageNumber, lines, regions);
        AddFormulaRegions(pageNumber, lines, regions);
        AddDiagramRegions(pageNumber, lines, regions);
        AddFigureCandidate(filePath, pageNumber, pageText, report, regions);
        AddTextRegion(pageNumber, pageText, regions);

        return new PageUnderstandingResult
        {
            PageNumber = pageNumber,
            Text = pageText,
            Confidence = report?.Confidence ?? 0d,
            Regions = DistinctRegions(regions)
        };
    }

    private static Dictionary<int, string> SplitPages(string? legacyExtractedText)
    {
        var text = legacyExtractedText ?? string.Empty;
        var pages = new Dictionary<int, string>();
        var currentPage = 1;
        var hasPageMarkers = false;
        var builder = new StringBuilder();

        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var marker = PageMarkerRegex.Match(rawLine.Trim());
            if (marker.Success && int.TryParse(marker.Groups["page"].Value, out var markedPage))
            {
                if (hasPageMarkers || builder.Length > 0)
                {
                    pages[currentPage] = builder.ToString().Trim();
                    builder.Clear();
                }

                currentPage = markedPage;
                hasPageMarkers = true;
                continue;
            }

            builder.AppendLine(rawLine);
        }

        if (hasPageMarkers || builder.Length > 0 || !string.IsNullOrEmpty(text))
        {
            pages[currentPage] = builder.ToString().Trim();
        }

        if (pages.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            pages[1] = text.Trim();
        }

        return pages;
    }

    private static void AddMissingReportedPages(
        IDictionary<int, string> pages,
        DocumentInputQualityReport? pageQualityReport)
    {
        if (pageQualityReport == null)
        {
            return;
        }

        foreach (var report in pageQualityReport.Pages)
        {
            pages.TryAdd(report.PageNumber, string.Empty);
        }
    }

    private static void AddHeaderFooterRegions(
        int pageNumber,
        IReadOnlyList<string> lines,
        ICollection<DocumentRegion> regions)
    {
        foreach (var line in lines.Take(2).Concat(lines.TakeLast(2)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (HeaderFooterRegex.IsMatch(line.Trim()))
            {
                regions.Add(CreateRegion(pageNumber, DocumentRegionTypes.HeaderFooterCandidate, line));
            }
        }
    }

    private static void AddTitleRegion(
        int pageNumber,
        IReadOnlyList<string> lines,
        ICollection<DocumentRegion> regions)
    {
        var title = lines
            .Take(5)
            .FirstOrDefault(line => LooksLikeTitle(line) && !LooksLikeTableLine(line) && !LooksLikeDiagramLine(line));
        if (!string.IsNullOrWhiteSpace(title))
        {
            regions.Add(CreateRegion(pageNumber, DocumentRegionTypes.Title, title));
        }
    }

    private static void AddTableRegions(
        int pageNumber,
        IReadOnlyList<string> lines,
        ICollection<DocumentRegion> regions)
    {
        var tableLines = new List<string>();
        foreach (var line in lines)
        {
            if (LooksLikeTableLine(line) || (tableLines.Count > 0 && LooksLikeTableContinuationLine(line)))
            {
                tableLines.Add(line);
                continue;
            }

            FlushTableLines(pageNumber, tableLines, regions);
        }

        FlushTableLines(pageNumber, tableLines, regions);
    }

    private static void FlushTableLines(
        int pageNumber,
        List<string> tableLines,
        ICollection<DocumentRegion> regions)
    {
        if (tableLines.Count >= 2 || tableLines.Any(line => line.Count(ch => ch == '|') >= 2))
        {
            regions.Add(CreateTableRegion(pageNumber, tableLines));
        }

        tableLines.Clear();
    }

    private static void AddFormulaRegions(
        int pageNumber,
        IReadOnlyList<string> lines,
        ICollection<DocumentRegion> regions)
    {
        var formulaLines = new List<string>();
        foreach (var line in lines)
        {
            if (LooksLikeFormulaLine(line) && !LooksLikeTableLine(line))
            {
                formulaLines.Add(line);
                continue;
            }

            FlushFormulaLines(pageNumber, formulaLines, regions);
        }

        FlushFormulaLines(pageNumber, formulaLines, regions);
    }

    private static void FlushFormulaLines(
        int pageNumber,
        List<string> formulaLines,
        ICollection<DocumentRegion> regions)
    {
        if (formulaLines.Count > 0)
        {
            regions.Add(CreateRegion(
                pageNumber,
                DocumentRegionTypes.FormulaCandidate,
                string.Join(Environment.NewLine, formulaLines),
                rawText: string.Join(Environment.NewLine, formulaLines),
                layoutConfidence: 0.45d,
                needsReview: true,
                reviewTags: new[] { "FormulaHeavy", "NeedsReview" }));
        }

        formulaLines.Clear();
    }

    private static void AddChartAndNumericRegions(
        int pageNumber,
        IReadOnlyList<string> lines,
        ICollection<DocumentRegion> regions)
    {
        var numericLines = lines
            .Where(line => NumericValueRegex.Matches(line).Count >= 2)
            .Take(12)
            .ToList();
        if (numericLines.Count == 0)
        {
            return;
        }

        var chartSignals = numericLines.Count(line => ChartKeywordRegex.IsMatch(line))
            + lines.Take(6).Count(line => ChartKeywordRegex.IsMatch(line));
        var chartText = string.Join(Environment.NewLine, numericLines);
        var hasExplicitScale = lines.Any(HasScaleOrAxisCue);

        if (numericLines.Count >= 2 && (chartSignals > 0 || numericLines.Count >= 3))
        {
            regions.Add(CreateRegion(
                pageNumber,
                DocumentRegionTypes.ChartCandidate,
                chartText,
                rawText: chartText,
                layoutConfidence: hasExplicitScale ? 0.76d : 0.58d,
                needsReview: !hasExplicitScale,
                reviewTags: hasExplicitScale
                    ? new[] { "ChartCandidate", "NumericSeries" }
                    : new[] { "ChartCandidate", "NumericSeries", "ScaleMissing", "NeedsReview" }));
        }

        regions.Add(CreateRegion(
            pageNumber,
            DocumentRegionTypes.NumericEvidence,
            chartText,
            rawText: chartText,
            layoutConfidence: 0.64d,
            needsReview: numericLines.Any(LooksLikeFormulaLine),
            reviewTags: new[] { "NumericEvidence" }));
    }

    private static void AddDiagramRegions(
        int pageNumber,
        IReadOnlyList<string> lines,
        ICollection<DocumentRegion> regions)
    {
        var diagramLines = lines.Where(LooksLikeDiagramLine).ToList();
        if (diagramLines.Count > 0)
        {
            regions.Add(CreateRegion(pageNumber, DocumentRegionTypes.DiagramCandidate, string.Join(Environment.NewLine, diagramLines)));
            if (diagramLines.Any(line => TimelineKeywordRegex.IsMatch(line) || CountArrowTokens(line) >= 2))
            {
                regions.Add(CreateRegion(
                    pageNumber,
                    DocumentRegionTypes.ProcessCandidate,
                    string.Join(Environment.NewLine, diagramLines.Take(6)),
                    rawText: string.Join(Environment.NewLine, diagramLines),
                    layoutConfidence: 0.72d,
                    reviewTags: new[] { "ProcessOrTimeline" }));
            }
        }
    }

    private static void AddFigureCandidate(
        string filePath,
        int pageNumber,
        string pageText,
        DocumentPageProcessingReport? report,
        ICollection<DocumentRegion> regions)
    {
        var wordCount = CountWords(pageText);
        var isImageFile = IsImageFile(filePath);
        var renderedPageWithLittleText = report != null
            && report.Method is DocumentPageProcessingMethods.Ocr or DocumentPageProcessingMethods.Empty or DocumentPageProcessingMethods.Failed
            && (report.WordCount <= 12 || report.CharCount < 80);

        if ((isImageFile && wordCount <= 12) || renderedPageWithLittleText)
        {
            var description = string.IsNullOrWhiteSpace(pageText)
                ? "Page has little or no OCR text and should be considered image-heavy."
                : pageText.Trim();
            regions.Add(CreateRegion(pageNumber, DocumentRegionTypes.FigureCandidate, description));
        }
    }

    private static void AddTextRegion(
        int pageNumber,
        string pageText,
        ICollection<DocumentRegion> regions)
    {
        if (!string.IsNullOrWhiteSpace(pageText)
            && regions.All(region => region.RegionType != DocumentRegionTypes.Text))
        {
            regions.Add(CreateRegion(pageNumber, DocumentRegionTypes.Text, pageText.Trim()));
        }
    }

    private static bool LooksLikeTitle(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length is < 4 or > 90)
        {
            return false;
        }

        var words = CountWords(trimmed);
        if (words is < 2 or > 14)
        {
            return false;
        }

        var letters = trimmed.Where(char.IsLetter).ToList();
        if (letters.Count < 4)
        {
            return false;
        }

        var uppercaseRatio = letters.Count(char.IsUpper) / (double)letters.Count;
        return uppercaseRatio >= 0.65d;
    }

    private static bool LooksLikeTableLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 5)
        {
            return false;
        }

        var pipeCount = trimmed.Count(ch => ch == '|');
        return pipeCount >= 2
            || trimmed.Contains('\t')
            || MultiSpaceColumnRegex.IsMatch(line);
    }

    private static bool LooksLikeTableContinuationLine(string line)
        => line.Trim().Count(ch => ch == '|') >= 1;

    private static bool LooksLikeFormulaLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 3 || trimmed.Length > 180)
        {
            return false;
        }

        var mathSymbols = MathSymbolRegex.Matches(trimmed).Count;
        if (mathSymbols < 2 && !FormulaKeywordRegex.IsMatch(trimmed))
        {
            return false;
        }

        var wordCount = CountWords(trimmed);
        var hasEquation = trimmed.Contains('=')
            || trimmed.Contains('≈')
            || trimmed.Contains('≤')
            || trimmed.Contains('≥');
        var symbolRatio = trimmed.Count(ch => !char.IsLetterOrDigit(ch) && !char.IsWhiteSpace(ch)) / (double)Math.Max(1, trimmed.Length);

        return VariableDigitMixRegex.IsMatch(trimmed)
            || (hasEquation && mathSymbols >= 2 && wordCount <= 16)
            || (FormulaKeywordRegex.IsMatch(trimmed) && hasEquation)
            || symbolRatio >= 0.18d && mathSymbols >= 3;
    }

    private static DocumentRegion CreateTableRegion(int pageNumber, IReadOnlyList<string> tableLines)
    {
        var rawText = string.Join(Environment.NewLine, tableLines);
        var parsed = TryBuildMarkdownTable(tableLines);
        if (parsed.IsHighConfidence)
        {
            return CreateRegion(
                pageNumber,
                DocumentRegionTypes.TableLikeText,
                parsed.Markdown,
                rawText: rawText,
                layoutConfidence: parsed.Confidence,
                needsReview: false,
                reviewTags: parsed.Tags);
        }

        return CreateRegion(
            pageNumber,
            DocumentRegionTypes.TableLowConfidence,
            rawText,
            rawText: rawText,
            layoutConfidence: parsed.Confidence,
            needsReview: true,
            reviewTags: parsed.Tags.Concat(new[] { "TableLowConfidence", "NeedsReview" }));
    }

    private static TableParseResult TryBuildMarkdownTable(IReadOnlyList<string> tableLines)
    {
        var tags = new List<string>();
        var lines = tableLines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count is < 2 or > 20)
        {
            tags.Add("row-count-out-of-range");
            return new TableParseResult(false, string.Empty, 0.25d, tags);
        }

        if (lines.Any(LooksLikeFormulaLine))
        {
            tags.Add("formula-like-table-text");
            return new TableParseResult(false, string.Empty, 0.35d, tags);
        }

        var rows = lines.Select(SplitTableLine).ToList();
        var columnCounts = rows.Select(row => row.Count).ToList();
        var targetColumns = columnCounts
            .GroupBy(count => count)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .First()
            .Key;
        var stableRows = columnCounts.Count(count => count == targetColumns);
        var stability = stableRows / (double)rows.Count;

        if (targetColumns is < 2 or > 8)
        {
            tags.Add("column-count-out-of-range");
        }

        if (stability < 0.85d)
        {
            tags.Add("ragged-column-count");
        }

        if (rows.Any(row => row.Count != targetColumns || row.Any(cell => string.IsNullOrWhiteSpace(cell))))
        {
            tags.Add("empty-or-misaligned-cell");
        }

        if (lines.Any(line => line.Contains("  |", StringComparison.Ordinal) || line.Contains("|  ", StringComparison.Ordinal)))
        {
            tags.Add("possible-merged-cell");
        }

        var confidence = 0.45d
            + Math.Min(0.35d, stability * 0.35d)
            + (rows.All(row => row.Count == targetColumns && row.All(cell => !string.IsNullOrWhiteSpace(cell))) ? 0.15d : 0d)
            + (lines.Count(line => line.Contains('|', StringComparison.Ordinal) || line.Contains('\t')) >= lines.Count * 0.75d ? 0.05d : 0d);
        confidence = Math.Clamp(confidence, 0d, 0.98d);

        if (tags.Count > 0 || confidence < 0.82d)
        {
            return new TableParseResult(false, string.Empty, confidence, tags);
        }

        var markdownRows = rows.Select(row => row.Take(targetColumns).Select(EscapeMarkdownCell).ToList()).ToList();
        var builder = new StringBuilder();
        builder.AppendLine($"| {string.Join(" | ", markdownRows[0])} |");
        builder.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", targetColumns))} |");
        foreach (var row in markdownRows.Skip(1))
        {
            builder.AppendLine($"| {string.Join(" | ", row)} |");
        }

        tags.Add("SimpleMarkdownTable");
        return new TableParseResult(true, builder.ToString().TrimEnd(), confidence, tags);
    }

    private static List<string> SplitTableLine(string line)
    {
        var trimmed = line.Trim().Trim('|').Trim();
        if (trimmed.Contains('|', StringComparison.Ordinal))
        {
            return trimmed.Split('|', StringSplitOptions.TrimEntries).ToList();
        }

        if (trimmed.Contains('\t'))
        {
            return trimmed.Split('\t', StringSplitOptions.TrimEntries).ToList();
        }

        return Regex.Split(trimmed, @"\s{2,}")
            .Select(cell => cell.Trim())
            .Where(cell => !string.IsNullOrWhiteSpace(cell))
            .ToList();
    }

    private static string EscapeMarkdownCell(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal).Trim();

    private static bool LooksLikeDiagramLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Contains("->", StringComparison.Ordinal)
            || trimmed.Contains("=>", StringComparison.Ordinal)
            || trimmed.Contains("<-", StringComparison.Ordinal)
            || trimmed.Contains("<=", StringComparison.Ordinal)
            || trimmed.Contains('\u2192')
            || trimmed.Contains('\u21d2')
            || trimmed.Contains('\u2193')
            || trimmed.Contains('\u2191');
    }

    private static bool HasScaleOrAxisCue(string line)
    {
        var lowered = line.ToLowerInvariant();
        return lowered.Contains("axis", StringComparison.Ordinal)
            || lowered.Contains("scale", StringComparison.Ordinal)
            || lowered.Contains("range", StringComparison.Ordinal)
            || lowered.Contains("0%", StringComparison.Ordinal)
            || lowered.Contains("100%", StringComparison.Ordinal)
            || lowered.Contains("truc", StringComparison.Ordinal)
            || lowered.Contains("thang", StringComparison.Ordinal);
    }

    private static int CountArrowTokens(string line)
        => Regex.Matches(line, @"(?:->|=>|<-|<=|\u2192|\u21d2|\u2193|\u2191)").Count;

    private static int CountWords(string text)
        => WordRegex.Matches(text).Count;

    private static bool IsImageFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static DocumentRegion CreateRegion(
        int pageNumber,
        string regionType,
        string text,
        string? rawText = null,
        double? layoutConfidence = null,
        bool needsReview = false,
        IEnumerable<string>? reviewTags = null)
        => new()
        {
            PageNumber = pageNumber,
            RegionType = regionType,
            Text = text.Trim(),
            RawText = string.IsNullOrWhiteSpace(rawText) ? null : rawText.Trim(),
            LayoutConfidence = layoutConfidence,
            NeedsReview = needsReview,
            ReviewTags = reviewTags?
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

    private static List<DocumentRegion> DistinctRegions(IEnumerable<DocumentRegion> regions)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinct = new List<DocumentRegion>();

        foreach (var region in regions)
        {
            var key = $"{region.PageNumber}|{region.RegionType}|{region.Text}";
            if (seen.Add(key))
            {
                distinct.Add(region);
            }
        }

        return distinct;
    }

    private sealed record TableParseResult(bool IsHighConfidence, string Markdown, double Confidence, List<string> Tags);
}
