using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.Infrastructure.Repositories;

public class DocumentRepository : IDocumentRepository
{
    private readonly ApplicationDbContext _context;

    public DocumentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Document> CreateAsync(Document document)
    {
        _context.Documents.Add(document);
        await _context.SaveChangesAsync();
        return document;
    }

    public async Task<Document?> GetByIdAsync(int id)
    {
        return await _context.Documents
            .Include(d => d.Questions)
            .Include(d => d.GameSessions)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<IEnumerable<Document>> GetAllAsync()
    {
        return await _context.Documents
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Document>> GetByUserAsync(string userId)
    {
        return await _context.Documents
            .Include(d => d.Questions)
            .Where(d => d.UploadedBy == userId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> UpdateAsync(int id, Document document)
    {
        var existing = await _context.Documents.FindAsync(id);
        if (existing == null)
            return false;

        document.UpdatedAt = DateTime.UtcNow;
        _context.Entry(existing).CurrentValues.SetValues(document);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var document = await _context.Documents.FindAsync(id);
        if (document == null)
            return false;

        _context.Documents.Remove(document);
        await _context.SaveChangesAsync();
        return true;
    }
}
