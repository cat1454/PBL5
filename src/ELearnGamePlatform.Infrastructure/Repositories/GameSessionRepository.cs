using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.Infrastructure.Repositories;

public class GameSessionRepository : IGameSessionRepository
{
    private readonly ApplicationDbContext _context;

    public GameSessionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GameSession> CreateAsync(GameSession session)
    {
        _context.GameSessions.Add(session);
        await _context.SaveChangesAsync();
        return session;
    }

    public async Task<GameSession?> GetByIdAsync(int id)
    {
        return await _context.GameSessions
            .Include(g => g.Document)
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    public async Task<IEnumerable<GameSession>> GetByUserIdAsync(string userId)
    {
        return await _context.GameSessions
            .Where(g => g.UserId == userId)
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<GameSession>> GetByDocumentIdAsync(int documentId)
    {
        return await _context.GameSessions
            .Where(g => g.DocumentId == documentId)
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> UpdateAsync(int id, GameSession session)
    {
        var existing = await _context.GameSessions.FindAsync(id);
        if (existing == null)
            return false;

        _context.Entry(existing).CurrentValues.SetValues(session);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var session = await _context.GameSessions.FindAsync(id);
        if (session == null)
            return false;

        _context.GameSessions.Remove(session);
        await _context.SaveChangesAsync();
        return true;
    }
}
