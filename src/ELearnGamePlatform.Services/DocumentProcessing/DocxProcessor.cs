using ELearnGamePlatform.Core.Entities;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Utilities;
using Microsoft.Extensions.Logging;
using System.Text;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public class DocxProcessor : IDocumentProcessor
{
    private readonly ILogger<DocxProcessor> _logger;

    public DocxProcessor(ILogger<DocxProcessor> logger)
    {
        _logger = logger;
    }

    public Task<string> ExtractTextAsync(string filePath, string fileType, IProgress<DocumentProcessingProgressUpdate>? progress = null)
    {
        if (!SupportedFileType(fileType))
        {
            throw new NotSupportedException($"File type {fileType} is not supported by DocxProcessor");
        }

        try
        {
            progress?.Report(new DocumentProcessingProgressUpdate
            {
                Percent = 5,
                Stage = "reading-docx",
                StageLabel = "Trich xuat DOCX",
                Message = "Dang doc noi dung DOCX",
                Detail = "Quet doan van va bang trong tai lieu",
                StageIndex = 2,
                StageCount = 6
            });

            var text = TextCleanupUtility.NormalizeForAi(ExtractTextFromDocx(filePath, progress), preserveLineBreaks: true);
            return Task.FromResult(text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from DOCX: {FilePath}", filePath);
            throw;
        }
    }

    public bool SupportedFileType(string fileType)
    {
        return fileType.Equals("docx", StringComparison.OrdinalIgnoreCase) ||
               fileType.Equals(".docx", StringComparison.OrdinalIgnoreCase);
    }

    private string ExtractTextFromDocx(string filePath, IProgress<DocumentProcessingProgressUpdate>? progress)
    {
        var textBuilder = new StringBuilder();

        using (WordprocessingDocument wordDoc = WordprocessingDocument.Open(filePath, false))
        {
            if (wordDoc.MainDocumentPart?.Document.Body != null)
            {
                var paragraphs = wordDoc.MainDocumentPart.Document.Body.Elements<Paragraph>().ToList();
                var tables = wordDoc.MainDocumentPart.Document.Body.Elements<Table>().ToList();
                var totalUnits = Math.Max(1, paragraphs.Count + tables.Count);
                var processedUnits = 0;

                foreach (var paragraph in paragraphs)
                {
                    var paragraphText = paragraph.InnerText;
                    if (!string.IsNullOrWhiteSpace(paragraphText))
                    {
                        textBuilder.AppendLine(paragraphText);
                    }

                    processedUnits++;
                    ReportDocxProgress(progress, processedUnits, totalUnits);
                }

                // Extract text from tables
                foreach (var table in tables)
                {
                    foreach (var row in table.Elements<TableRow>())
                    {
                        foreach (var cell in row.Elements<TableCell>())
                        {
                            var cellText = cell.InnerText;
                            if (!string.IsNullOrWhiteSpace(cellText))
                            {
                                textBuilder.Append(cellText + "\t");
                            }
                        }
                        textBuilder.AppendLine();
                    }

                    processedUnits++;
                    ReportDocxProgress(progress, processedUnits, totalUnits);
                }
            }
        }

        return textBuilder.ToString();
    }

    private static void ReportDocxProgress(IProgress<DocumentProcessingProgressUpdate>? progress, int current, int total)
    {
        progress?.Report(new DocumentProcessingProgressUpdate
        {
            Percent = Math.Max(10, (int)Math.Round((current / (double)Math.Max(1, total)) * 100d)),
            Stage = "reading-docx",
            StageLabel = "Trich xuat DOCX",
            Message = $"Dang doc cau truc DOCX {current}/{total}",
            Detail = $"Da xu ly {current}/{total} khoi noi dung cua tai lieu",
            Current = current,
            Total = total,
            UnitLabel = "khoi noi dung",
            StageIndex = 2,
            StageCount = 6
        });
    }
}
