using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using ELearnGamePlatform.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public sealed class DoclingDocumentParser : IExternalDocumentParser
{
    private const int MaxLoggedOutputLength = 2000;
    private static readonly Regex MarkdownLinkRegex = new(
        @"!?\[([^\]]*)\]\([^)]+\)",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownSyntaxRegex = new(
        @"(^|\s)[#>*_`~-]+(?=\s|$)",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ExcessWhitespaceRegex = new(
        @"[ \t]+",
        RegexOptions.Compiled);
    private static readonly Regex ExcessBlankLinesRegex = new(
        @"(\r?\n){3,}",
        RegexOptions.Compiled);

    private readonly DocumentParsingSettings _settings;
    private readonly ILogger<DoclingDocumentParser> _logger;

    public DoclingDocumentParser(
        IOptions<DocumentParsingSettings> settings,
        ILogger<DoclingDocumentParser> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ExternalDocumentParseResult> TryParseAsync(
        string filePath,
        string fileType,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!_settings.Enabled)
        {
            return Failed("External document parsing is disabled.", stopwatch);
        }

        if (!string.Equals(_settings.Provider, "docling", StringComparison.OrdinalIgnoreCase))
        {
            return Failed(
                $"External document parsing provider '{_settings.Provider}' is not supported.",
                stopwatch);
        }

        if (!File.Exists(filePath))
        {
            return Failed($"Input file does not exist: {filePath}", stopwatch);
        }

        string outputDirectory;
        try
        {
            outputDirectory = CreateOutputDirectory(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create Docling output directory for {FilePath}.", filePath);
            return Failed(ex.Message, stopwatch);
        }

        using var process = new Process
        {
            StartInfo = BuildStartInfo(filePath, outputDirectory)
        };

        try
        {
            try
            {
                if (!process.Start())
                {
                    return Failed("Docling command did not start.", stopwatch);
                }
            }
            catch (Win32Exception ex)
            {
                return Failed(
                    $"Docling command could not be started: {ex.Message}",
                    stopwatch,
                    commandMissing: true);
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds)));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await WaitForExitAfterKillAsync(process);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return Failed(
                    $"Docling timed out after {_settings.TimeoutSeconds} seconds.",
                    stopwatch,
                    timedOut: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;

            if (process.ExitCode != 0)
            {
                return Failed(
                    $"Docling exited with code {process.ExitCode}: {SummarizeOutput(standardError, standardOutput)}",
                    stopwatch);
            }

            var markdownPath = Directory
                .EnumerateFiles(outputDirectory, "*.md", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (markdownPath == null)
            {
                return Failed(
                    $"Docling completed without a Markdown file. Output={SummarizeOutput(standardOutput, standardError)}",
                    stopwatch);
            }

            var markdown = (await File.ReadAllTextAsync(markdownPath, cancellationToken)).Trim();
            if (markdown.Length < Math.Max(1, _settings.MinMarkdownLength))
            {
                return Failed("Parsed markdown too short", stopwatch);
            }

            var provider = "docling";
            if (VietnameseMojibakeRepair.IsLikelyMojibake(markdown))
            {
                var repaired = VietnameseMojibakeRepair.TryRepair(markdown);
                if (!VietnameseMojibakeRepair.IsRepairSuccessful(markdown, repaired) ||
                    repaired.Length < Math.Max(1, _settings.MinMarkdownLength))
                {
                    return Failed(
                        "Docling output rejected because mojibake repair failed.",
                        stopwatch);
                }

                markdown = repaired;
                provider = "docling-repaired";
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "Docling parsed {FilePath} to {OutputPath}: markdownChars={MarkdownCharacters}, elapsedMs={ElapsedMs}.",
                filePath,
                markdownPath,
                markdown.Length,
                stopwatch.ElapsedMilliseconds);
            return new ExternalDocumentParseResult
            {
                Success = true,
                Provider = provider,
                Markdown = markdown,
                PlainText = ConvertMarkdownToPlainText(markdown),
                OutputPath = markdownPath,
                ElapsedMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Docling parsing failed for {FilePath}.", filePath);
            return Failed(ex.Message, stopwatch);
        }
    }

    private ProcessStartInfo BuildStartInfo(string filePath, string outputDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(_settings.DoclingCommand)
                ? "docling"
                : _settings.DoclingCommand,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in BuildDoclingArguments(filePath, outputDirectory))
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    // Keep Docling CLI compatibility changes isolated here. OCR stays opt-in.
    private static IReadOnlyList<string> BuildDoclingArguments(
        string filePath,
        string outputDirectory)
        =>
        [
            filePath,
            "--to",
            "md",
            "--output",
            outputDirectory,
            "--no-ocr",
            "--image-export-mode",
            "placeholder"
        ];

    private string CreateOutputDirectory(string filePath)
    {
        var root = Path.IsPathRooted(_settings.OutputDirectory)
            ? _settings.OutputDirectory
            : Path.Combine(Directory.GetCurrentDirectory(), _settings.OutputDirectory);
        Directory.CreateDirectory(root);

        var fileName = SanitizeFileName(Path.GetFileNameWithoutExtension(filePath));
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        var outputDirectory = Path.Combine(root, $"{fileName}-{shortGuid}");
        Directory.CreateDirectory(outputDirectory);
        return outputDirectory;
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(character => invalidCharacters.Contains(character) ? '-' : character)
            .ToArray())
            .Trim(' ', '.', '-');
        return string.IsNullOrWhiteSpace(sanitized) ? "document" : sanitized;
    }

    private static string ConvertMarkdownToPlainText(string markdown)
    {
        var withoutLinks = MarkdownLinkRegex.Replace(markdown, "$1");
        var withoutSyntax = MarkdownSyntaxRegex.Replace(withoutLinks, "$1");
        var normalizedSpaces = ExcessWhitespaceRegex.Replace(withoutSyntax, " ");
        return ExcessBlankLinesRegex.Replace(normalizedSpaces, Environment.NewLine + Environment.NewLine).Trim();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort after timeout; the ingestion fallback remains available.
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch
        {
            // Best effort process cleanup.
        }
    }

    private static ExternalDocumentParseResult Failed(
        string error,
        Stopwatch stopwatch,
        bool timedOut = false,
        bool commandMissing = false)
    {
        stopwatch.Stop();
        return new ExternalDocumentParseResult
        {
            Success = false,
            Provider = "docling",
            Error = error,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            TimedOut = timedOut,
            CommandMissing = commandMissing
        };
    }

    private static string SummarizeOutput(params string[] values)
    {
        var output = string.Join(
            " ",
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()));
        return output.Length <= MaxLoggedOutputLength
            ? output
            : output[..MaxLoggedOutputLength];
    }
}
