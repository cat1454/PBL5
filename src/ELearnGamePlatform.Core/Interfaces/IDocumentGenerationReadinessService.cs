using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IDocumentGenerationReadinessService
{
    Task<DocumentGenerationReadiness> GetReadinessAsync(Document document, bool confirmed = false);
    DocumentGenerationReadiness GetAggregateReadiness(IEnumerable<DocumentGenerationReadiness> readinessResults, bool confirmed = false);
}
