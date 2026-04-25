using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface IFolderProjectRepository
{
    Task<FolderProject> CreateAsync(FolderProject folderProject);
    Task<FolderProject?> GetByIdAsync(int id);
    Task<IEnumerable<FolderProject>> GetByUserAsync(string userId);
    Task<FolderProject?> GetByUserAndNameAsync(string userId, string name);
    Task<bool> UpdateAsync(FolderProject folderProject);
    Task<bool> DeleteAsync(int id);
    Task<bool> TouchAsync(int id);
}
