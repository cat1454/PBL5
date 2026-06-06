using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using ELearnGamePlatform.Core.Options;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public sealed record DocumentExtractionSelection(
    string Text,
    string Provider,
    bool? ExternalParsingSucceeded,
    long? ExternalParsingElapsedMs,
    string? ExternalParsingError,
    string? ExternalMarkdownPath);

public sealed class DocumentParsingException : Exception
{
    public DocumentParsingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public static class DocumentExtractionSelector
{
    public static async Task<DocumentExtractionSelection> SelectAsync(
        string legacyExtractedText,
        string filePath,
        string fileType,
        DocumentParsingSettings settings,
        IExternalDocumentParser externalParser,
        CancellationToken cancellationToken = default)
    {
        legacyExtractedText ??= string.Empty;
        if (!settings.Enabled)
        {
            return Legacy(legacyExtractedText);
        }

        ExternalDocumentParseResult parseResult;
        try
        {
            parseResult = await externalParser.TryParseAsync(
                filePath,
                fileType,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HandleFailure(legacyExtractedText, settings, ex.Message, null, ex);
        }

        var markdown = parseResult.Markdown ?? string.Empty;
        var provider = string.Equals(
            parseResult.Provider,
            "docling-repaired",
            StringComparison.OrdinalIgnoreCase)
                ? "docling-repaired"
                : "docling";
        if (parseResult.Success && VietnameseMojibakeRepair.IsLikelyMojibake(markdown))
        {
            var repaired = VietnameseMojibakeRepair.TryRepair(markdown);
            if (!VietnameseMojibakeRepair.IsRepairSuccessful(markdown, repaired) ||
                repaired.Length < settings.MinMarkdownLength)
            {
                return HandleFailure(
                    legacyExtractedText,
                    settings,
                    "Docling output rejected because mojibake repair failed.",
                    parseResult.ElapsedMs);
            }

            markdown = repaired;
            provider = "docling-repaired";
        }

        if (parseResult.Success && markdown.Length >= settings.MinMarkdownLength)
        {
            return new DocumentExtractionSelection(
                settings.PreferMarkdownForGeneration ? markdown : legacyExtractedText,
                settings.PreferMarkdownForGeneration ? provider : "legacy",
                true,
                parseResult.ElapsedMs,
                null,
                parseResult.OutputPath);
        }

        var error = parseResult.Error
            ?? $"Parsed markdown too short ({markdown.Length} < {settings.MinMarkdownLength}).";
        return HandleFailure(legacyExtractedText, settings, error, parseResult.ElapsedMs);
    }

    private static DocumentExtractionSelection HandleFailure(
        string legacyExtractedText,
        DocumentParsingSettings settings,
        string error,
        long? elapsedMs,
        Exception? innerException = null)
    {
        if (settings.FallbackToLegacy)
        {
            return Legacy(legacyExtractedText, false, elapsedMs, error);
        }

        throw new DocumentParsingException(
            $"Docling parsing failed and legacy fallback is disabled: {error}",
            innerException);
    }

    private static DocumentExtractionSelection Legacy(
        string text,
        bool? externalParsingSucceeded = null,
        long? elapsedMs = null,
        string? error = null)
        => new(text, "legacy", externalParsingSucceeded, elapsedMs, error, null);
}
