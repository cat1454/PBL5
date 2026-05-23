using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Models;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IDocumentUnderstandingOrchestrator
{
    Task<DocumentUnderstandingResult> UnderstandAsync(
        int documentId,
        string filePath,
        string? legacyExtractedText,
        DocumentInputQualityReport? pageQualityReport = null,
        CancellationToken cancellationToken = default);
}
