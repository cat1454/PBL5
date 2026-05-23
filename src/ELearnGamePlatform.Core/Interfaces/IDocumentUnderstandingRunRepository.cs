using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IDocumentUnderstandingRunRepository
{
    Task<DocumentUnderstandingRun> CreateAsync(DocumentUnderstandingRun run);
    Task<DocumentUnderstandingRun?> GetLatestByDocumentIdAsync(int documentId);
}
