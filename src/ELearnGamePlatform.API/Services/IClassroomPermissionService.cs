namespace ELearnGamePlatform.API.Services;

public interface IClassroomPermissionService
{
    Task<bool> CanManageClassroomAsync(int classroomWorkspaceId, int userId, CancellationToken cancellationToken = default);
    Task<bool> CanViewClassroomAsync(int classroomWorkspaceId, int userId, CancellationToken cancellationToken = default);
    Task<bool> IsTeacherAsync(int classroomWorkspaceId, int userId, CancellationToken cancellationToken = default);
    Task<bool> IsStudentAsync(int classroomWorkspaceId, int userId, CancellationToken cancellationToken = default);
}
