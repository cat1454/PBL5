using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ELearnGamePlatform.Core.Interfaces;
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

    public Task<string> ExtractTextAsync(string filePath, string fileType)
    {
        if (!SupportedFileType(fileType))
        {
            throw new NotSupportedException($"File type {fileType} is not supported by DocxProcessor");
        }

        try
        {
            var text = ExtractTextFromDocx(filePath);
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

    private string ExtractTextFromDocx(string filePath)
    {
        var textBuilder = new StringBuilder();

        using (WordprocessingDocument wordDoc = WordprocessingDocument.Open(filePath, false))
        {
            if (wordDoc.MainDocumentPart?.Document.Body != null)
            {
                foreach (var paragraph in wordDoc.MainDocumentPart.Document.Body.Elements<Paragraph>())
                {
                    var paragraphText = paragraph.InnerText;
                    if (!string.IsNullOrWhiteSpace(paragraphText))
                    {
                        textBuilder.AppendLine(paragraphText);
                    }
                }

                // Extract text from tables
                foreach (var table in wordDoc.MainDocumentPart.Document.Body.Elements<Table>())
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
                }
            }
        }

        return textBuilder.ToString();
    }
}
