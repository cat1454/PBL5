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
    [HttpPost("smoke-seed")]
    public async Task<IActionResult> SmokeSeed()
    {
        var oldTeacher = await _dbContext.AppUsers.FirstOrDefaultAsync(u => u.Email == "teacher_smoke@t.com");
        if (oldTeacher != null)
        {
            var oldWorkspaces = await _dbContext.ClassroomWorkspaces.Where(w => w.OwnerUserId == oldTeacher.Id).ToListAsync();
            _dbContext.ClassroomWorkspaces.RemoveRange(oldWorkspaces);
            _dbContext.AppUsers.Remove(oldTeacher);
        }

        var oldStudent = await _dbContext.AppUsers.FirstOrDefaultAsync(u => u.Email == "student_smoke@t.com");
        if (oldStudent != null)
        {
            _dbContext.AppUsers.Remove(oldStudent);
        }

        var oldDoc = await _dbContext.Documents.FirstOrDefaultAsync(d => d.FileName == "smoke.pdf");
        if (oldDoc != null)
        {
            var oldQuestions = await _dbContext.Questions.Where(q => q.DocumentId == oldDoc.Id).ToListAsync();
            _dbContext.Questions.RemoveRange(oldQuestions);
            _dbContext.Documents.Remove(oldDoc);
        }
        await _dbContext.SaveChangesAsync();

        var teacher = new AppUser
        {
            FullName = "Smoke Teacher",
            Email = "teacher_smoke@t.com",
            PasswordHash = string.Empty,
            Role = UserRole.Instructor,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        teacher.PasswordHash = _passwordService.HashPassword(teacher, "Password123!");

        var student = new AppUser
        {
            FullName = "Smoke Student",
            Email = "student_smoke@t.com",
            PasswordHash = string.Empty,
            Role = UserRole.Learner,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        student.PasswordHash = _passwordService.HashPassword(student, "Password123!");

        _dbContext.AppUsers.AddRange(teacher, student);
        await _dbContext.SaveChangesAsync();

        var document = new Document
        {
            FileName = "smoke.pdf",
            FileType = "PDF",
            FilePath = "/tmp/smoke.pdf",
            UploadedBy = teacher.Id.ToString()
        };
        _dbContext.Documents.Add(document);
        await _dbContext.SaveChangesAsync();

        var q1 = new Question
        {
            DocumentId = document.Id,
            QuestionText = "What is the capital of France?",
            QuestionType = QuestionType.MultipleChoice,
            OptionsJson = "[{\"key\":\"A\",\"text\":\"Paris\"},{\"key\":\"B\",\"text\":\"London\"},{\"key\":\"C\",\"text\":\"Berlin\"}]",
            CorrectAnswer = "A",
            Explanation = "Paris is the capital of France.",
            Difficulty = DifficultyLevel.Medium,
            CreatedAt = DateTime.UtcNow
        };

        var q2 = new Question
        {
            DocumentId = document.Id,
            QuestionText = "What is 2 + 2?",
            QuestionType = QuestionType.MultipleChoice,
            OptionsJson = "[{\"key\":\"A\",\"text\":\"3\"},{\"key\":\"B\",\"text\":\"4\"},{\"key\":\"C\",\"text\":\"5\"}]",
            CorrectAnswer = "B",
            Explanation = "2 + 2 is 4.",
            Difficulty = DifficultyLevel.Easy,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Questions.AddRange(q1, q2);
        await _dbContext.SaveChangesAsync();

        var workspace = new ClassroomWorkspace
        {
            Name = "Smoke Workspace",
            Description = "Workspace for Playwright Smoke Testing",
            OwnerUserId = teacher.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.ClassroomWorkspaces.Add(workspace);
        await _dbContext.SaveChangesAsync();

        _dbContext.ClassroomMembers.Add(new ClassroomMember
        {
            ClassroomWorkspaceId = workspace.Id,
            UserId = teacher.Id,
            Role = ClassroomRole.Teacher,
            Status = ClassroomMemberStatus.Active,
            JoinedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var questionSet = new ClassroomQuestionSet
        {
            ClassroomWorkspaceId = workspace.Id,
            Title = "Smoke Question Set",
            Description = "Seeded question set",
            CreatedByUserId = teacher.Id,
            Visibility = ClassroomQuestionSetVisibility.Published,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.ClassroomQuestionSets.Add(questionSet);
        await _dbContext.SaveChangesAsync();

        _dbContext.ClassroomQuestionSetItems.AddRange(
            new ClassroomQuestionSetItem
            {
                ClassroomQuestionSetId = questionSet.Id,
                QuestionId = q1.Id,
                OrderIndex = 0,
                PointWeight = 1,
                CreatedAt = DateTime.UtcNow
            },
            new ClassroomQuestionSetItem
            {
                ClassroomQuestionSetId = questionSet.Id,
                QuestionId = q2.Id,
                OrderIndex = 1,
                PointWeight = 3,
                CreatedAt = DateTime.UtcNow
            }
        );

        var joinCode = new ClassroomJoinCode
        {
            ClassroomWorkspaceId = workspace.Id,
            Code = "SMOKE123",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            MaxUses = 100,
            UsedCount = 0,
            IsActive = true,
            CreatedByUserId = teacher.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _dbContext.ClassroomJoinCodes.Add(joinCode);
        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            classroomId = workspace.Id,
            joinCode = "SMOKE123",
            questionSetId = questionSet.Id
        });
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
