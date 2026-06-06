using System.Text;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public static class VietnameseMojibakeRepair
{
    private const int MojibakeMarkerThreshold = 3;
    private const string ReplacementCharacter = "\uFFFD";
    private const string MojibakeBulletPrefix = "\u00EF\u201A";
    private const string PrivateUseBullet = "\uF0B7";
    private static readonly string[] MojibakeMarkers =
    [
        "\u00C3",
        "\u00C4",
        "\u00C6",
        "\u00E1\u00BB",
        "\u00E1\u00BA",
        "\u00C2",
        MojibakeBulletPrefix,
        ReplacementCharacter
    ];
    private static readonly string[] ObviousMojibakePatterns =
    [
        "D\u00C3",
        "c\u00C3\u00A2u",
        "h\u00E1\u00BB",
        "t\u00E1\u00BA",
        "\u00C4\u2018"
    ];
    private static readonly char[] VietnameseLetters =
    [
        '\u0103', '\u00E2', '\u0111', '\u00EA', '\u00F4', '\u01A1', '\u01B0',
        '\u00E1', '\u00E0', '\u1EA3', '\u00E3', '\u1EA1',
        '\u00E9', '\u00E8', '\u00ED', '\u00EC',
        '\u00F3', '\u00F2', '\u00FA', '\u00F9'
    ];
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    static VietnameseMojibakeRepair()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static bool IsLikelyMojibake(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return ObviousMojibakePatterns.Any(pattern =>
               text.Contains(pattern, StringComparison.Ordinal)) ||
               text.Contains(MojibakeBulletPrefix, StringComparison.Ordinal) ||
               text.Contains(PrivateUseBullet, StringComparison.Ordinal) ||
               CountMojibakeMarkers(text) >= MojibakeMarkerThreshold;
    }

    public static string TryRepair(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var bulletNormalized = NormalizeBullets(text);
        if (!IsLikelyMojibake(text))
        {
            return bulletNormalized;
        }

        var candidates = new List<string>();
        TryAddWindows1252Candidate(bulletNormalized, candidates);
        TryAddRecoveredByteCandidate(bulletNormalized, candidates);

        return candidates
            .Where(candidate => IsRepairSuccessful(text, candidate))
            .OrderBy(CountMojibakeMarkers)
            .ThenBy(CountReplacementCharacters)
            .ThenByDescending(CountVietnameseLetters)
            .FirstOrDefault() ?? bulletNormalized;
    }

    public static bool IsRepairSuccessful(string original, string repaired)
    {
        if (string.IsNullOrWhiteSpace(repaired) ||
            string.Equals(original, repaired, StringComparison.Ordinal))
        {
            return false;
        }

        var minimumLength = Math.Max(1, original.Length / 2);
        return repaired.Length >= minimumLength &&
               CountMojibakeMarkers(repaired) < CountMojibakeMarkers(original) &&
               CountVietnameseLetters(repaired) > 0 &&
               CountReplacementCharacters(repaired) <= 1;
    }

    private static void TryAddWindows1252Candidate(string text, ICollection<string> candidates)
    {
        try
        {
            var windows1252 = Encoding.GetEncoding(
                1252,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            candidates.Add(NormalizeBullets(StrictUtf8.GetString(windows1252.GetBytes(text))));
        }
        catch (Exception ex) when (
            ex is EncoderFallbackException or DecoderFallbackException or ArgumentException)
        {
            // The mixed Latin-1/Windows-1252 recovery below can still handle this text.
        }
    }

    private static void TryAddRecoveredByteCandidate(string text, ICollection<string> candidates)
    {
        try
        {
            var bytes = text.Select(ToOriginalByte).ToArray();
            candidates.Add(NormalizeBullets(StrictUtf8.GetString(bytes)));
        }
        catch (Exception ex) when (
            ex is EncoderFallbackException or DecoderFallbackException or InvalidOperationException)
        {
            // Return the original text when deterministic byte recovery is impossible.
        }
    }

    private static byte ToOriginalByte(char character)
    {
        if (character <= byte.MaxValue)
        {
            return (byte)character;
        }

        var windows1252 = Encoding.GetEncoding(
            1252,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
        var bytes = windows1252.GetBytes([character]);
        if (bytes.Length != 1)
        {
            throw new InvalidOperationException("Character cannot be represented as one source byte.");
        }

        return bytes[0];
    }

    private static string NormalizeBullets(string text)
        => text
            .Replace("\u00EF\u201A\u00B7", "-", StringComparison.Ordinal)
            .Replace(PrivateUseBullet, "-", StringComparison.Ordinal);

    private static int CountMojibakeMarkers(string text)
        => MojibakeMarkers.Sum(marker => CountOccurrences(text, marker));

    private static int CountReplacementCharacters(string text)
        => CountOccurrences(text, ReplacementCharacter);

    private static int CountVietnameseLetters(string text)
        => text.Count(character => VietnameseLetters.Contains(char.ToLowerInvariant(character)));

    private static int CountOccurrences(string value, string marker)
    {
        var count = 0;
        var searchIndex = 0;
        while ((searchIndex = value.IndexOf(marker, searchIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            searchIndex += marker.Length;
        }

        return count;
    }
}
