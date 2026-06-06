using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public class DoclingMarkdownParser : IDocumentMarkdownParser
{
    private readonly IDoclingCommandRunner _commandRunner;
    private readonly DocumentParsingSettings _options;
    private readonly ILogger<DoclingMarkdownParser> _logger;

    public DoclingMarkdownParser(
        IDoclingCommandRunner commandRunner,
        IOptions<DocumentParsingSettings> options,
        ILogger<DoclingMarkdownParser> logger)
    {
        _commandRunner = commandRunner;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> TryParseAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled
            || !string.Equals(_options.Provider, "docling", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var result = await _commandRunner.ConvertAsync(filePath, _options, cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Markdown))
        {
            _logger.LogWarning(
                "Docling conversion failed for {FilePath}; legacy extraction will be used. Reason={FailureReason}",
                filePath,
                result.FailureReason ?? "No Markdown output was produced.");
            return null;
        }

        var markdown = result.Markdown.Trim();
        if (markdown.Length < Math.Max(1, _options.MinMarkdownLength))
        {
            _logger.LogWarning(
                "Docling output for {FilePath} was too short ({CharacterCount} characters, minimum {MinimumCharacters}); legacy extraction will be used.",
                filePath,
                markdown.Length,
                _options.MinMarkdownLength);
            return null;
        }

        _logger.LogInformation(
            "Docling Markdown selected for {FilePath}: {CharacterCount} characters.",
            filePath,
            markdown.Length);
        return markdown;
    }
}
