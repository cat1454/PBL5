using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Extensions;

public static class DocumentTextExtensions
{
    public static string? GetPreferredTextForAi(this Document document)
    {
        if (!string.IsNullOrWhiteSpace(document.CleanedText))
        {
            return document.CleanedText;
        }

        if (!string.IsNullOrWhiteSpace(document.RawOcrText))
        {
            return document.RawOcrText;
        }

        return document.ExtractedText;
    }
}
