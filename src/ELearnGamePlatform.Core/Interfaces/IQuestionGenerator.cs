using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IQuestionGenerator
{
    Task<List<Question>> GenerateQuestionsAsync(int documentId, string content, int count = 10, IProgress<QuestionGenerationProgressUpdate>? progress = null);
    Task<List<Question>> GenerateQuestionsByTypeAsync(int documentId, string content, QuestionType type, int count = 10, IProgress<QuestionGenerationProgressUpdate>? progress = null);
}
