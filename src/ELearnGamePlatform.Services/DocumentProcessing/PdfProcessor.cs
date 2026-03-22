using System.Text;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Utilities;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public class PdfProcessor : IDocumentProcessor
{
    private readonly ILogger<PdfProcessor> _logger;
    private readonly IOcrService _ocrService;

    public PdfProcessor(ILogger<PdfProcessor> logger, IOcrService ocrService)
    {
        _logger = logger;
        _ocrService = ocrService;
    }

    public async Task<string> ExtractTextAsync(string filePath, string fileType, IProgress<DocumentProcessingProgressUpdate>? progress = null)
    {
        if (!SupportedFileType(fileType))
        {
            throw new NotSupportedException($"File type {fileType} is not supported by PdfProcessor");
        }

        try
        {
            progress?.Report(new DocumentProcessingProgressUpdate
            {
                Percent = 5,
                Stage = "reading-pdf",
                StageLabel = "Doc PDF",
                Message = "Dang quet tung trang PDF",
                Detail = "Kiem tra trang nao co text truc tiep va trang nao can OCR",
                StageIndex = 2,
                StageCount = 6
            });

            var extraction = ExtractDirectTextPerPage(filePath, progress);

            if (!extraction.PagesNeedingOcr.Any())
            {
                _logger.LogInformation("PDF extracted fully from embedded text without OCR: {FilePath}", filePath);
                progress?.Report(new DocumentProcessingProgressUpdate
                {
                    Percent = 100,
                    Stage = "reading-pdf",
                    StageLabel = "Doc PDF",
                    Message = "Da doc xong PDF bang text co san",
                    Detail = $"Da trich xuat text truc tiep tu {extraction.TotalPages}/{extraction.TotalPages} trang",
                    Current = extraction.TotalPages,
                    Total = extraction.TotalPages,
                    UnitLabel = "trang",
                    StageIndex = 2,
                    StageCount = 6
                });
                return BuildMergedPdfText(extraction.TotalPages, extraction.DirectTextByPage, new Dictionary<int, string>());
            }

            _logger.LogInformation(
                "PDF requires OCR for {OcrPageCount}/{TotalPages} pages: {FilePath}",
                extraction.PagesNeedingOcr.Count,
                extraction.TotalPages,
                filePath);

            progress?.Report(new DocumentProcessingProgressUpdate
            {
                Percent = 45,
                Stage = "switching-to-ocr",
                StageLabel = "OCR trang scan",
                Message = "Phat hien trang scan can OCR",
                Detail = $"Se OCR {extraction.PagesNeedingOcr.Count}/{extraction.TotalPages} trang con thieu text",
                Current = extraction.PagesNeedingOcr.Count,
                Total = extraction.TotalPages,
                UnitLabel = "trang",
                StageIndex = 2,
                StageCount = 6
            });

            var ocrProgress = new Progress<DocumentProcessingProgressUpdate>(update =>
            {
                progress?.Report(new DocumentProcessingProgressUpdate
                {
                    Percent = MapPercent(update.Percent, 48, 100),
                    Stage = update.Stage,
                    StageLabel = update.StageLabel,
                    Message = update.Message,
                    Detail = update.Detail,
                    Current = update.Current,
                    Total = update.Total,
                    UnitLabel = update.UnitLabel,
                    StageIndex = 2,
                    StageCount = 6
                });
            });

            var ocrTextByPage = await _ocrService.ExtractTextFromPdfPagesAsync(filePath, extraction.PagesNeedingOcr, ocrProgress);
            return BuildMergedPdfText(extraction.TotalPages, extraction.DirectTextByPage, ocrTextByPage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from PDF: {FilePath}", filePath);
            throw;
        }
    }

    public bool SupportedFileType(string fileType)
    {
        return fileType.Equals("pdf", StringComparison.OrdinalIgnoreCase) ||
               fileType.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private DirectPdfExtractionResult ExtractDirectTextPerPage(string filePath, IProgress<DocumentProcessingProgressUpdate>? progress)
    {
        var directTextByPage = new Dictionary<int, string>();
        var pagesNeedingOcr = new List<int>();

        using var document = PdfDocument.Open(filePath);
        var pages = document.GetPages().ToList();
        var totalPages = Math.Max(1, pages.Count);

        for (var index = 0; index < pages.Count; index++)
        {
            var pageNumber = index + 1;
            var pageText = NormalizeDirectPdfText(pages[index].Text);
            var hasUsableText = IsDirectTextUsable(pageText);

            if (hasUsableText)
            {
                directTextByPage[pageNumber] = pageText;
            }
            else
            {
                pagesNeedingOcr.Add(pageNumber);
            }

            progress?.Report(new DocumentProcessingProgressUpdate
            {
                Percent = MapRange(index + 1, totalPages, 8, 40),
                Stage = "reading-pdf",
                StageLabel = "Doc PDF",
                Message = $"Dang kiem tra trang PDF {pageNumber}/{totalPages}",
                Detail = hasUsableText
                    ? $"Trang {pageNumber} co text nhung co san, khong can OCR"
                    : $"Trang {pageNumber} gan nhu khong co text, dua vao hang OCR",
                Current = pageNumber,
                Total = totalPages,
                UnitLabel = "trang",
                StageIndex = 2,
                StageCount = 6
            });
        }

        return new DirectPdfExtractionResult
        {
            TotalPages = totalPages,
            DirectTextByPage = directTextByPage,
            PagesNeedingOcr = pagesNeedingOcr
        };
    }

    private static string BuildMergedPdfText(
        int totalPages,
        IReadOnlyDictionary<int, string> directTextByPage,
        IReadOnlyDictionary<int, string> ocrTextByPage)
    {
        var combinedPages = new Dictionary<int, string>();

        for (var pageNumber = 1; pageNumber <= totalPages; pageNumber++)
        {
            if (ocrTextByPage.TryGetValue(pageNumber, out var ocrText) && !string.IsNullOrWhiteSpace(ocrText))
            {
                combinedPages[pageNumber] = ocrText.Trim();
                continue;
            }

            if (directTextByPage.TryGetValue(pageNumber, out var directText) && !string.IsNullOrWhiteSpace(directText))
            {
                combinedPages[pageNumber] = directText.Trim();
            }
        }

        var cleanedPages = TextCleanupUtility.RemoveRepeatedPageArtifacts(combinedPages);
        var builder = new StringBuilder();

        for (var pageNumber = 1; pageNumber <= totalPages; pageNumber++)
        {
            if (!cleanedPages.TryGetValue(pageNumber, out var pageText) || string.IsNullOrWhiteSpace(pageText))
            {
                continue;
            }

            builder.AppendLine($"[Page {pageNumber}]");
            builder.AppendLine(pageText);
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static bool IsDirectTextUsable(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var alphanumericCount = text.Count(char.IsLetterOrDigit);
        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var signalRatio = alphanumericCount / (double)Math.Max(1, text.Length);

        return alphanumericCount >= 40 && wordCount >= 12 && signalRatio >= 0.35d;
    }

    private static string NormalizeDirectPdfText(string text)
    {
        return TextCleanupUtility.CleanPageText(text);
    }

    private static int MapRange(int current, int total, int startPercent, int endPercent)
    {
        if (total <= 0)
        {
            return endPercent;
        }

        var ratio = Math.Clamp(current / (double)total, 0d, 1d);
        return startPercent + (int)Math.Round((endPercent - startPercent) * ratio);
    }

    private static int MapPercent(int percent, int startPercent, int endPercent)
    {
        var ratio = Math.Clamp(percent, 0, 100) / 100d;
        return startPercent + (int)Math.Round((endPercent - startPercent) * ratio);
    }

    private sealed class DirectPdfExtractionResult
    {
        public int TotalPages { get; init; }
        public Dictionary<int, string> DirectTextByPage { get; init; } = new();
        public List<int> PagesNeedingOcr { get; init; } = new();
    }
}
