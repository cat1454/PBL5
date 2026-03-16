using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IContentAnalyzer
{
    Task<ProcessedContent> AnalyzeContentAsync(string text);
    Task<string> SummarizeTextAsync(string text);
    Task<List<string>> ExtractKeyPointsAsync(string text);
}
