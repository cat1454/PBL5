using ELearnGamePlatform.API.Controllers;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class ClassroomWorkspaceServiceTests
{
    [Fact]
    public async Task JoinByCodeAsync_AddsStudentMemberAndIncrementsUseCount()
    {
        await using var context = CreateContext();
        var teacher = await AddUserAsync(context, "Teacher", "teacher@example.com", UserRole.Instructor);
        var student = await AddUserAsync(context, "Student", "student@example.com", UserRole.Learner);
        var workspace = new ClassroomWorkspace
        {
            Name = "N5 Reading",
            OwnerUserId = teacher.Id
        };
        context.ClassroomWorkspaces.Add(workspace);
        await context.SaveChangesAsync();

        var code = new ClassroomJoinCode
        {
            ClassroomWorkspaceId = workspace.Id,
            Code = "ABC123",
            MaxUses = 2,
            CreatedByUserId = teacher.Id
        };
        context.ClassroomJoinCodes.Add(code);
        await context.SaveChangesAsync();

        var service = new ClassroomWorkspaceService(context);

        var joined = await service.JoinByCodeAsync(student.Id, "abc123");

        Assert.Equal(workspace.Id, joined.Id);
        var member = await context.ClassroomMembers.SingleAsync(item => item.UserId == student.Id);
        Assert.Equal(ClassroomRole.Student, member.Role);
        Assert.Equal(ClassroomMemberStatus.Active, member.Status);
        Assert.Equal(1, (await context.ClassroomJoinCodes.SingleAsync()).UsedCount);
    }

    [Fact]
    public async Task JoinByCodeAsync_DoesNotCreateDuplicateMemberOrIncrementUseCount()
    {
        await using var context = CreateContext();
        var teacher = await AddUserAsync(context, "Teacher", "teacher@example.com", UserRole.Instructor);
        var student = await AddUserAsync(context, "Student", "student@example.com", UserRole.Learner);
        var workspace = new ClassroomWorkspace
        {
            Name = "N5 Reading",
            OwnerUserId = teacher.Id
        };
        context.ClassroomWorkspaces.Add(workspace);
        await context.SaveChangesAsync();

        context.ClassroomMembers.Add(new ClassroomMember
        {
            ClassroomWorkspaceId = workspace.Id,
            UserId = student.Id,
            Role = ClassroomRole.Student,
            Status = ClassroomMemberStatus.Active
        });
        context.ClassroomJoinCodes.Add(new ClassroomJoinCode
        {
            ClassroomWorkspaceId = workspace.Id,
            Code = "ABC123",
            MaxUses = 2,
            UsedCount = 1,
            CreatedByUserId = teacher.Id
        });
        await context.SaveChangesAsync();

        var service = new ClassroomWorkspaceService(context);

        await service.JoinByCodeAsync(student.Id, "ABC123");

        Assert.Equal(1, await context.ClassroomMembers.CountAsync(item => item.UserId == student.Id));
        Assert.Equal(1, (await context.ClassroomJoinCodes.SingleAsync()).UsedCount);
    }

    [Fact]
    public async Task JoinByCodeAsync_ReactivatesRemovedMemberAndIncrementsUseCount()
    {
        await using var context = CreateContext();
        var teacher = await AddUserAsync(context, "Teacher", "teacher@example.com", UserRole.Instructor);
        var student = await AddUserAsync(context, "Student", "student@example.com", UserRole.Learner);
        var workspace = new ClassroomWorkspace
        {
            Name = "N5 Reading",
            OwnerUserId = teacher.Id
        };
        context.ClassroomWorkspaces.Add(workspace);
        await context.SaveChangesAsync();

        context.ClassroomMembers.Add(new ClassroomMember
        {
            ClassroomWorkspaceId = workspace.Id,
            UserId = student.Id,
            Role = ClassroomRole.Student,
            Status = ClassroomMemberStatus.Removed
        });
        context.ClassroomJoinCodes.Add(new ClassroomJoinCode
        {
            ClassroomWorkspaceId = workspace.Id,
            Code = "ABC123",
            MaxUses = 2,
            UsedCount = 0,
            CreatedByUserId = teacher.Id
        });
        await context.SaveChangesAsync();

        var service = new ClassroomWorkspaceService(context);

        await service.JoinByCodeAsync(student.Id, "ABC123");

        var member = await context.ClassroomMembers.SingleAsync(item => item.UserId == student.Id);
        Assert.Equal(ClassroomMemberStatus.Active, member.Status);
        Assert.Equal(ClassroomRole.Student, member.Role);
        Assert.Equal(1, await context.ClassroomMembers.CountAsync(item => item.UserId == student.Id));
        Assert.Equal(1, (await context.ClassroomJoinCodes.SingleAsync()).UsedCount);
    }

    [Fact]
    public async Task JoinByCodeAsync_RejectsExpiredInactiveOrFullyUsedCodes()
    {
        await using var context = CreateContext();
        var teacher = await AddUserAsync(context, "Teacher", "teacher@example.com", UserRole.Instructor);
        var student = await AddUserAsync(context, "Student", "student@example.com", UserRole.Learner);
        var workspace = new ClassroomWorkspace
        {
            Name = "N5 Reading",
            OwnerUserId = teacher.Id
        };
        context.ClassroomWorkspaces.Add(workspace);
        await context.SaveChangesAsync();

        context.ClassroomJoinCodes.AddRange(
            new ClassroomJoinCode { ClassroomWorkspaceId = workspace.Id, Code = "EXPIRED", ExpiresAt = DateTime.UtcNow.AddMinutes(-1), CreatedByUserId = teacher.Id },
            new ClassroomJoinCode { ClassroomWorkspaceId = workspace.Id, Code = "INACTIVE", IsActive = false, CreatedByUserId = teacher.Id },
            new ClassroomJoinCode { ClassroomWorkspaceId = workspace.Id, Code = "FULL", MaxUses = 1, UsedCount = 1, CreatedByUserId = teacher.Id });
        await context.SaveChangesAsync();

        var service = new ClassroomWorkspaceService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.JoinByCodeAsync(student.Id, "EXPIRED"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.JoinByCodeAsync(student.Id, "INACTIVE"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.JoinByCodeAsync(student.Id, "FULL"));
    }

    [Fact]
    public async Task PermissionService_AllowsOnlyTeacherToManageClassroom()
    {
        await using var context = CreateContext();
        var owner = await AddUserAsync(context, "Teacher", "teacher@example.com", UserRole.Instructor);
        var student = await AddUserAsync(context, "Student", "student@example.com", UserRole.Learner);
        var otherTeacher = await AddUserAsync(context, "Other Teacher", "other@example.com", UserRole.Instructor);
        var workspace = new ClassroomWorkspace
        {
            Name = "N5 Reading",
            OwnerUserId = owner.Id
        };
        context.ClassroomWorkspaces.Add(workspace);
        await context.SaveChangesAsync();
        context.ClassroomMembers.Add(new ClassroomMember
        {
            ClassroomWorkspaceId = workspace.Id,
            UserId = student.Id,
            Role = ClassroomRole.Student,
            Status = ClassroomMemberStatus.Active
        });
        await context.SaveChangesAsync();

        var permissions = new ClassroomPermissionService(context);

        Assert.True(await permissions.CanManageClassroomAsync(workspace.Id, owner.Id));
        Assert.False(await permissions.CanManageClassroomAsync(workspace.Id, student.Id));
        Assert.False(await permissions.CanManageClassroomAsync(workspace.Id, otherTeacher.Id));
        Assert.True(await permissions.CanViewClassroomAsync(workspace.Id, student.Id));
    }

    [Fact]
    public async Task Controller_RejectsStudentMemberManagementActions()
    {
        await using var context = CreateContext();
        var owner = await AddUserAsync(context, "Teacher", "teacher@example.com", UserRole.Instructor);
        var student = await AddUserAsync(context, "Student", "student@example.com", UserRole.Learner);
        var workspace = new ClassroomWorkspace
        {
            Name = "N5 Reading",
            OwnerUserId = owner.Id
        };
        context.ClassroomWorkspaces.Add(workspace);
        await context.SaveChangesAsync();
        context.ClassroomMembers.Add(new ClassroomMember
        {
            ClassroomWorkspaceId = workspace.Id,
            UserId = student.Id,
            Role = ClassroomRole.Student,
            Status = ClassroomMemberStatus.Active
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, student);

        var membersResult = await controller.GetMembers(workspace.Id, CancellationToken.None);
        var createCodeResult = await controller.CreateJoinCode(workspace.Id, new CreateClassroomJoinCodeRequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(membersResult).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(createCodeResult).StatusCode);
    }

    [Fact]
    public async Task Controller_RejectsOtherTeacherAndNonMemberAccess()
    {
        await using var context = CreateContext();
        var owner = await AddUserAsync(context, "Teacher", "teacher@example.com", UserRole.Instructor);
        var otherTeacher = await AddUserAsync(context, "Other Teacher", "other@example.com", UserRole.Instructor);
        var workspace = new ClassroomWorkspace
        {
            Name = "N5 Reading",
            OwnerUserId = owner.Id
        };
        context.ClassroomWorkspaces.Add(workspace);
        await context.SaveChangesAsync();

        var controller = CreateController(context, otherTeacher);

        var createCodeResult = await controller.CreateJoinCode(workspace.Id, new CreateClassroomJoinCodeRequest(), CancellationToken.None);
        var detailResult = await controller.GetById(workspace.Id, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(createCodeResult).StatusCode);
        Assert.IsType<NotFoundObjectResult>(detailResult);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ClassroomWorkspacesController CreateController(ApplicationDbContext context, AppUser user)
    {
        var controller = new ClassroomWorkspacesController(
            new ClassroomWorkspaceService(context),
            new ClassroomPermissionService(context));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Role, user.Role.ToString())
                    },
                    "TestAuth"))
            }
        };

        return controller;
    }

    private static async Task<AppUser> AddUserAsync(
        ApplicationDbContext context,
        string fullName,
        string email,
        UserRole role)
    {
        var user = new AppUser
        {
            FullName = fullName,
            Email = email,
            PasswordHash = "test",
            Role = role
        };
        context.AppUsers.Add(user);
        await context.SaveChangesAsync();
        return user;
    }
}
