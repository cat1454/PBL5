using System.Diagnostics;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using Microsoft.Extensions.Logging;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public class VisionPageImageProvider : IVisionPageImageProvider
{
    private const int VisionPdfDpi = 180;
    private readonly ILogger<VisionPageImageProvider> _logger;
    private readonly string _pdfToPpmPath;

    public VisionPageImageProvider(ILogger<VisionPageImageProvider> logger)
    {
        _logger = logger;
        _pdfToPpmPath = ResolvePdfToPpmPath();
    }

    public async Task<VisionPageImageSource?> GetPageImageAsync(
        string filePath,
        string fileType,
        int pageNumber,
        CancellationToken cancellationToken = default)
    {
        if (IsImageFile(fileType, filePath))
        {
            return File.Exists(filePath)
                ? new VisionPageImageSource { ImagePath = filePath, PageNumber = pageNumber }
                : null;
        }

        if (!IsPdfFile(fileType, filePath))
        {
            return null;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"elearn_pdf_vision_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var outputPrefix = Path.Combine(tempDirectory, $"page_{pageNumber}");
            var startInfo = new ProcessStartInfo
            {
                FileName = _pdfToPpmPath,
                Arguments = $"-f {pageNumber} -l {pageNumber} -r {VisionPdfDpi} -png \"{filePath}\" \"{outputPrefix}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                CleanupTempDirectory(tempDirectory);
                return null;
            }

            var stdErr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning(
                    "pdftoppm failed while rendering page {PageNumber} for vision (exit code {ExitCode}): {Error}",
                    pageNumber,
                    process.ExitCode,
                    stdErr);
                CleanupTempDirectory(tempDirectory);
                return null;
            }

            var imagePath = Directory
                .GetFiles(tempDirectory, $"page_{pageNumber}*.png")
                .OrderBy(file => file)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(imagePath))
            {
                CleanupTempDirectory(tempDirectory);
                return null;
            }

            return new VisionPageImageSource
            {
                ImagePath = imagePath,
                PageNumber = pageNumber,
                TemporaryDirectory = tempDirectory
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not render PDF page {PageNumber} for vision. Checked pdftoppm executable: {PdfToPpmPath}",
                pageNumber,
                _pdfToPpmPath);
            CleanupTempDirectory(tempDirectory);
            return null;
        }
    }

    private static bool IsImageFile(string fileType, string filePath)
    {
        var extension = NormalizeExtension(fileType, filePath);
        return extension is ".png" or ".jpg" or ".jpeg";
    }

    private static bool IsPdfFile(string fileType, string filePath)
        => NormalizeExtension(fileType, filePath) == ".pdf";

    private static string NormalizeExtension(string fileType, string filePath)
    {
        var extension = string.IsNullOrWhiteSpace(fileType)
            ? Path.GetExtension(filePath)
            : fileType.Trim();
        return extension.StartsWith(".", StringComparison.Ordinal)
            ? extension.ToLowerInvariant()
            : $".{extension.ToLowerInvariant()}";
    }

    private string ResolvePdfToPpmPath()
    {
        var candidates = new List<string>
        {
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "poppler-25.12.0", "Library", "bin", "pdftoppm.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "poppler-25.12.0", "Library", "bin", "pdftoppm.exe"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                _logger.LogInformation("Using bundled pdftoppm executable for vision at {PdfToPpmPath}", candidate);
                return candidate;
            }
        }

        return "pdftoppm";
    }

    private static void CleanupTempDirectory(string tempDirectory)
    {
        try
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Temporary vision images are best-effort cleanup.
        }
    }
}
