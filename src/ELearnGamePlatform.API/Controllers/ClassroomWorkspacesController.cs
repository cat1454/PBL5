using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Authorize]
[Route("api/classroom-workspaces")]
public sealed class ClassroomWorkspacesController : AuthenticatedControllerBase
{
    private readonly IClassroomWorkspaceService _classroomWorkspaceService;
    private readonly IClassroomPermissionService _classroomPermissionService;

    public ClassroomWorkspacesController(
        IClassroomWorkspaceService classroomWorkspaceService,
        IClassroomPermissionService classroomPermissionService)
    {
        _classroomWorkspaceService = classroomWorkspaceService;
        _classroomPermissionService = classroomPermissionService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateClassroomWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ApiBadRequest("request_required", "Request body is required.");
        }

        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        if (!IsInstructorOrAdmin)
        {
            return ApiForbidden("classroom_teacher_required", "Only teachers can create classroom workspaces.");
        }

        try
        {
            var workspace = await _classroomWorkspaceService.CreateAsync(
                CurrentUserId.Value,
                new CreateClassroomWorkspaceInput(request.Name, request.Description),
                cancellationToken);

            return Ok(MapWorkspace(workspace, ClassroomRole.Teacher));
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_create_invalid", ex.Message);
        }
    }

    [HttpGet("teaching")]
    public async Task<IActionResult> GetTeaching(CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        var workspaces = await _classroomWorkspaceService.GetTeachingAsync(CurrentUserId.Value, cancellationToken);
        return Ok(workspaces.Select(workspace => MapWorkspace(workspace, ClassroomRole.Teacher)));
    }

    [HttpGet("joined")]
    public async Task<IActionResult> GetJoined(CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        var workspaces = await _classroomWorkspaceService.GetJoinedAsync(CurrentUserId.Value, cancellationToken);
        return Ok(workspaces.Select(workspace => MapWorkspace(workspace, ClassroomRole.Student)));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        var workspace = await _classroomWorkspaceService.GetVisibleByIdAsync(id, CurrentUserId.Value, cancellationToken);
        if (workspace == null)
        {
            return ApiNotFound("classroom_not_found", "Classroom workspace was not found or is not available to this user.");
        }

        var role = ResolveCurrentUserClassroomRole(workspace, CurrentUserId.Value);
        return Ok(MapWorkspace(workspace, role, includeJoinCodes: role == ClassroomRole.Teacher));
    }

    [HttpGet("{id:int}/members")]
    public async Task<IActionResult> GetMembers(int id, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        if (!await _classroomPermissionService.CanManageClassroomAsync(id, CurrentUserId.Value, cancellationToken))
        {
            return ApiForbidden("classroom_manage_required", "Only classroom teachers can view the member list.");
        }

        var members = await _classroomWorkspaceService.GetMembersAsync(id, cancellationToken);
        return Ok(members.Select(MapMember));
    }

    [HttpPost("{id:int}/join-codes")]
    public async Task<IActionResult> CreateJoinCode(
        int id,
        [FromBody] CreateClassroomJoinCodeRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new CreateClassroomJoinCodeRequest();

        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        if (!await _classroomPermissionService.CanManageClassroomAsync(id, CurrentUserId.Value, cancellationToken))
        {
            return ApiForbidden("classroom_manage_required", "Only classroom teachers can create join codes.");
        }

        try
        {
            var joinCode = await _classroomWorkspaceService.CreateJoinCodeAsync(
                id,
                CurrentUserId.Value,
                new CreateClassroomJoinCodeInput(request.ExpiresAt, request.MaxUses),
                cancellationToken);

            return Ok(MapJoinCode(joinCode));
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_join_code_invalid", ex.Message);
        }
    }

    [HttpPatch("{id:int}/join-codes/{codeId:int}/disable")]
    public async Task<IActionResult> DisableJoinCode(int id, int codeId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        if (!await _classroomPermissionService.CanManageClassroomAsync(id, CurrentUserId.Value, cancellationToken))
        {
            return ApiForbidden("classroom_manage_required", "Only classroom teachers can disable join codes.");
        }

        var joinCode = await _classroomWorkspaceService.DisableJoinCodeAsync(id, codeId, cancellationToken);
        if (joinCode == null)
        {
            return ApiNotFound("classroom_join_code_not_found", "Join code was not found.");
        }

        return Ok(MapJoinCode(joinCode));
    }

    [HttpPost("join")]
    public async Task<IActionResult> Join(
        [FromBody] JoinClassroomWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ApiBadRequest("request_required", "Request body is required.");
        }

        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var workspace = await _classroomWorkspaceService.JoinByCodeAsync(
                CurrentUserId.Value,
                request.Code,
                cancellationToken);

            return Ok(MapWorkspace(workspace, ResolveCurrentUserClassroomRole(workspace, CurrentUserId.Value)));
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_join_invalid", ex.Message);
        }
    }

    private bool IsInstructorOrAdmin =>
        string.Equals(CurrentUserRole, UserRole.Instructor.ToString(), StringComparison.OrdinalIgnoreCase)
        || IsAdmin;

    private static ClassroomRole ResolveCurrentUserClassroomRole(ClassroomWorkspace workspace, int userId)
    {
        if (workspace.OwnerUserId == userId
            || workspace.Members.Any(member =>
                member.UserId == userId
                && member.Role == ClassroomRole.Teacher
                && member.Status == ClassroomMemberStatus.Active))
        {
            return ClassroomRole.Teacher;
        }

        return ClassroomRole.Student;
    }

    private static object MapWorkspace(
        ClassroomWorkspace workspace,
        ClassroomRole currentUserRole,
        bool includeJoinCodes = false)
    {
        var activeMembers = workspace.Members
            .Where(member => member.Status == ClassroomMemberStatus.Active)
            .ToList();

        return new
        {
            id = workspace.Id,
            name = workspace.Name,
            description = workspace.Description,
            ownerUserId = workspace.OwnerUserId,
            isArchived = workspace.IsArchived,
            createdAt = workspace.CreatedAt,
            updatedAt = workspace.UpdatedAt,
            currentUserRole = currentUserRole.ToString(),
            memberCount = activeMembers.Count,
            teacherCount = activeMembers.Count(member => member.Role == ClassroomRole.Teacher),
            studentCount = activeMembers.Count(member => member.Role == ClassroomRole.Student),
            joinCodes = includeJoinCodes ? workspace.JoinCodes.OrderByDescending(code => code.CreatedAt).Select(MapJoinCode) : null
        };
    }

    private static object MapMember(ClassroomMember member)
    {
        return new
        {
            id = member.Id,
            classroomWorkspaceId = member.ClassroomWorkspaceId,
            userId = member.UserId,
            role = member.Role.ToString(),
            status = member.Status.ToString(),
            joinedAt = member.JoinedAt,
            updatedAt = member.UpdatedAt,
            user = member.User == null
                ? null
                : new
                {
                    id = member.User.Id,
                    fullName = member.User.FullName,
                    email = member.User.Email,
                    role = member.User.Role.ToString().ToUpperInvariant(),
                    isActive = member.User.IsActive
                }
        };
    }

    private static object MapJoinCode(ClassroomJoinCode joinCode)
    {
        return new
        {
            id = joinCode.Id,
            classroomWorkspaceId = joinCode.ClassroomWorkspaceId,
            code = joinCode.Code,
            expiresAt = joinCode.ExpiresAt,
            maxUses = joinCode.MaxUses,
            usedCount = joinCode.UsedCount,
            isActive = joinCode.IsActive,
            createdByUserId = joinCode.CreatedByUserId,
            createdAt = joinCode.CreatedAt,
            updatedAt = joinCode.UpdatedAt
        };
    }
}

public sealed class CreateClassroomWorkspaceRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class CreateClassroomJoinCodeRequest
{
    public DateTime? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
}

public sealed class JoinClassroomWorkspaceRequest
{
    public string Code { get; set; } = string.Empty;
}
