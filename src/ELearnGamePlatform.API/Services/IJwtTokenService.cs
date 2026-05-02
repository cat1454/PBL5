using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.API.Services;

public interface IJwtTokenService
{
    string CreateToken(AppUser user);
}
