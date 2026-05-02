using System.ComponentModel.DataAnnotations;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : AuthenticatedControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordService _passwordService;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthController(
        ApplicationDbContext dbContext,
        IPasswordService passwordService,
        IJwtTokenService jwtTokenService)
    {
        _dbContext = dbContext;
        _passwordService = passwordService;
        _jwtTokenService = jwtTokenService;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var validationError = ValidateRegisterRequest(request);
        if (validationError != null)
        {
            return BadRequest(new { message = validationError });
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        var parsedRole = ParseRegisterRole(request.Role);

        if (parsedRole == null || parsedRole == UserRole.Admin)
        {
            return BadRequest(new { message = "Role must be LEARNER or INSTRUCTOR." });
        }

        var emailExists = await _dbContext.AppUsers.AnyAsync(user => user.Email == normalizedEmail);
        if (emailExists)
        {
            return Conflict(new { message = "Email is already registered." });
        }

        var user = new AppUser
        {
            FullName = request.FullName.Trim(),
            Email = normalizedEmail,
            PasswordHash = string.Empty,
            Role = parsedRole.Value,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordService.HashPassword(user, request.Password);

        _dbContext.AppUsers.Add(user);
        await _dbContext.SaveChangesAsync();

        return Ok(BuildAuthResponse(user));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Email and password are required." });
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await _dbContext.AppUsers.FirstOrDefaultAsync(candidate => candidate.Email == normalizedEmail);
        if (user == null || !user.IsActive || !_passwordService.VerifyPassword(user, request.Password))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        return Ok(BuildAuthResponse(user));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(new { message = "Invalid token." });
        }

        var user = await _dbContext.AppUsers.FirstOrDefaultAsync(candidate => candidate.Id == CurrentUserId.Value);
        if (user == null || !user.IsActive)
        {
            return Unauthorized(new { message = "User is not available." });
        }

        return Ok(BuildUserPayload(user));
    }

    private object BuildAuthResponse(AppUser user)
    {
        var token = _jwtTokenService.CreateToken(user);
        return new
        {
            token,
            user = BuildUserPayload(user)
        };
    }

    private static object BuildUserPayload(AppUser user)
    {
        return new
        {
            id = user.Id,
            fullName = user.FullName,
            email = user.Email,
            role = user.Role.ToString().ToUpperInvariant(),
            isActive = user.IsActive,
            createdAt = user.CreatedAt,
            updatedAt = user.UpdatedAt
        };
    }

    private static string? ValidateRegisterRequest(RegisterRequest request)
    {
        if (request == null)
        {
            return "Request body is required.";
        }

        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length < 2)
        {
            return "Full name must be at least 2 characters.";
        }

        if (string.IsNullOrWhiteSpace(request.Email) || !new EmailAddressAttribute().IsValid(request.Email))
        {
            return "A valid email is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            return "Password must be at least 8 characters.";
        }

        if (string.IsNullOrWhiteSpace(request.Role))
        {
            return "Role is required.";
        }

        return null;
    }

    private static string NormalizeEmail(string email)
        => email.Trim().ToLowerInvariant();

    private static UserRole? ParseRegisterRole(string? rawRole)
    {
        if (string.IsNullOrWhiteSpace(rawRole))
        {
            return null;
        }

        return rawRole.Trim().ToUpperInvariant() switch
        {
            "LEARNER" => UserRole.Learner,
            "INSTRUCTOR" => UserRole.Instructor,
            _ => null
        };
    }
}

public class RegisterRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
