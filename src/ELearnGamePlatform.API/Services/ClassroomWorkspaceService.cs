using System.Security.Cryptography;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.API.Services;

public sealed class ClassroomWorkspaceService : IClassroomWorkspaceService
{
    private const string JoinCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int JoinCodeLength = 6;

    private readonly ApplicationDbContext _dbContext;

    public ClassroomWorkspaceService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ClassroomWorkspace> CreateAsync(
        int ownerUserId,
        CreateClassroomWorkspaceInput input,
        CancellationToken cancellationToken = default)
    {
        var name = NormalizeRequired(input.Name, "Classroom name is required.");
        var now = DateTime.UtcNow;
        var workspace = new ClassroomWorkspace
        {
            Name = name,
            Description = NormalizeOptional(input.Description),
            OwnerUserId = ownerUserId,
            CreatedAt = now,
            UpdatedAt = now,
            Members =
            {
                new ClassroomMember
                {
                    UserId = ownerUserId,
                    Role = ClassroomRole.Teacher,
                    Status = ClassroomMemberStatus.Active,
                    JoinedAt = now,
                    UpdatedAt = now
                }
            }
        };

        _dbContext.ClassroomWorkspaces.Add(workspace);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return workspace;
    }

    public async Task<IReadOnlyList<ClassroomWorkspace>> GetTeachingAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ClassroomWorkspaces
            .Include(workspace => workspace.Members)
            .Where(workspace =>
                !workspace.IsArchived
                && (workspace.OwnerUserId == userId
                    || workspace.Members.Any(member =>
                        member.UserId == userId
                        && member.Role == ClassroomRole.Teacher
                        && member.Status == ClassroomMemberStatus.Active)))
            .OrderByDescending(workspace => workspace.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClassroomWorkspace>> GetJoinedAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ClassroomWorkspaces
            .Include(workspace => workspace.Members)
            .Where(workspace =>
                !workspace.IsArchived
                && workspace.Members.Any(member =>
                    member.UserId == userId
                    && member.Role == ClassroomRole.Student
                    && member.Status == ClassroomMemberStatus.Active))
            .OrderByDescending(workspace => workspace.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ClassroomWorkspace?> GetVisibleByIdAsync(
        int classroomWorkspaceId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ClassroomWorkspaces
            .Include(workspace => workspace.Members)
                .ThenInclude(member => member.User)
            .Include(workspace => workspace.JoinCodes)
            .FirstOrDefaultAsync(
                workspace =>
                    workspace.Id == classroomWorkspaceId
                    && !workspace.IsArchived
                    && (workspace.OwnerUserId == userId
                        || workspace.Members.Any(member =>
                            member.UserId == userId && member.Status == ClassroomMemberStatus.Active)),
                cancellationToken);
    }

    public async Task<IReadOnlyList<ClassroomMember>> GetMembersAsync(
        int classroomWorkspaceId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ClassroomMembers
            .Include(member => member.User)
            .Where(member => member.ClassroomWorkspaceId == classroomWorkspaceId)
            .OrderBy(member => member.Role)
            .ThenBy(member => member.JoinedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ClassroomJoinCode> CreateJoinCodeAsync(
        int classroomWorkspaceId,
        int createdByUserId,
        CreateClassroomJoinCodeInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.ExpiresAt.HasValue && input.ExpiresAt.Value <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("Join code expiration must be in the future.");
        }

        if (input.MaxUses.HasValue && input.MaxUses.Value <= 0)
        {
            throw new InvalidOperationException("Join code max uses must be greater than zero.");
        }

        var classroomExists = await _dbContext.ClassroomWorkspaces.AnyAsync(
            workspace => workspace.Id == classroomWorkspaceId && !workspace.IsArchived,
            cancellationToken);
        if (!classroomExists)
        {
            throw new InvalidOperationException("Classroom workspace was not found.");
        }

        var now = DateTime.UtcNow;
        var joinCode = new ClassroomJoinCode
        {
            ClassroomWorkspaceId = classroomWorkspaceId,
            Code = await GenerateUniqueJoinCodeAsync(cancellationToken),
            ExpiresAt = input.ExpiresAt,
            MaxUses = input.MaxUses,
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.ClassroomJoinCodes.Add(joinCode);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return joinCode;
    }

    public async Task<ClassroomJoinCode?> DisableJoinCodeAsync(
        int classroomWorkspaceId,
        int codeId,
        CancellationToken cancellationToken = default)
    {
        var code = await _dbContext.ClassroomJoinCodes.FirstOrDefaultAsync(
            candidate => candidate.Id == codeId && candidate.ClassroomWorkspaceId == classroomWorkspaceId,
            cancellationToken);
        if (code == null)
        {
            return null;
        }

        code.IsActive = false;
        code.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return code;
    }

    public async Task<ClassroomWorkspace> JoinByCodeAsync(
        int userId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeRequired(code, "Join code is required.").ToUpperInvariant();
        var joinCode = await _dbContext.ClassroomJoinCodes
            .Include(candidate => candidate.ClassroomWorkspace)
            .FirstOrDefaultAsync(candidate => candidate.Code == normalizedCode, cancellationToken);

        if (joinCode == null)
        {
            throw new InvalidOperationException("Join code was not found.");
        }

        if (!joinCode.IsActive)
        {
            throw new InvalidOperationException("Join code is disabled.");
        }

        if (joinCode.ExpiresAt.HasValue && joinCode.ExpiresAt.Value <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("Join code has expired.");
        }

        if (joinCode.MaxUses.HasValue && joinCode.UsedCount >= joinCode.MaxUses.Value)
        {
            throw new InvalidOperationException("Join code has reached its maximum uses.");
        }

        if (joinCode.ClassroomWorkspace == null || joinCode.ClassroomWorkspace.IsArchived)
        {
            throw new InvalidOperationException("Classroom workspace is not available.");
        }

        var existingMember = await _dbContext.ClassroomMembers.FirstOrDefaultAsync(
            member => member.ClassroomWorkspaceId == joinCode.ClassroomWorkspaceId && member.UserId == userId,
            cancellationToken);
        if (existingMember != null)
        {
            if (existingMember.Status == ClassroomMemberStatus.Removed)
            {
                var rejoinAt = DateTime.UtcNow;
                existingMember.Role = ClassroomRole.Student;
                existingMember.Status = ClassroomMemberStatus.Active;
                existingMember.JoinedAt = rejoinAt;
                existingMember.UpdatedAt = rejoinAt;
                joinCode.UsedCount += 1;
                joinCode.UpdatedAt = rejoinAt;
                joinCode.ClassroomWorkspace.UpdatedAt = rejoinAt;

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await LoadWorkspaceMembersAsync(joinCode.ClassroomWorkspace, cancellationToken);
            return joinCode.ClassroomWorkspace;
        }

        var now = DateTime.UtcNow;
        _dbContext.ClassroomMembers.Add(new ClassroomMember
        {
            ClassroomWorkspaceId = joinCode.ClassroomWorkspaceId,
            UserId = userId,
            Role = ClassroomRole.Student,
            Status = ClassroomMemberStatus.Active,
            JoinedAt = now,
            UpdatedAt = now
        });
        joinCode.UsedCount += 1;
        joinCode.UpdatedAt = now;
        joinCode.ClassroomWorkspace.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await LoadWorkspaceMembersAsync(joinCode.ClassroomWorkspace, cancellationToken);
        return joinCode.ClassroomWorkspace;
    }

    private async Task LoadWorkspaceMembersAsync(
        ClassroomWorkspace workspace,
        CancellationToken cancellationToken)
    {
        await _dbContext.Entry(workspace)
            .Collection(item => item.Members)
            .LoadAsync(cancellationToken);
    }

    private async Task<string> GenerateUniqueJoinCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = GenerateJoinCode();
            var exists = await _dbContext.ClassroomJoinCodes.AnyAsync(candidate => candidate.Code == code, cancellationToken);
            if (!exists)
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not generate a unique join code.");
    }

    private static string GenerateJoinCode()
    {
        Span<char> chars = stackalloc char[JoinCodeLength];
        for (var index = 0; index < chars.Length; index++)
        {
            chars[index] = JoinCodeAlphabet[RandomNumberGenerator.GetInt32(JoinCodeAlphabet.Length)];
        }

        return new string(chars);
    }

    private static string NormalizeRequired(string? value, string message)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException(message);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
