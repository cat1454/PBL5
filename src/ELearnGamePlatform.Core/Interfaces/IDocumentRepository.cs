using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IDocumentRepository
{
    Task<Document> CreateAsync(Document document);
    Task<Document?> GetByIdAsync(int id);
    Task<IEnumerable<Document>> GetAllAsync();
    Task<IEnumerable<Document>> GetByUserAsync(string userId);
    Task<bool> UpdateAsync(int id, Document document);
    Task<bool> DeleteAsync(int id);
}
