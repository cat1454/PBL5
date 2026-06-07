using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.API.Services;

public interface IClassroomWorkspaceService
{
    Task<ClassroomWorkspace> CreateAsync(
        int ownerUserId,
        CreateClassroomWorkspaceInput input,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClassroomWorkspace>> GetTeachingAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClassroomWorkspace>> GetJoinedAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task<ClassroomWorkspace?> GetVisibleByIdAsync(
        int classroomWorkspaceId,
        int userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClassroomMember>> GetMembersAsync(
        int classroomWorkspaceId,
        CancellationToken cancellationToken = default);

    Task<ClassroomJoinCode> CreateJoinCodeAsync(
        int classroomWorkspaceId,
        int createdByUserId,
        CreateClassroomJoinCodeInput input,
        CancellationToken cancellationToken = default);

    Task<ClassroomJoinCode?> DisableJoinCodeAsync(
        int classroomWorkspaceId,
        int codeId,
        CancellationToken cancellationToken = default);

    Task<ClassroomWorkspace> JoinByCodeAsync(
        int userId,
        string code,
        CancellationToken cancellationToken = default);
}

public sealed record CreateClassroomWorkspaceInput(string Name, string? Description);

public sealed record CreateClassroomJoinCodeInput(DateTime? ExpiresAt, int? MaxUses);
