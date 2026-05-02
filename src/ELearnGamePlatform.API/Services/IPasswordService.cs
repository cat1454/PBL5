using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.API.Services;

public interface IPasswordService
{
    string HashPassword(AppUser user, string password);
    bool VerifyPassword(AppUser user, string password);
}
