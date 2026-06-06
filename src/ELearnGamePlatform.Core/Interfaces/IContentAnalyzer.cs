using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Models;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IContentAnalyzer
{
    Task<ProcessedContent> AnalyzeContentAsync(string text, IProgress<DocumentProcessingProgressUpdate>? progress = null);
    Task<ProcessedContent> AnalyzeContentAsync(string text, DocumentUnderstandingResult? understandingResult, IProgress<DocumentProcessingProgressUpdate>? progress = null);
    Task<ProcessedContent> AnalyzeContentAsync(string text, DocumentUnderstandingResult? understandingResult, int? pageCount, IProgress<DocumentProcessingProgressUpdate>? progress = null);
    Task<ProcessedContent> AnalyzeContentAsync(string text, DocumentUnderstandingRun? understandingRun, IProgress<DocumentProcessingProgressUpdate>? progress = null);
    Task<string> SummarizeTextAsync(string text);
    Task<List<string>> ExtractKeyPointsAsync(string text);
}
