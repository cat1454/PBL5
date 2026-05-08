using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Utilities;

namespace ELearnGamePlatform.Services.AI;

public class DocumentInputQualityGate : IDocumentInputQualityGate
{
    private readonly ITokenEstimator _tokenEstimator;
    private static readonly Regex WordRegex = new(@"\b[\p{L}\p{N}]+\b", RegexOptions.Compiled);

    public DocumentInputQualityGate(ITokenEstimator tokenEstimator)
    {
        _tokenEstimator = tokenEstimator;
    }

    public DocumentInputQualityResult Evaluate(string? extractedText)
    {
        var normalized = TextCleanupUtility.NormalizeForAi(extractedText, preserveLineBreaks: true);
        var charCount = normalized.Length;
        var wordCount = WordRegex.Matches(normalized).Count;
        var nonWhitespaceCount = normalized.Count(ch => !char.IsWhiteSpace(ch));
        var signalCount = normalized.Count(char.IsLetterOrDigit);
        var suspiciousCount = normalized.Count(IsSuspiciousCharacter);
        var shortTokenCount = WordRegex.Matches(normalized)
            .Select(match => match.Value)
            .Count(word => word.Length <= 2);
        var noiseScore = TextCleanupUtility.EstimateNoiseScore(normalized);
        var signalRatio = nonWhitespaceCount == 0 ? 0d : signalCount / (double)nonWhitespaceCount;
        var garbageRatio = nonWhitespaceCount == 0 ? 1d : suspiciousCount / (double)nonWhitespaceCount;
        var shortTokenRatio = wordCount == 0 ? 1d : shortTokenCount / (double)wordCount;
        var hasHealthyVietnameseText = wordCount >= 250 && signalRatio >= 0.50d && ContainsVietnameseSignal(normalized);
        var effectiveShortTokenRatio = hasHealthyVietnameseText ? Math.Min(shortTokenRatio, 0.30d) : shortTokenRatio;
        var tokenWasteRatio = Math.Clamp(Math.Max(garbageRatio, effectiveShortTokenRatio), 0d, 1d);
        var estimatedTokenCount = _tokenEstimator.EstimateTokens(normalized);
        var qualityScore = CalculateQualityScore(
            charCount,
            wordCount,
            signalRatio,
            garbageRatio,
            tokenWasteRatio,
            noiseScore);
        var warnings = BuildWarnings(
            charCount,
            wordCount,
            signalRatio,
            garbageRatio,
            tokenWasteRatio,
            noiseScore,
            qualityScore);

        return new DocumentInputQualityResult
        {
            Classification = Classify(
                charCount,
                wordCount,
                signalRatio,
                noiseScore,
                qualityScore,
                tokenWasteRatio),
            CharCount = charCount,
            WordCount = wordCount,
            SignalRatio = Math.Round(signalRatio, 4),
            GarbageRatio = Math.Round(garbageRatio, 4),
            TokenWasteRatio = Math.Round(tokenWasteRatio, 4),
            NoiseScore = noiseScore,
            QualityScore = qualityScore,
            EstimatedTokenCount = estimatedTokenCount,
            Warnings = warnings
        };
    }

    private static string Classify(
        int charCount,
        int wordCount,
        double signalRatio,
        int noiseScore,
        int qualityScore,
        double tokenWasteRatio)
    {
        if (charCount < 150 || wordCount < 40 || signalRatio < 0.45d || noiseScore >= 60 || qualityScore < 40)
        {
            return DocumentInputQualityClassifications.Rejected;
        }

        if (qualityScore < 60 || signalRatio < 0.60d || noiseScore >= 30 || tokenWasteRatio > 0.45d)
        {
            return DocumentInputQualityClassifications.NeedReview;
        }

        if (qualityScore < 75 || tokenWasteRatio >= 0.25d)
        {
            return DocumentInputQualityClassifications.UsableWithWarning;
        }

        return DocumentInputQualityClassifications.Good;
    }

    private static int CalculateQualityScore(
        int charCount,
        int wordCount,
        double signalRatio,
        double garbageRatio,
        double tokenWasteRatio,
        int noiseScore)
    {
        var score = 100;

        if (charCount < 150)
        {
            score -= 45;
        }
        else if (charCount < 600)
        {
            score -= 12;
        }

        if (wordCount < 40)
        {
            score -= 45;
        }
        else if (wordCount < 120)
        {
            score -= 10;
        }

        score -= (int)Math.Round(Math.Clamp(0.75d - signalRatio, 0d, 0.75d) * 55);
        score -= (int)Math.Round(Math.Clamp(garbageRatio, 0d, 1d) * 45);
        score -= (int)Math.Round(Math.Clamp(tokenWasteRatio, 0d, 1d) * 30);
        score -= Math.Min(40, noiseScore);

        return Math.Clamp(score, 0, 100);
    }

    private static List<string> BuildWarnings(
        int charCount,
        int wordCount,
        double signalRatio,
        double garbageRatio,
        double tokenWasteRatio,
        int noiseScore,
        int qualityScore)
    {
        var warnings = new List<string>();

        if (charCount < 150)
        {
            warnings.Add("Extracted text is too short for reliable AI analysis.");
        }

        if (wordCount < 40)
        {
            warnings.Add("Extracted text has too few words for reliable AI analysis.");
        }

        if (signalRatio < 0.60d)
        {
            warnings.Add("Extracted text has a low letter/digit signal ratio.");
        }

        if (garbageRatio > 0.35d)
        {
            warnings.Add("Extracted text contains many suspicious OCR characters.");
        }

        if (tokenWasteRatio >= 0.25d)
        {
            warnings.Add("Extracted text may waste prompt budget on low-signal tokens.");
        }

        if (noiseScore >= 30)
        {
            warnings.Add($"Existing OCR noise heuristic reported a high noise score ({noiseScore}).");
        }

        if (qualityScore < 75)
        {
            warnings.Add($"Document input quality score is {qualityScore}/100.");
        }

        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsSuspiciousCharacter(char ch)
        => !char.IsLetterOrDigit(ch)
            && !char.IsWhiteSpace(ch)
            && ",.;:?!()[]\"'/%+-_:".IndexOf(ch) < 0;

    private static bool ContainsVietnameseSignal(string text)
        => Regex.IsMatch(
            text,
            @"[ăâđêôơưáàảãạấầẩẫậắằẳẵặéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ]|\b(và|là|của|được|trong|không|chương|phần|mục)\b",
            RegexOptions.IgnoreCase);
}
