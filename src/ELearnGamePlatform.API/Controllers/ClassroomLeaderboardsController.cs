using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ELearnGamePlatform.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Authorize]
public sealed class ClassroomLeaderboardsController : AuthenticatedControllerBase
{
    private readonly IClassroomLeaderboardService _leaderboardService;

    public ClassroomLeaderboardsController(IClassroomLeaderboardService leaderboardService)
    {
        _leaderboardService = leaderboardService;
    }

    [HttpGet("api/classroom-assignments/{assignmentId:int}/leaderboard")]
    public async Task<IActionResult> GetAssignmentLeaderboard(int assignmentId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var response = await _leaderboardService.GetAssignmentLeaderboardAsync(
                assignmentId,
                CurrentUserId.Value,
                cancellationToken);

            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return ApiNotFound("classroom_assignment_not_found", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_leaderboard_view_forbidden", ex.Message);
        }
    }

    [HttpGet("api/classroom-workspaces/{classroomId:int}/leaderboard")]
    public async Task<IActionResult> GetClassroomLeaderboard(int classroomId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var response = await _leaderboardService.GetClassroomLeaderboardAsync(
                classroomId,
                CurrentUserId.Value,
                cancellationToken);

            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return ApiNotFound("classroom_workspace_not_found", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_leaderboard_view_forbidden", ex.Message);
        }
    }
}
