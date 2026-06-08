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
public sealed class ClassroomAnalyticsController : AuthenticatedControllerBase
{
    private readonly IClassroomAnalyticsService _analyticsService;

    public ClassroomAnalyticsController(IClassroomAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    /// <summary>
    /// Teacher analytics for a classroom workspace.
    /// Only the classroom owner or a teacher-role member can access.
    /// </summary>
    [HttpGet("api/classroom-workspaces/{classroomId:int}/analytics")]
    public async Task<IActionResult> GetTeacherAnalytics(int classroomId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var response = await _analyticsService.GetTeacherAnalyticsAsync(
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
            return ApiForbidden("classroom_analytics_teacher_forbidden", ex.Message);
        }
    }

    /// <summary>
    /// Student personal analytics for a classroom.
    /// Only active student members can access their own analytics.
    /// </summary>
    [HttpGet("api/classroom-workspaces/{classroomId:int}/student/analytics")]
    public async Task<IActionResult> GetStudentAnalytics(int classroomId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var response = await _analyticsService.GetStudentAnalyticsAsync(
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
            return ApiForbidden("classroom_analytics_student_forbidden", ex.Message);
        }
    }
}
