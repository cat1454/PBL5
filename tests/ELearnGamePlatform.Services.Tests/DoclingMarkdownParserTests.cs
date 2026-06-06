using ELearnGamePlatform.Core.Options;
using ELearnGamePlatform.Services.DocumentProcessing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class DoclingMarkdownParserTests
{
    [Fact]
    public async Task TryParseAsync_ReturnsMarkdown_WhenDoclingSucceeds()
    {
        var markdown = "# Chapter 1\n\n" + new string('x', 240);
        var parser = CreateParser(
            new DoclingCommandResult(true, markdown, null),
            new DocumentParsingSettings { Enabled = true, MinMarkdownLength = 100 });

        var result = await parser.TryParseAsync("lesson.pdf");

        Assert.Equal(markdown, result);
    }

    [Fact]
    public async Task TryParseAsync_DoesNotRunCommand_WhenDisabled()
    {
        var runner = new StubDoclingCommandRunner(
            new DoclingCommandResult(true, "# Unexpected\n\n" + new string('x', 240), null));
        var parser = CreateParser(
            runner,
            new DocumentParsingSettings { Enabled = false, MinMarkdownLength = 100 });

        var result = await parser.TryParseAsync("lesson.pdf");

        Assert.Null(result);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task TryParseAsync_ReturnsNull_WhenOutputIsTooShort()
    {
        var parser = CreateParser(
            new DoclingCommandResult(true, "# Short", null),
            new DocumentParsingSettings { Enabled = true, MinMarkdownLength = 100 });

        var result = await parser.TryParseAsync("lesson.pdf");

        Assert.Null(result);
    }

    [Fact]
    public async Task TryParseAsync_ReturnsNull_WhenCommandFails()
    {
        var parser = CreateParser(
            new DoclingCommandResult(false, null, "docling command was not found"),
            new DocumentParsingSettings { Enabled = true, MinMarkdownLength = 100 });

        var result = await parser.TryParseAsync("lesson.pdf");

        Assert.Null(result);
    }

    [Fact]
    public async Task CommandRunner_ReturnsFailure_WhenCommandIsMissing()
    {
        var runner = new DoclingCommandRunner(NullLogger<DoclingCommandRunner>.Instance);
        var inputPath = Path.GetTempFileName();

        try
        {
            var result = await runner.ConvertAsync(
                inputPath,
                new DocumentParsingSettings
                {
                    DoclingCommand = $"missing-docling-{Guid.NewGuid():N}",
                    TimeoutSeconds = 1
                });

            Assert.False(result.Success);
            Assert.Contains("could not be started", result.FailureReason);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    private static DoclingMarkdownParser CreateParser(
        DoclingCommandResult result,
        DocumentParsingSettings options)
        => CreateParser(new StubDoclingCommandRunner(result), options);

    private static DoclingMarkdownParser CreateParser(
        IDoclingCommandRunner runner,
        DocumentParsingSettings options)
        => new(
            runner,
            Options.Create(options),
            NullLogger<DoclingMarkdownParser>.Instance);

    private sealed class StubDoclingCommandRunner : IDoclingCommandRunner
    {
        private readonly DoclingCommandResult _result;

        public StubDoclingCommandRunner(DoclingCommandResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<DoclingCommandResult> ConvertAsync(
            string filePath,
            DocumentParsingSettings options,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }
}
