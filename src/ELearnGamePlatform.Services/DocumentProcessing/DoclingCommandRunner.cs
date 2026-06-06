using System.ComponentModel;
using System.Diagnostics;
using ELearnGamePlatform.Core.Options;
using Microsoft.Extensions.Logging;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public interface IDoclingCommandRunner
{
    Task<DoclingCommandResult> ConvertAsync(
        string filePath,
        DocumentParsingSettings options,
        CancellationToken cancellationToken = default);
}

public sealed record DoclingCommandResult(
    bool Success,
    string? Markdown,
    string? FailureReason);

public class DoclingCommandRunner : IDoclingCommandRunner
{
    private const int MaxLoggedErrorCharacters = 2000;
    private readonly ILogger<DoclingCommandRunner> _logger;

    public DoclingCommandRunner(ILogger<DoclingCommandRunner> logger)
    {
        _logger = logger;
    }

    public async Task<DoclingCommandResult> ConvertAsync(
        string filePath,
        DocumentParsingSettings options,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return Failed($"Input file does not exist: {filePath}");
        }

        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "pbl5-docling",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        try
        {
            using var process = new Process
            {
                StartInfo = BuildStartInfo(filePath, outputDirectory, options)
            };

            try
            {
                if (!process.Start())
                {
                    return Failed("Docling command did not start.");
                }
            }
            catch (Win32Exception ex)
            {
                return Failed($"Docling command could not be started: {ex.Message}");
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)));

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

                return Failed($"Docling timed out after {options.TimeoutSeconds} seconds.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;

            if (process.ExitCode != 0)
            {
                return Failed(
                    $"Docling exited with code {process.ExitCode}: {SummarizeError(standardError, standardOutput)}");
            }

            var markdownPath = Path.Combine(
                outputDirectory,
                $"{Path.GetFileNameWithoutExtension(filePath)}.md");
            if (!File.Exists(markdownPath))
            {
                return Failed(
                    $"Docling completed without the expected Markdown file. Output={SummarizeError(standardOutput, standardError)}");
            }

            return new DoclingCommandResult(
                true,
                await File.ReadAllTextAsync(markdownPath, cancellationToken),
                null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Docling conversion raised an unexpected error for {FilePath}.", filePath);
            return Failed(ex.Message);
        }
        finally
        {
            try
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not delete Docling temporary directory {OutputDirectory}.", outputDirectory);
            }
        }
    }

    private static ProcessStartInfo BuildStartInfo(
        string filePath,
        string outputDirectory,
        DocumentParsingSettings options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(options.DoclingCommand) ? "docling" : options.DoclingCommand,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(filePath);
        startInfo.ArgumentList.Add("--to");
        startInfo.ArgumentList.Add("md");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputDirectory);
        startInfo.ArgumentList.Add("--abort-on-error");
        startInfo.ArgumentList.Add("--image-export-mode");
        startInfo.ArgumentList.Add("placeholder");
        startInfo.ArgumentList.Add("--document-timeout");
        startInfo.ArgumentList.Add(Math.Max(1, options.TimeoutSeconds).ToString());
        startInfo.ArgumentList.Add("--num-threads");
        startInfo.ArgumentList.Add("4");
        startInfo.ArgumentList.Add("--device");
        startInfo.ArgumentList.Add("auto");
        return startInfo;
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
            // Best effort after timeout; the legacy extraction path will continue.
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
            // Best effort cleanup after cancellation or timeout.
        }
    }

    private static DoclingCommandResult Failed(string reason)
        => new(false, null, reason);

    private static string SummarizeError(params string[] values)
    {
        var message = string.Join(
            " ",
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()));
        return message.Length <= MaxLoggedErrorCharacters
            ? message
            : message[..MaxLoggedErrorCharacters];
    }
}
