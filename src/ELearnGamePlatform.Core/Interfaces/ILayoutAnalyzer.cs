using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Models;

namespace ELearnGamePlatform.Core.Interfaces;

public interface ILayoutAnalyzer
{
    IReadOnlyList<PageUnderstandingResult> Analyze(
        string filePath,
        string? legacyExtractedText,
        DocumentInputQualityReport? pageQualityReport = null);
}
