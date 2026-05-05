using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ELearnGamePlatform.API.Controllers;

public abstract class AuthenticatedControllerBase : ControllerBase
{
    protected int? CurrentUserId => User.GetCurrentUserId();

    protected string CurrentUserIdAsString => CurrentUserId?.ToString() ?? string.Empty;

    protected bool IsAdmin =>
        string.Equals(User.GetCurrentUserRole(), UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase)
        || string.Equals(User.GetCurrentUserRole(), UserRole.Admin.ToString().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase);

    protected IActionResult? EnsureCurrentUserMatches(string? userId)
    {
        if (IsAdmin)
        {
            return null;
        }

        if (CurrentUserId == null || !string.Equals(userId?.Trim(), CurrentUserIdAsString, StringComparison.Ordinal))
        {
            return Forbid();
        }

        return null;
    }

    protected IActionResult? EnsureOwnerOrAdmin(string? ownerUserId)
    {
        if (IsAdmin)
        {
            return null;
        }

        if (CurrentUserId == null || !string.Equals(ownerUserId?.Trim(), CurrentUserIdAsString, StringComparison.Ordinal))
        {
            return Forbid();
        }

        return null;
    }

    protected IActionResult ApiBadRequest(string code, string message)
        => BadRequest(ApiErrorResponse.Create(code, message));

    protected IActionResult ApiNotFound(string code, string message)
        => NotFound(ApiErrorResponse.Create(code, message));

    protected IActionResult ApiConflict(string code, string message)
        => Conflict(ApiErrorResponse.Create(code, message));

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
