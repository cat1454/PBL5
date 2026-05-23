using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Utilities;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public class LegacyDocumentQualityScorer : IDocumentQualityScorer
{
    private static readonly Regex WordRegex = new(@"\b[\p{L}\p{N}]+\b", RegexOptions.Compiled);

    public DocumentQualityScoreResult Score(DocumentQualityScoreInput input)
    {
        var text = TextCleanupUtility.NormalizeForAi(input.ExtractedText, preserveLineBreaks: true);
        var charCount = text.Length;
        var words = WordRegex.Matches(text).Select(match => match.Value).ToList();
        var wordCount = words.Count;
        var nonWhitespaceCount = text.Count(ch => !char.IsWhiteSpace(ch));
        var suspiciousCount = text.Count(IsSuspiciousCharacter);
        var garbageRatio = nonWhitespaceCount == 0 ? 1d : suspiciousCount / (double)nonWhitespaceCount;
        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
        var shortLineCount = lines.Count(line => WordRegex.Matches(line).Count <= 2 || line.Count(char.IsLetterOrDigit) < 8);
        var shortLineRatio = lines.Count == 0 ? (charCount == 0 ? 1d : 0d) : shortLineCount / (double)lines.Count;
        var report = input.PageQualityReport;
        var totalPages = report?.TotalPages ?? 0;
        var pageReports = report?.Pages ?? new List<DocumentPageProcessingReport>();
        var bodyPages = pageReports
            .Where(page => page.PageRole is null or DocumentPageRoles.Body or DocumentPageRoles.FootnoteHeavy)
            .ToList();
        var qualityPages = bodyPages.Count > 0 ? bodyPages : pageReports;
        var lowTextPageCount = qualityPages.Count(page => page.CharCount < 80 || page.WordCount < 12);
        var ocrConfidences = qualityPages
            .Where(page => page.Confidence.HasValue)
            .Select(page => page.Confidence!.Value)
            .ToList();
        var averageOcrConfidence = ocrConfidences.Count == 0 ? (double?)null : ocrConfidences.Average();
        var reasons = new List<string>();

        var lengthScore = ScoreTextLength(charCount, wordCount, reasons);
        var garbagePenalty = Math.Clamp(garbageRatio * 1.35d, 0d, 0.40d);
        var shortLinePenalty = Math.Clamp(Math.Max(0d, shortLineRatio - 0.25d) * 0.45d, 0d, 0.22d);
        var pagePenalty = CalculateLowTextPagePenalty(lowTextPageCount, qualityPages.Count, reasons);
        var ocrPenalty = CalculateOcrPenalty(averageOcrConfidence, reasons);
        var pageQualityAdjustment = CalculatePageQualityAdjustment(report, reasons);

        if (garbageRatio > 0.18d)
        {
            reasons.Add($"Extracted text has high garbage ratio ({garbageRatio:P0}).");
        }

        if (shortLineRatio > 0.45d)
        {
            reasons.Add($"Extracted text has many short or broken lines ({shortLineRatio:P0}).");
        }

        var confidence = Math.Clamp(
            lengthScore - garbagePenalty - shortLinePenalty - pagePenalty - ocrPenalty + pageQualityAdjustment,
            0d,
            1d);
        confidence = Math.Round(confidence, 4);

        var status = MapStatus(confidence);
        if (status != DocumentQualityStatuses.AutoGenerateAllowed && reasons.Count == 0)
        {
            reasons.Add($"Document quality confidence is {confidence:P0}.");
        }

        return new DocumentQualityScoreResult
        {
            Confidence = confidence,
            Status = status,
            NeedsReview = status != DocumentQualityStatuses.AutoGenerateAllowed,
            CharCount = charCount,
            WordCount = wordCount,
            GarbageRatio = Math.Round(garbageRatio, 4),
            ShortLineRatio = Math.Round(shortLineRatio, 4),
            AverageOcrConfidence = averageOcrConfidence.HasValue ? Math.Round(averageOcrConfidence.Value, 4) : null,
            LowTextPageCount = lowTextPageCount,
            TotalPages = totalPages,
            Reasons = reasons
                .Concat(report?.TopQualityPenalties ?? Enumerable.Empty<string>())
                .Concat(report?.Warnings ?? Enumerable.Empty<string>())
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList()
        };
    }

    public double ScoreLegacyText(string? legacyExtractedText)
        => Score(new DocumentQualityScoreInput { ExtractedText = legacyExtractedText }).Confidence;

    private static double ScoreTextLength(int charCount, int wordCount, List<string> reasons)
    {
        if (charCount == 0 || wordCount == 0)
        {
            reasons.Add("Extracted text is empty.");
            return 0.18d;
        }

        if (charCount < 150 || wordCount < 40)
        {
            reasons.Add("Extracted text is too short for reliable generation.");
            return 0.40d;
        }

        if (charCount < 600 || wordCount < 120)
        {
            reasons.Add("Extracted text is short and should be reviewed.");
            return 0.70d;
        }

        if (charCount < 1500 || wordCount < 250)
        {
            return 0.84d;
        }

        return 0.94d;
    }

    private static double CalculateLowTextPagePenalty(int lowTextPageCount, int pageCount, List<string> reasons)
    {
        if (lowTextPageCount <= 0 || pageCount <= 0)
        {
            return 0d;
        }

        var ratio = lowTextPageCount / (double)pageCount;
        reasons.Add($"{lowTextPageCount}/{pageCount} low-text page(s) detected.");
        return Math.Clamp(ratio * 0.24d, 0.04d, 0.24d);
    }

    private static double CalculateOcrPenalty(double? averageOcrConfidence, List<string> reasons)
    {
        if (!averageOcrConfidence.HasValue)
        {
            return 0d;
        }

        if (averageOcrConfidence.Value >= 0.82d)
        {
            return 0d;
        }

        reasons.Add($"Average OCR confidence is low ({averageOcrConfidence.Value:P0}).");
        return Math.Clamp((0.82d - averageOcrConfidence.Value) * 0.35d, 0.02d, 0.20d);
    }

    private static double CalculatePageQualityAdjustment(DocumentInputQualityReport? report, List<string> reasons)
    {
        if (report == null)
        {
            return 0d;
        }

        var pageQuality = report.BodyPageQualityAverage > 0 ? report.BodyPageQualityAverage : report.AveragePageQualityWeighted;
        if (pageQuality <= 0)
        {
            return 0d;
        }

        if (pageQuality < 55)
        {
            reasons.Add($"Page quality average is low ({pageQuality}/100).");
            return -0.08d;
        }

        if (pageQuality >= 75)
        {
            return 0.04d;
        }

        return 0d;
    }

    private static string MapStatus(double confidence)
        => confidence switch
        {
            >= 0.85d => DocumentQualityStatuses.AutoGenerateAllowed,
            >= 0.65d => DocumentQualityStatuses.NeedsReview,
            >= 0.45d => DocumentQualityStatuses.SummaryOnlyRecommended,
            _ => DocumentQualityStatuses.ExtractionFailed
        };

    private static bool IsSuspiciousCharacter(char ch)
        => !char.IsLetterOrDigit(ch)
            && !char.IsWhiteSpace(ch)
            && ",.;:?!()[]\"'/%+-_:".IndexOf(ch) < 0;
}
