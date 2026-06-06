using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using ELearnGamePlatform.Core.Options;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Services.DocumentProcessing;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class DocumentExtractionSelectorTests
{
    [Fact]
    public async Task SelectAsync_WhenDisabled_DoesNotCallParserAndUsesLegacyText()
    {
        var parser = new StubExternalParser(() => throw new InvalidOperationException("Parser must not be called."));

        var selection = await DocumentExtractionSelector.SelectAsync(
            "legacy extracted text",
            "lesson.pdf",
            "pdf",
            new DocumentParsingSettings { Enabled = false },
            parser);

        Assert.Equal(0, parser.CallCount);
        Assert.Equal("legacy extracted text", selection.Text);
        Assert.Equal("legacy", selection.Provider);
        Assert.Null(selection.ExternalParsingSucceeded);
    }

    [Theory]
    [InlineData(true, false, "Docling command could not be started.")]
    [InlineData(false, true, "Docling timed out.")]
    public async Task SelectAsync_WhenFallbackEnabledAndExternalParsingFails_UsesLegacyText(
        bool commandMissing,
        bool timedOut,
        string error)
    {
        var parser = new StubExternalParser(() => new ExternalDocumentParseResult
        {
            Success = false,
            CommandMissing = commandMissing,
            TimedOut = timedOut,
            Error = error,
            ElapsedMs = 25
        });

        var selection = await DocumentExtractionSelector.SelectAsync(
            "legacy extracted text",
            "lesson.pdf",
            "pdf",
            EnabledSettings(fallbackToLegacy: true),
            parser);

        Assert.Equal(1, parser.CallCount);
        Assert.Equal("legacy extracted text", selection.Text);
        Assert.Equal("legacy", selection.Provider);
        Assert.False(selection.ExternalParsingSucceeded);
        Assert.Equal(25, selection.ExternalParsingElapsedMs);
        Assert.Equal(error, selection.ExternalParsingError);
    }

    [Fact]
    public async Task SelectAsync_WhenFallbackDisabledAndExternalParsingFails_ThrowsClearError()
    {
        var parser = new StubExternalParser(() => new ExternalDocumentParseResult
        {
            Success = false,
            CommandMissing = true,
            Error = "Docling command could not be started."
        });

        var exception = await Assert.ThrowsAsync<DocumentParsingException>(() =>
            DocumentExtractionSelector.SelectAsync(
                "legacy extracted text",
                "lesson.pdf",
                "pdf",
                EnabledSettings(fallbackToLegacy: false),
                parser));

        Assert.Contains("legacy fallback is disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("could not be started", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectAsync_WhenMarkdownPreferredAndDoclingSucceeds_UsesMarkdown()
    {
        const string markdown = "# Lesson\n\n| Name | Value |\n| --- | --- |\n| Alpha | 10 |";
        var parser = new StubExternalParser(() => new ExternalDocumentParseResult
        {
            Success = true,
            Provider = "docling",
            Markdown = markdown,
            PlainText = "Lesson Name Value Alpha 10",
            ElapsedMs = 42
        });

        var selection = await DocumentExtractionSelector.SelectAsync(
            "legacy extracted text",
            "lesson.pdf",
            "pdf",
            EnabledSettings(preferMarkdown: true),
            parser);

        Assert.Equal(markdown, selection.Text);
        Assert.Equal("docling", selection.Provider);
        Assert.True(selection.ExternalParsingSucceeded);
        Assert.Equal(42, selection.ExternalParsingElapsedMs);
        Assert.Null(selection.ExternalParsingError);

        var document = new Document
        {
            FileName = "lesson.pdf",
            FileType = "pdf",
            FilePath = "lesson.pdf",
            UploadedBy = "test-user",
            ExtractedText = selection.Text
        };
        document.SetProcessingMetadata(new DocumentProcessingMetadata
        {
            ExtractionProvider = selection.Provider,
            ExternalParsingSucceeded = selection.ExternalParsingSucceeded,
            ExternalParsingElapsedMs = selection.ExternalParsingElapsedMs,
            ExternalParsingError = selection.ExternalParsingError
        });

        Assert.Equal(markdown, document.ExtractedText);
        Assert.Equal("docling", document.GetProcessingMetadata().ExtractionProvider);
    }

    [Fact]
    public async Task SelectAsync_WhenMarkdownNotPreferredAndDoclingSucceeds_KeepsLegacyText()
    {
        const string markdown = "# Parsed lesson\n\nDocling output.";
        var parser = new StubExternalParser(() => new ExternalDocumentParseResult
        {
            Success = true,
            Provider = "docling",
            Markdown = markdown,
            OutputPath = "parsed/result.md",
            ElapsedMs = 42
        });

        var selection = await DocumentExtractionSelector.SelectAsync(
            "legacy extracted text",
            "lesson.pdf",
            "pdf",
            EnabledSettings(preferMarkdown: false),
            parser);

        Assert.Equal(1, parser.CallCount);
        Assert.Equal("legacy extracted text", selection.Text);
        Assert.Equal("legacy", selection.Provider);
        Assert.True(selection.ExternalParsingSucceeded);
        Assert.Equal(42, selection.ExternalParsingElapsedMs);
        Assert.Null(selection.ExternalParsingError);
    }

    [Fact]
    public async Task SelectAsync_WhenFallbackEnabledAndParserThrows_UsesLegacyText()
    {
        var parser = new StubExternalParser(() => throw new InvalidOperationException("parser failure"));

        var selection = await DocumentExtractionSelector.SelectAsync(
            "legacy extracted text",
            "lesson.pdf",
            "pdf",
            EnabledSettings(fallbackToLegacy: true),
            parser);

        Assert.Equal("legacy extracted text", selection.Text);
        Assert.Equal("legacy", selection.Provider);
        Assert.False(selection.ExternalParsingSucceeded);
        Assert.Equal("parser failure", selection.ExternalParsingError);
    }

    [Fact]
    public async Task SelectAsync_WhenMarkdownPreferredAndMojibakeRepairSucceeds_UsesRepairedMarkdown()
    {
        const string mojibake =
            "# H\u00E1\u00BB\u2021 th\u00E1\u00BB\u2018ng\n\n" +
            "D\u00C3\u00B9ng Docling \u00C4\u2018\u00E1\u00BB\u0192 sinh c\u00C3\u00A2u h\u00E1\u00BB\u008Fi.";
        var parser = new StubExternalParser(() => new ExternalDocumentParseResult
        {
            Success = true,
            Markdown = mojibake,
            ElapsedMs = 18
        });

        var selection = await DocumentExtractionSelector.SelectAsync(
            "legacy extracted text",
            "lesson.pdf",
            "pdf",
            EnabledSettings(fallbackToLegacy: true),
            parser);

        Assert.Equal("# H\u1EC7 th\u1ED1ng\n\nD\u00F9ng Docling \u0111\u1EC3 sinh c\u00E2u h\u1ECFi.", selection.Text);
        Assert.Equal("docling-repaired", selection.Provider);
        Assert.True(selection.ExternalParsingSucceeded);
        Assert.Equal(18, selection.ExternalParsingElapsedMs);
        Assert.Null(selection.ExternalParsingError);
    }

    [Fact]
    public async Task SelectAsync_WhenFallbackEnabledAndMojibakeRepairFails_UsesLegacyAndMarksError()
    {
        var parser = new StubExternalParser(() => new ExternalDocumentParseResult
        {
            Success = true,
            Markdown = "# Lesson\n\n\u00C3 \u00C3 \u00C3"
        });

        var selection = await DocumentExtractionSelector.SelectAsync(
            "legacy extracted text",
            "lesson.pdf",
            "pdf",
            EnabledSettings(fallbackToLegacy: true),
            parser);

        Assert.Equal("legacy extracted text", selection.Text);
        Assert.Equal("legacy", selection.Provider);
        Assert.False(selection.ExternalParsingSucceeded);
        Assert.Equal(
            "Docling output rejected because mojibake repair failed.",
            selection.ExternalParsingError);
    }

    [Fact]
    public async Task SelectAsync_WhenFallbackDisabledAndMojibakeRepairFails_Throws()
    {
        var parser = new StubExternalParser(() => new ExternalDocumentParseResult
        {
            Success = true,
            Markdown = "# Lesson\n\n\u00C3 \u00C3 \u00C3"
        });

        var exception = await Assert.ThrowsAsync<DocumentParsingException>(() =>
            DocumentExtractionSelector.SelectAsync(
                "legacy extracted text",
                "lesson.pdf",
                "pdf",
                EnabledSettings(fallbackToLegacy: false),
                parser));

        Assert.Contains("mojibake repair failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fallback is disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DocumentParsingSettings EnabledSettings(
        bool fallbackToLegacy = true,
        bool preferMarkdown = true)
        => new()
        {
            Enabled = true,
            MinMarkdownLength = 1,
            FallbackToLegacy = fallbackToLegacy,
            PreferMarkdownForGeneration = preferMarkdown
        };

    private sealed class StubExternalParser : IExternalDocumentParser
    {
        private readonly Func<ExternalDocumentParseResult> _resultFactory;

        public StubExternalParser(Func<ExternalDocumentParseResult> resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public int CallCount { get; private set; }

        public Task<ExternalDocumentParseResult> TryParseAsync(
            string filePath,
            string fileType,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_resultFactory());
        }
    }
}
