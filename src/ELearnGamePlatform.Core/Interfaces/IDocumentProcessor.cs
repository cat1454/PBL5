namespace ELearnGamePlatform.Core.Interfaces;

public interface IDocumentProcessor
{
    Task<string> ExtractTextAsync(string filePath, string fileType);
    bool SupportedFileType(string fileType);
}
