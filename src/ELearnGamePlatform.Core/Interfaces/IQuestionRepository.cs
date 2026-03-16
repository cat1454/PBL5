using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IQuestionRepository
{
    Task<Question> CreateAsync(Question question);
    Task<IEnumerable<Question>> ReplaceByDocumentIdAsync(int documentId, IEnumerable<Question> questions);
    Task<Question?> GetByIdAsync(int id);
    Task<IEnumerable<Question>> GetByDocumentIdAsync(int documentId);
    Task<IEnumerable<Question>> GetByDocumentIdAndTypeAsync(int documentId, QuestionType type);
    Task<bool> UpdateAsync(int id, Question question);
    Task<bool> DeleteAsync(int id);
    Task<bool> DeleteByDocumentIdAsync(int documentId);
}
