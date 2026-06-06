using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Options;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public enum ParsedMarkdownExportStatus
{
    ExistingDoclingMarkdown,
    FallbackExported,
    SkippedEmptyText
}

public sealed record ParsedMarkdownExportResult(
    ParsedMarkdownExportStatus Status,
    string? Path,
    int CharacterCount);

public static partial class ParsedMarkdownExporter
{
    public static async Task<bool> IsValidExistingMarkdownAsync(
        string? path,
        int minMarkdownLength,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var markdown = await File.ReadAllTextAsync(path, cancellationToken);
        return markdown.Trim().Length >= Math.Max(1, minMarkdownLength);
    }

    public static async Task<ParsedMarkdownExportResult> EnsureAsync(
        string finalExtractedText,
        string documentFileName,
        string sourceFilePath,
        string extractionProvider,
        string? doclingMarkdownPath,
        DocumentParsingSettings settings,
        DateTimeOffset? generatedAt = null,
        CancellationToken cancellationToken = default)
    {
        if (await IsValidExistingMarkdownAsync(
                doclingMarkdownPath,
                settings.MinMarkdownLength,
                cancellationToken))
        {
            var existingLength = (await File.ReadAllTextAsync(
                doclingMarkdownPath!,
                cancellationToken)).Length;
            return new ParsedMarkdownExportResult(
                ParsedMarkdownExportStatus.ExistingDoclingMarkdown,
                doclingMarkdownPath,
                existingLength);
        }

        if (string.IsNullOrWhiteSpace(finalExtractedText))
        {
            return new ParsedMarkdownExportResult(
                ParsedMarkdownExportStatus.SkippedEmptyText,
                null,
                0);
        }

        var documentGuid = ResolveDocumentGuid(sourceFilePath);
        var outputRoot = Path.IsPathRooted(settings.OutputDirectory)
            ? settings.OutputDirectory
            : Path.Combine(Directory.GetCurrentDirectory(), settings.OutputDirectory);
        var outputDirectory = Path.Combine(outputRoot, documentGuid);
        var outputPath = Path.Combine(outputDirectory, $"{documentGuid}.md");
        Directory.CreateDirectory(outputDirectory);

        var markdown = BuildFallbackMarkdown(
            finalExtractedText,
            documentFileName,
            sourceFilePath,
            extractionProvider,
            generatedAt ?? DateTimeOffset.UtcNow);
        var temporaryPath = Path.Combine(
            outputDirectory,
            $".{documentGuid}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                markdown,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return new ParsedMarkdownExportResult(
            ParsedMarkdownExportStatus.FallbackExported,
            outputPath,
            markdown.Length);
    }

    public static string BuildFallbackMarkdown(
        string extractedText,
        string documentFileName,
        string sourceFilePath,
        string extractionProvider,
        DateTimeOffset generatedAt)
    {
        var builder = new StringBuilder();
        builder.Append("# ");
        builder.AppendLine(NormalizeSingleLine(documentFileName));
        builder.AppendLine();
        builder.AppendLine($"- Extraction provider: `{EscapeInlineCode(extractionProvider)}`");
        builder.AppendLine($"- Generated at: `{generatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- Source file: `{EscapeInlineCode(sourceFilePath)}`");
        builder.AppendLine();

        var normalized = extractedText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var pageMatch = PageMarkerRegex().Match(line.Trim());
            if (pageMatch.Success)
            {
                builder.AppendLine($"## Page {pageMatch.Groups["page"].Value}");
                builder.AppendLine();
                continue;
            }

            var trimmed = line.Trim();
            if (IsDetectedHeading(trimmed))
            {
                builder.Append("### ");
                builder.AppendLine(trimmed);
                builder.AppendLine();
                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string ResolveDocumentGuid(string sourceFilePath)
    {
        var fileStem = Path.GetFileNameWithoutExtension(sourceFilePath);
        if (Guid.TryParse(fileStem, out var documentGuid))
        {
            return documentGuid.ToString();
        }

        throw new InvalidOperationException(
            $"The uploaded document path does not contain a GUID filename: {sourceFilePath}");
    }

    private static bool IsDetectedHeading(string line)
        => !string.IsNullOrWhiteSpace(line)
            && line.Length <= 180
            && (ChapterHeadingRegex().IsMatch(line) || RomanHeadingRegex().IsMatch(line));

    private static string NormalizeSingleLine(string value)
        => string.Join(" ", (value ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Trim();

    private static string EscapeInlineCode(string value)
        => (value ?? string.Empty).Replace("`", "\\`", StringComparison.Ordinal);

    [GeneratedRegex(@"^\s*\[Page\s+(?<page>\d+)\]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PageMarkerRegex();

    [GeneratedRegex(@"^\s*Ch(?:\u01B0\u01A1ng|uong)\s+(?:[IVXLCDM]+|\d+)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterHeadingRegex();

    [GeneratedRegex(@"^\s*[IVXLCDM]+\.\s+\S.*$", RegexOptions.IgnoreCase)]
    private static partial Regex RomanHeadingRegex();
}
