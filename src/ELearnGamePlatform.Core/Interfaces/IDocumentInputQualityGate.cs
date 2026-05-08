using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IDocumentInputQualityGate
{
    DocumentInputQualityResult Evaluate(string? extractedText);
}
