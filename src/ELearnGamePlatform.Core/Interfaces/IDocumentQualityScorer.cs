using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IDocumentQualityScorer
{
    DocumentQualityScoreResult Score(DocumentQualityScoreInput input);
    double ScoreLegacyText(string? legacyExtractedText);
}
