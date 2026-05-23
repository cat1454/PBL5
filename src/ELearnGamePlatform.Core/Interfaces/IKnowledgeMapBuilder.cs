using ELearnGamePlatform.Core.Models;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IKnowledgeMapBuilder
{
    IReadOnlyList<DocumentRegion> BuildRegions(string? legacyExtractedText);
}
