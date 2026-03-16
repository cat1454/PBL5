using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IGameSessionRepository
{
    Task<GameSession> CreateAsync(GameSession session);
    Task<GameSession?> GetByIdAsync(int id);
    Task<IEnumerable<GameSession>> GetByUserIdAsync(string userId);
    Task<IEnumerable<GameSession>> GetByDocumentIdAsync(int documentId);
    Task<bool> UpdateAsync(int id, GameSession session);
    Task<bool> DeleteAsync(int id);
}
