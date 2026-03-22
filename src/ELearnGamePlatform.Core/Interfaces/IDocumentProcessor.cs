using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IDocumentProcessor
{
    Task<string> ExtractTextAsync(string filePath, string fileType, IProgress<DocumentProcessingProgressUpdate>? progress = null);
    bool SupportedFileType(string fileType);
}
