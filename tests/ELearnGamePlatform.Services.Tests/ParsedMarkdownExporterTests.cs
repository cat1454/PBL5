using System.Text;
using ELearnGamePlatform.Core.Options;
using ELearnGamePlatform.Services.DocumentProcessing;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class ParsedMarkdownExporterTests
{
    [Fact]
    public async Task EnsureAsync_ExportsCanonicalUtf8MarkdownWithPagesAndHeadings()
    {
        var root = CreateTemporaryDirectory();
        var documentGuid = Guid.NewGuid();
        var sourcePath = Path.Combine(root, $"{documentGuid}.pdf");
        var generatedAt = new DateTimeOffset(2026, 6, 6, 10, 30, 0, TimeSpan.Zero);
        var settings = new DocumentParsingSettings
        {
            OutputDirectory = Path.Combine(root, "parsed"),
            MinMarkdownLength = 20
        };
        var text = string.Join(
            "\n",
            "[Page 1]",
            "Ch\u01B0\u01A1ng I M\u1EDF \u0111\u1EA7u",
            "N\u1ED9i dung trang m\u1ED9t.",
            "[Page 2]",
            "II. B\u1ED1i c\u1EA3nh",
            "N\u1ED9i dung trang hai.");

        try
        {
            var result = await ParsedMarkdownExporter.EnsureAsync(
                text,
                "L\u1ECBch s\u1EED \u0110\u1EA3ng.pdf",
                sourcePath,
                "legacy",
                null,
                settings,
                generatedAt);

            var expectedPath = Path.Combine(
                settings.OutputDirectory,
                documentGuid.ToString(),
                $"{documentGuid}.md");
            Assert.Equal(ParsedMarkdownExportStatus.FallbackExported, result.Status);
            Assert.Equal(expectedPath, result.Path);
            Assert.True(File.Exists(expectedPath));

            var bytes = await File.ReadAllBytesAsync(expectedPath);
            Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
            var markdown = Encoding.UTF8.GetString(bytes);
            Assert.StartsWith("# L\u1ECBch s\u1EED \u0110\u1EA3ng.pdf", markdown, StringComparison.Ordinal);
            Assert.Contains("- Extraction provider: `legacy`", markdown);
            Assert.Contains("- Generated at: `2026-06-06T10:30:00.0000000Z`", markdown);
            Assert.Contains($"- Source file: `{sourcePath}`", markdown);
            Assert.Contains("## Page 1", markdown);
            Assert.Contains("## Page 2", markdown);
            Assert.Contains("### Ch\u01B0\u01A1ng I M\u1EDF \u0111\u1EA7u", markdown);
            Assert.Contains("### II. B\u1ED1i c\u1EA3nh", markdown);
            Assert.Contains("N\u1ED9i dung trang hai.", markdown);
            Assert.Empty(Directory.GetFiles(
                Path.GetDirectoryName(expectedPath)!,
                "*.tmp",
                SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureAsync_KeepsValidDoclingMarkdown()
    {
        var root = CreateTemporaryDirectory();
        var doclingPath = Path.Combine(root, "docling.md");
        const string doclingMarkdown = "# Parsed document\n\nDocling content is valid.";
        await File.WriteAllTextAsync(doclingPath, doclingMarkdown);

        try
        {
            var result = await ParsedMarkdownExporter.EnsureAsync(
                "legacy text",
                "lesson.pdf",
                Path.Combine(root, $"{Guid.NewGuid()}.pdf"),
                "docling",
                doclingPath,
                new DocumentParsingSettings
                {
                    OutputDirectory = Path.Combine(root, "parsed"),
                    MinMarkdownLength = 10
                });

            Assert.Equal(ParsedMarkdownExportStatus.ExistingDoclingMarkdown, result.Status);
            Assert.Equal(doclingPath, result.Path);
            Assert.Equal(doclingMarkdown, await File.ReadAllTextAsync(doclingPath));
            Assert.False(Directory.Exists(Path.Combine(root, "parsed")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureAsync_ExportsFallbackWhenDoclingMarkdownIsTooShort()
    {
        var root = CreateTemporaryDirectory();
        var documentGuid = Guid.NewGuid();
        var doclingPath = Path.Combine(root, "short.md");
        await File.WriteAllTextAsync(doclingPath, "# short");

        try
        {
            var result = await ParsedMarkdownExporter.EnsureAsync(
                "[Page 1]\nLegacy extraction remains available.",
                "lesson.pdf",
                Path.Combine(root, $"{documentGuid}.pdf"),
                "legacy",
                doclingPath,
                new DocumentParsingSettings
                {
                    OutputDirectory = Path.Combine(root, "parsed"),
                    MinMarkdownLength = 50
                });

            Assert.Equal(ParsedMarkdownExportStatus.FallbackExported, result.Status);
            Assert.Equal(
                Path.Combine(root, "parsed", documentGuid.ToString(), $"{documentGuid}.md"),
                result.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureAsync_SkipsEmptyFinalText()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var result = await ParsedMarkdownExporter.EnsureAsync(
                "  ",
                "empty.pdf",
                Path.Combine(root, "not-a-guid.pdf"),
                "legacy",
                null,
                new DocumentParsingSettings
                {
                    OutputDirectory = Path.Combine(root, "parsed")
                });

            Assert.Equal(ParsedMarkdownExportStatus.SkippedEmptyText, result.Status);
            Assert.Null(result.Path);
            Assert.False(Directory.Exists(Path.Combine(root, "parsed")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pbl5-markdown-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
