using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public class LegacyKnowledgeMapBuilder : IKnowledgeMapBuilder
{
    public IReadOnlyList<DocumentRegion> BuildRegions(string? legacyExtractedText)
    {
        var text = legacyExtractedText?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<DocumentRegion>();
        }

        return new[]
        {
            new DocumentRegion
            {
                PageNumber = 1,
                RegionType = "legacy-text",
                Text = text
            }
        };
    }
}
