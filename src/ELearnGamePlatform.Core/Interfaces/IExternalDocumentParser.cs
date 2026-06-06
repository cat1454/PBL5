using ELearnGamePlatform.Core.Models;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IExternalDocumentParser
{
    Task<ExternalDocumentParseResult> TryParseAsync(
        string filePath,
        string fileType,
        CancellationToken cancellationToken = default);
}
