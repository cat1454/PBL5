using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.API.Services;

public sealed class ClassroomPermissionService : IClassroomPermissionService
{
    private readonly ApplicationDbContext _dbContext;

    public ClassroomPermissionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> CanManageClassroomAsync(
        int classroomWorkspaceId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ClassroomWorkspaces.AnyAsync(
                workspace => workspace.Id == classroomWorkspaceId && workspace.OwnerUserId == userId && !workspace.IsArchived,
                cancellationToken)
            || await IsTeacherAsync(classroomWorkspaceId, userId, cancellationToken);
    }

    public async Task<bool> CanViewClassroomAsync(
        int classroomWorkspaceId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        return await CanManageClassroomAsync(classroomWorkspaceId, userId, cancellationToken)
            || await IsStudentAsync(classroomWorkspaceId, userId, cancellationToken);
    }

    public async Task<bool> IsTeacherAsync(
        int classroomWorkspaceId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ClassroomMembers.AnyAsync(
            member =>
                member.ClassroomWorkspaceId == classroomWorkspaceId
                && member.UserId == userId
                && member.Role == ClassroomRole.Teacher
                && member.Status == ClassroomMemberStatus.Active,
            cancellationToken);
    }

    public async Task<bool> IsStudentAsync(
        int classroomWorkspaceId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ClassroomMembers.AnyAsync(
            member =>
                member.ClassroomWorkspaceId == classroomWorkspaceId
                && member.UserId == userId
                && member.Role == ClassroomRole.Student
                && member.Status == ClassroomMemberStatus.Active,
            cancellationToken);
    }
}
