using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Interfaces;

namespace ELearnGamePlatform.Services.AI;

public class TokenEstimator : ITokenEstimator
{
    private static readonly Regex WordRegex = new(@"\b[\p{L}\p{N}]+\b", RegexOptions.Compiled);

    public int EstimateTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var charCountEstimate = (int)Math.Ceiling(text.Length / 4.0d);
        var wordCount = WordRegex.Matches(text).Count;
        var wordCountEstimate = (int)Math.Ceiling(wordCount * 1.35d);
        return Math.Max(charCountEstimate, wordCountEstimate);
    }
}
