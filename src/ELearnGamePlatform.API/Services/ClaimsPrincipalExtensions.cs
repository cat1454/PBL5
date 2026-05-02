using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace ELearnGamePlatform.API.Services;

public static class ClaimsPrincipalExtensions
{
    public static int? GetCurrentUserId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue("sub");

        return int.TryParse(raw, out var userId) ? userId : null;
    }

    public static string? GetCurrentUserRole(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Role) ?? user.FindFirstValue("role");
}
