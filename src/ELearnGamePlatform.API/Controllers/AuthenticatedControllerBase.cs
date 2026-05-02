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
}
