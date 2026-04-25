using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.API.Services;

public interface IWorkspaceService
{
    string DefaultWorkspaceName { get; }
    Task<FolderProject> EnsureDefaultWorkspaceAsync(string userId);
    Task<IReadOnlyCollection<Document>> AttachOrphanDocumentsAsync(string userId, int workspaceId);
}
