using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;

namespace ELearnGamePlatform.API.Services;

public class WorkspaceService : IWorkspaceService
{
    public const string DefaultWorkspaceLabel = "My Workspace";

    private readonly IFolderProjectRepository _folderProjectRepository;
    private readonly IDocumentRepository _documentRepository;

    public WorkspaceService(
        IFolderProjectRepository folderProjectRepository,
        IDocumentRepository documentRepository)
    {
        _folderProjectRepository = folderProjectRepository;
        _documentRepository = documentRepository;
    }

    public string DefaultWorkspaceName => DefaultWorkspaceLabel;

    public async Task<FolderProject> EnsureDefaultWorkspaceAsync(string userId)
    {
        var existing = await _folderProjectRepository.GetByUserAndNameAsync(userId, DefaultWorkspaceLabel);
        if (existing != null)
        {
            return existing;
        }

        return await _folderProjectRepository.CreateAsync(new FolderProject
        {
            Name = DefaultWorkspaceLabel,
            Description = "Workspace mac dinh cho tai lieu upload truc tiep.",
            UploadedBy = userId.Trim(),
        });
    }

    public async Task<IReadOnlyCollection<Document>> AttachOrphanDocumentsAsync(string userId, int workspaceId)
    {
        var orphans = (await _documentRepository.GetByUserAsync(userId)).ToList();
        if (orphans.Count == 0)
        {
            return Array.Empty<Document>();
        }

        foreach (var document in orphans)
        {
            document.FolderProjectId = workspaceId;
            document.IncludeInFolderSlides = false;
            document.FolderSourceOrder = await _documentRepository.GetNextFolderSourceOrderAsync(workspaceId);
            document.UpdatedAt = DateTime.UtcNow;
            await _documentRepository.UpdateAsync(document.Id, document);
        }

        return orphans;
    }
}
