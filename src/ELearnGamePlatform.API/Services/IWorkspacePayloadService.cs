using ELearnGamePlatform.API.Contracts;
using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.API.Services;

public interface IWorkspacePayloadService
{
    WorkspacePayload BuildWorkspacePayload(FolderProject workspace);
    Task<SourcePayload> BuildSourcePayloadAsync(Document source, int questionsCount);
    Task<IReadOnlyList<SourcePayload>> BuildSourcePayloadsAsync(IEnumerable<Document> sources);
    Task<DashboardHomePayload> BuildDashboardHomePayloadAsync(FolderProject workspace, IEnumerable<Document> sources);
}
