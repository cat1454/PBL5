using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.Infrastructure.Repositories;

public class FolderProjectRepository : IFolderProjectRepository
{
    private readonly ApplicationDbContext _context;

    public FolderProjectRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<FolderProject> CreateAsync(FolderProject folderProject)
    {
        _context.FolderProjects.Add(folderProject);
        await _context.SaveChangesAsync();
        return folderProject;
    }

    public async Task<FolderProject?> GetByIdAsync(int id)
    {
        return await _context.FolderProjects
            .Include(folder => folder.Documents)
            .Include(folder => folder.SlideDecks)
                .ThenInclude(deck => deck.Items)
            .FirstOrDefaultAsync(folder => folder.Id == id);
    }

    public async Task<IEnumerable<FolderProject>> GetByUserAsync(string userId)
    {
        return await _context.FolderProjects
            .Include(folder => folder.Documents)
            .Include(folder => folder.SlideDecks)
                .ThenInclude(deck => deck.Items)
            .Where(folder => folder.UploadedBy == userId)
            .OrderByDescending(folder => folder.UpdatedAt)
            .ToListAsync();
    }

    public async Task<FolderProject?> GetByUserAndNameAsync(string userId, string name)
    {
        return await _context.FolderProjects
            .Include(folder => folder.Documents)
            .Include(folder => folder.SlideDecks)
                .ThenInclude(deck => deck.Items)
            .FirstOrDefaultAsync(folder => folder.UploadedBy == userId && folder.Name == name);
    }

    public async Task<bool> UpdateAsync(FolderProject folderProject)
    {
        var existing = await _context.FolderProjects.FindAsync(folderProject.Id);
        if (existing == null)
        {
            return false;
        }

        folderProject.UpdatedAt = DateTime.UtcNow;
        _context.Entry(existing).CurrentValues.SetValues(folderProject);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var folderProject = await _context.FolderProjects.FindAsync(id);
        if (folderProject == null)
        {
            return false;
        }

        _context.FolderProjects.Remove(folderProject);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> TouchAsync(int id)
    {
        var folderProject = await _context.FolderProjects.FindAsync(id);
        if (folderProject == null)
        {
            return false;
        }

        folderProject.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }
}
