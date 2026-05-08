using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IDocumentInputQualityReportProvider
{
    DocumentInputQualityReport? LastInputQualityReport { get; }
}
