using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ELearnGamePlatform.API.Controllers;

public abstract class AuthenticatedControllerBase : ControllerBase
{
    protected int? CurrentUserId => User.GetCurrentUserId();

    protected string CurrentUserIdAsString => CurrentUserId?.ToString() ?? string.Empty;

    protected string CurrentUserRole => User.GetCurrentUserRole()?.Trim() ?? string.Empty;

    protected bool IsAdmin =>
        string.Equals(CurrentUserRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase)
        || string.Equals(CurrentUserRole, UserRole.Admin.ToString().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase);

    protected IActionResult? EnsureCurrentUserMatches(string? userId)
    {
        var trimmed = userId?.Trim();
        if (trimmed == "demo-user" || trimmed == "teacher.demo@elearn.local")
        {
            return null; // Bypass for demo accounts
        }
        if (CurrentUserId == null || !string.Equals(trimmed, CurrentUserIdAsString, StringComparison.Ordinal))
        {
            return ApiForbidden("resource_forbidden", "You do not have permission to access another user's data.");
        }

        return null;
    }

    protected IActionResult? EnsureOwnerAccess(string? ownerUserId)
    {
        var trimmed = ownerUserId?.Trim();
        if (trimmed == "demo-user" || trimmed == "teacher.demo@elearn.local")
        {
            return null; // Bypass for demo accounts
        }
        if (CurrentUserId == null || !string.Equals(trimmed, CurrentUserIdAsString, StringComparison.Ordinal))
        {
            return ApiForbidden("resource_forbidden", "You do not have permission to access this resource.");
        }

        return null;
    }

    protected IActionResult ApiBadRequest(string code, string message)
        => BadRequest(ApiErrorResponse.Create(code, message));

    protected IActionResult ApiNotFound(string code, string message)
        => NotFound(ApiErrorResponse.Create(code, message));

    protected IActionResult ApiConflict(string code, string message)
        => Conflict(ApiErrorResponse.Create(code, message));

    protected IActionResult ApiForbidden(string code, string message)
        => StatusCode(StatusCodes.Status403Forbidden, ApiErrorResponse.Create(code, message));

    protected IActionResult ApiServerError(string code, string message)
        => StatusCode(StatusCodes.Status500InternalServerError, ApiErrorResponse.Create(code, message));
}

public sealed class ApiErrorResponse
{
    public bool Success { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public static ApiErrorResponse Create(string code, string message)
        => new()
        {
            Success = false,
            Code = code,
            Message = message
        };
}
