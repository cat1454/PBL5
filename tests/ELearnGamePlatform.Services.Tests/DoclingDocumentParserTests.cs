using ELearnGamePlatform.Core.Options;
using ELearnGamePlatform.Services.DocumentProcessing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class DoclingDocumentParserTests
{
    [Fact]
    public async Task TryParseAsync_ReturnsFailureWithoutStartingCommand_WhenDisabled()
    {
        var parser = CreateParser(new DocumentParsingSettings { Enabled = false });

        var result = await parser.TryParseAsync("lesson.pdf", "pdf");

        Assert.False(result.Success);
        Assert.Equal("External document parsing is disabled.", result.Error);
        Assert.False(result.CommandMissing);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task TryParseAsync_ReturnsCommandMissing_WhenExecutableDoesNotExist()
    {
        var inputPath = Path.GetTempFileName();
        var outputRoot = Path.Combine(Path.GetTempPath(), $"pbl5-docling-test-{Guid.NewGuid():N}");

        try
        {
            var parser = CreateParser(new DocumentParsingSettings
            {
                Enabled = true,
                DoclingCommand = $"missing-docling-{Guid.NewGuid():N}",
                OutputDirectory = outputRoot
            });

            var result = await parser.TryParseAsync(inputPath, "pdf");

            Assert.False(result.Success);
            Assert.True(result.CommandMissing);
            Assert.Contains("could not be started", result.Error);
            Assert.True(Directory.Exists(outputRoot));
            Assert.Single(Directory.GetDirectories(outputRoot));
        }
        finally
        {
            File.Delete(inputPath);
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TryParseAsync_ReturnsFailure_WhenInputFileDoesNotExist()
    {
        var parser = CreateParser(new DocumentParsingSettings { Enabled = true });

        var result = await parser.TryParseAsync(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pdf"),
            "pdf");

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.Error);
    }

    private static DoclingDocumentParser CreateParser(DocumentParsingSettings settings)
        => new(
            Options.Create(settings),
            NullLogger<DoclingDocumentParser>.Instance);
}
