using ELearnGamePlatform.Core.Entities;
using Microsoft.AspNetCore.Identity;

namespace ELearnGamePlatform.API.Services;

public class PasswordService : IPasswordService
{
    private readonly PasswordHasher<AppUser> _passwordHasher = new();

    public string HashPassword(AppUser user, string password)
        => _passwordHasher.HashPassword(user, password);

    public bool VerifyPassword(AppUser user, string password)
        => _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
}
