namespace ELearnGamePlatform.Core.Interfaces;

public interface IDocumentMarkdownParser
{
    Task<string?> TryParseAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
