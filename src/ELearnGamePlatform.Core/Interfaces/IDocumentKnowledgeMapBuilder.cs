using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Models;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IDocumentKnowledgeMapBuilder
{
    KnowledgeMapBuildResult Build(DocumentUnderstandingResult? result);
    KnowledgeMapBuildResult Build(DocumentUnderstandingRun? run);
}
