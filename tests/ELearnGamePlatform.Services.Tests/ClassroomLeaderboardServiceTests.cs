using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ELearnGamePlatform.API.Controllers;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class ClassroomLeaderboardServiceTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ClassroomLeaderboardService CreateService(ApplicationDbContext context)
    {
        return new ClassroomLeaderboardService(context, new ClassroomPermissionService(context));
    }

    private static ClassroomLeaderboardsController CreateController(ApplicationDbContext context, AppUser user)
    {
        var controller = new ClassroomLeaderboardsController(CreateService(context));
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

    // ======================================================================
    // Helper Methods
    // ======================================================================

    private static async Task<AppUser> AddUserAsync(ApplicationDbContext context, string fullName, string email, UserRole role)
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

    private static async Task<ClassroomFixture> CreateClassroomFixtureAsync(ApplicationDbContext context)
    {
        var teacher = await AddUserAsync(context, "Teacher One", "teacher1@example.com", UserRole.Instructor);
        var student = await AddUserAsync(context, "Student One", "student1@example.com", UserRole.Learner);
        var workspace = new ClassroomWorkspace
        {
            Name = "Class 101",
            OwnerUserId = teacher.Id
        };
        context.ClassroomWorkspaces.Add(workspace);
        await context.SaveChangesAsync();

        context.ClassroomMembers.AddRange(
            new ClassroomMember
            {
                ClassroomWorkspaceId = workspace.Id,
                UserId = teacher.Id,
                Role = ClassroomRole.Teacher,
                Status = ClassroomMemberStatus.Active
            },
            new ClassroomMember
            {
                ClassroomWorkspaceId = workspace.Id,
                UserId = student.Id,
                Role = ClassroomRole.Student,
                Status = ClassroomMemberStatus.Active
            });
        await context.SaveChangesAsync();
        return new ClassroomFixture(teacher, student, workspace);
    }

    private static async Task<ClassroomAssignment> AddAssignmentAsync(
        ApplicationDbContext context,
        ClassroomWorkspace workspace,
        AppUser teacher,
        ClassroomAssignmentStatus status,
        string title = "Quiz 1",
        ClassroomScoringMode scoringMode = ClassroomScoringMode.Percent)
    {
        var questionSet = new ClassroomQuestionSet
        {
            ClassroomWorkspaceId = workspace.Id,
            Title = "Question Set for " + title,
            CreatedByUserId = teacher.Id,
            Visibility = ClassroomQuestionSetVisibility.Published
        };
        context.ClassroomQuestionSets.Add(questionSet);
        await context.SaveChangesAsync();

        var assignment = new ClassroomAssignment
        {
            ClassroomWorkspaceId = workspace.Id,
            QuestionSetId = questionSet.Id,
            Title = title,
            CreatedByUserId = teacher.Id,
            Status = status,
            ScoringMode = scoringMode,
            DueAt = DateTime.UtcNow.AddDays(7)
        };
        context.ClassroomAssignments.Add(assignment);
        await context.SaveChangesAsync();
        return assignment;
    }

    private static async Task<ClassroomAssignmentAttempt> AddAttemptAsync(
        ApplicationDbContext context,
        int assignmentId,
        int userId,
        ClassroomAttemptStatus status,
        decimal percentScore,
        decimal rawScore,
        int? durationSeconds,
        DateTime? submittedAt,
        int attemptNumber = 1)
    {
        var attempt = new ClassroomAssignmentAttempt
        {
            ClassroomAssignmentId = assignmentId,
            UserId = userId,
            StartedAt = DateTime.UtcNow.AddHours(-1),
            SubmittedAt = submittedAt,
            Status = status,
            PercentScore = percentScore,
            RawScore = rawScore,
            DurationSeconds = durationSeconds,
            AttemptNumber = attemptNumber
        };
        context.ClassroomAssignmentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        return attempt;
    }

    // ======================================================================
    // Test Cases - Assignment Leaderboard Permissions
    // ======================================================================

    [Fact]
    public async Task ClassroomLeaderboard_TeacherCanViewAssignmentLeaderboard()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Published);
        var service = CreateService(context);

        var leaderboard = await service.GetAssignmentLeaderboardAsync(assignment.Id, fixture.Teacher.Id);

        Assert.NotNull(leaderboard);
        Assert.Equal(assignment.Id, leaderboard.AssignmentId);
        Assert.Equal(fixture.Workspace.Id, leaderboard.ClassroomId);
    }

    [Fact]
    public async Task ClassroomLeaderboard_StudentMemberCanViewAssignmentLeaderboard()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Published);
        var service = CreateService(context);

        var leaderboard = await service.GetAssignmentLeaderboardAsync(assignment.Id, fixture.Student.Id);

        Assert.NotNull(leaderboard);
    }

    [Fact]
    public async Task ClassroomLeaderboard_NonMemberCannotViewAssignmentLeaderboard()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Published);
        var nonMember = await AddUserAsync(context, "Stranger", "stranger@example.com", UserRole.Learner);
        var service = CreateService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetAssignmentLeaderboardAsync(assignment.Id, nonMember.Id));
    }

    [Fact]
    public async Task ClassroomLeaderboard_TeacherFromAnotherClassroomCannotViewAssignmentLeaderboard()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var otherTeacher = await AddUserAsync(context, "Other Teacher", "other.teacher@example.com", UserRole.Instructor);
        var assignment = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Published);
        var service = CreateService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetAssignmentLeaderboardAsync(assignment.Id, otherTeacher.Id));
    }

    [Fact]
    public async Task ClassroomLeaderboard_StudentMemberCannotViewDraftAssignmentLeaderboard()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Draft);
        var service = CreateService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetAssignmentLeaderboardAsync(assignment.Id, fixture.Student.Id));

        // But teacher can
        var leaderboard = await service.GetAssignmentLeaderboardAsync(assignment.Id, fixture.Teacher.Id);
        Assert.NotNull(leaderboard);
    }

    // ======================================================================
    // Test Cases - Assignment Leaderboard Scoring & Ranking Logic
    // ======================================================================

    [Fact]
    public async Task ClassroomLeaderboard_OnlySubmittedAttemptsAreRanked()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var student2 = await AddUserAsync(context, "Student Two", "student2@example.com", UserRole.Learner);
        context.ClassroomMembers.Add(new ClassroomMember
        {
            ClassroomWorkspaceId = fixture.Workspace.Id,
            UserId = student2.Id,
            Role = ClassroomRole.Student,
            Status = ClassroomMemberStatus.Active
        });
        await context.SaveChangesAsync();

        var assignment = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Published);
        
        // Student 1 submitted
        await AddAttemptAsync(context, assignment.Id, fixture.Student.Id, ClassroomAttemptStatus.Submitted, 80m, 8m, 300, DateTime.UtcNow);
        // Student 2 InProgress
        await AddAttemptAsync(context, assignment.Id, student2.Id, ClassroomAttemptStatus.InProgress, 0m, 0m, null, null);

        var service = CreateService(context);
        var leaderboard = await service.GetAssignmentLeaderboardAsync(assignment.Id, fixture.Teacher.Id);

        Assert.Equal(2, leaderboard.TotalStudents);
        Assert.Equal(1, leaderboard.SubmittedStudents);
        Assert.Equal(1, leaderboard.InProgressStudents);
        Assert.Equal(0, leaderboard.NotStartedStudents);

        var row1 = leaderboard.Rows.First(r => r.UserId == fixture.Student.Id);
        var row2 = leaderboard.Rows.First(r => r.UserId == student2.Id);

        Assert.Equal(1, row1.Rank);
        Assert.Equal("Submitted", row1.StatusLabel);

        Assert.Null(row2.Rank);
        Assert.Equal("InProgress", row2.StatusLabel);
    }

    [Fact]
    public async Task ClassroomLeaderboard_BestAttemptSelectedWhenMultipleAttempts()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Published);

        // Attempt 1: 50%
        await AddAttemptAsync(context, assignment.Id, fixture.Student.Id, ClassroomAttemptStatus.Submitted, 50m, 5m, 400, DateTime.UtcNow.AddMinutes(-30), attemptNumber: 1);
        // Attempt 2: 90% (Best)
        var best = await AddAttemptAsync(context, assignment.Id, fixture.Student.Id, ClassroomAttemptStatus.Submitted, 90m, 9m, 300, DateTime.UtcNow.AddMinutes(-10), attemptNumber: 2);

        var service = CreateService(context);
        var leaderboard = await service.GetAssignmentLeaderboardAsync(assignment.Id, fixture.Teacher.Id);

        var row = leaderboard.Rows.First(r => r.UserId == fixture.Student.Id);
        Assert.Equal(best.Id, row.BestAttemptId);
        Assert.Equal(2, row.AttemptCount);
        Assert.Equal(90m, row.PercentScore);
    }

    [Fact]
    public async Task ClassroomLeaderboard_RankingOrderTieBreakingWorks()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var s2 = await AddUserAsync(context, "S2", "s2@example.com", UserRole.Learner);
        var s3 = await AddUserAsync(context, "S3", "s3@example.com", UserRole.Learner);
        var s4 = await AddUserAsync(context, "S4", "s4@example.com", UserRole.Learner);
        context.ClassroomMembers.AddRange(
            new ClassroomMember { ClassroomWorkspaceId = fixture.Workspace.Id, UserId = s2.Id, Role = ClassroomRole.Student, Status = ClassroomMemberStatus.Active },
            new ClassroomMember { ClassroomWorkspaceId = fixture.Workspace.Id, UserId = s3.Id, Role = ClassroomRole.Student, Status = ClassroomMemberStatus.Active },
            new ClassroomMember { ClassroomWorkspaceId = fixture.Workspace.Id, UserId = s4.Id, Role = ClassroomRole.Student, Status = ClassroomMemberStatus.Active }
        );
        await context.SaveChangesAsync();

        var assignment = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Published);

        var now = DateTime.UtcNow;

        // Rank 1: S4 (100%)
        await AddAttemptAsync(context, assignment.Id, s4.Id, ClassroomAttemptStatus.Submitted, 100m, 10m, 200, now);
        // Rank 2: Student 1 (90%, 9 raw, 150s, submitted earlier)
        await AddAttemptAsync(context, assignment.Id, fixture.Student.Id, ClassroomAttemptStatus.Submitted, 90m, 9m, 150, now.AddMinutes(-5));
        // Rank 3: S2 (90%, 9 raw, 150s, submitted later)
        await AddAttemptAsync(context, assignment.Id, s2.Id, ClassroomAttemptStatus.Submitted, 90m, 9m, 150, now);
        // Rank 4: S3 (90%, 9 raw, 300s)
        await AddAttemptAsync(context, assignment.Id, s3.Id, ClassroomAttemptStatus.Submitted, 90m, 9m, 300, now);

        var service = CreateService(context);
        var leaderboard = await service.GetAssignmentLeaderboardAsync(assignment.Id, fixture.Teacher.Id);

        Assert.Equal(1, leaderboard.Rows[0].Rank);
        Assert.Equal(s4.Id, leaderboard.Rows[0].UserId);

        Assert.Equal(2, leaderboard.Rows[1].Rank);
        Assert.Equal(fixture.Student.Id, leaderboard.Rows[1].UserId);

        Assert.Equal(3, leaderboard.Rows[2].Rank);
        Assert.Equal(s2.Id, leaderboard.Rows[2].UserId);

        Assert.Equal(4, leaderboard.Rows[3].Rank);
        Assert.Equal(s3.Id, leaderboard.Rows[3].UserId);
    }

    [Fact]
    public async Task ClassroomLeaderboard_ScoreFinalityBehavior()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        
        // 1. Empirical scoring mode + Published status -> ScoreFinality is Temporary
        var assignmentTemp = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Published, "Empirical Temp", ClassroomScoringMode.EmpiricalDifficulty);
        var service = CreateService(context);
        var lbTemp = await service.GetAssignmentLeaderboardAsync(assignmentTemp.Id, fixture.Teacher.Id);
        Assert.Equal("Temporary", lbTemp.ScoreFinality);

        // 2. Empirical scoring mode + Closed status -> ScoreFinality is Final
        var assignmentFinal = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Closed, "Empirical Final", ClassroomScoringMode.EmpiricalDifficulty);
        var lbFinal = await service.GetAssignmentLeaderboardAsync(assignmentFinal.Id, fixture.Teacher.Id);
        Assert.Equal("Final", lbFinal.ScoreFinality);

        // 3. Percent mode + Published status -> ScoreFinality is Final
        var assignmentPercent = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Published, "Percent Final", ClassroomScoringMode.Percent);
        var lbPercent = await service.GetAssignmentLeaderboardAsync(assignmentPercent.Id, fixture.Teacher.Id);
        Assert.Equal("Final", lbPercent.ScoreFinality);
    }

    // ======================================================================
    // Test Cases - Classroom Leaderboard
    // ======================================================================

    [Fact]
    public async Task ClassroomLeaderboard_TeacherAndStudentCanViewClassroomLeaderboard()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var service = CreateService(context);

        var lbTeacher = await service.GetClassroomLeaderboardAsync(fixture.Workspace.Id, fixture.Teacher.Id);
        var lbStudent = await service.GetClassroomLeaderboardAsync(fixture.Workspace.Id, fixture.Student.Id);

        Assert.NotNull(lbTeacher);
        Assert.NotNull(lbStudent);
        Assert.Equal(fixture.Workspace.Id, lbTeacher.ClassroomId);
        Assert.Equal(fixture.Workspace.Name, lbTeacher.ClassroomName);
    }

    [Fact]
    public async Task ClassroomLeaderboard_NonMemberCannotViewClassroomLeaderboard()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var nonMember = await AddUserAsync(context, "Stranger", "stranger@example.com", UserRole.Learner);
        var service = CreateService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetClassroomLeaderboardAsync(fixture.Workspace.Id, nonMember.Id));
    }

    [Fact]
    public async Task ClassroomLeaderboard_AggregatesAndRanksClassroomLeaderboardCorrectly()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var s2 = await AddUserAsync(context, "Student Two", "s2@example.com", UserRole.Learner);
        var s3 = await AddUserAsync(context, "Student Three", "s3@example.com", UserRole.Learner);
        context.ClassroomMembers.AddRange(
            new ClassroomMember { ClassroomWorkspaceId = fixture.Workspace.Id, UserId = s2.Id, Role = ClassroomRole.Student, Status = ClassroomMemberStatus.Active },
            new ClassroomMember { ClassroomWorkspaceId = fixture.Workspace.Id, UserId = s3.Id, Role = ClassroomRole.Student, Status = ClassroomMemberStatus.Active }
        );
        await context.SaveChangesAsync();

        var assignment1 = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Published, "Assignment 1");
        var assignment2 = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Closed, "Assignment 2");
        var assignmentDraft = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Draft, "Assignment Draft");

        var now = DateTime.UtcNow;

        // Student 1 (Student One):
        // Assignment 1: 1 attempt, 100%
        await AddAttemptAsync(context, assignment1.Id, fixture.Student.Id, ClassroomAttemptStatus.Submitted, 100m, 10m, 120, now.AddMinutes(-60));
        // Assignment 2: 1 attempt, 80%
        await AddAttemptAsync(context, assignment2.Id, fixture.Student.Id, ClassroomAttemptStatus.Submitted, 80m, 8m, 180, now.AddMinutes(-50));
        // Draft: 1 attempt (should be ignored since draft assignment isn't included)
        await AddAttemptAsync(context, assignmentDraft.Id, fixture.Student.Id, ClassroomAttemptStatus.Submitted, 90m, 9m, 100, now);

        // Student 2 (Student Two):
        // Assignment 1: 2 attempts, best is 90%
        await AddAttemptAsync(context, assignment1.Id, s2.Id, ClassroomAttemptStatus.Submitted, 40m, 4m, 100, now.AddMinutes(-90), attemptNumber: 1);
        await AddAttemptAsync(context, assignment1.Id, s2.Id, ClassroomAttemptStatus.Submitted, 90m, 9m, 150, now.AddMinutes(-40), attemptNumber: 2);
        // Assignment 2: 1 InProgress (ignored)
        await AddAttemptAsync(context, assignment2.Id, s2.Id, ClassroomAttemptStatus.InProgress, 0m, 0m, null, null);

        // Student 3 (Student Three):
        // No attempts at all (should have CompletedAssignments = 0, average = 0, total = 0, latestSubmittedAt = null)

        var service = CreateService(context);
        var leaderboard = await service.GetClassroomLeaderboardAsync(fixture.Workspace.Id, fixture.Teacher.Id);

        Assert.Equal(2, leaderboard.AssignmentCount);
        Assert.Equal(3, leaderboard.ActiveStudentCount);

        var r1 = leaderboard.Rows.First(r => r.UserId == fixture.Student.Id);
        var r2 = leaderboard.Rows.First(r => r.UserId == s2.Id);
        var r3 = leaderboard.Rows.First(r => r.UserId == s3.Id);

        // Student 1 metrics: completed = 2, average = (100+80)/2 = 90, total = 180, best = 100, latest = -50 mins
        Assert.Equal(2, r1.CompletedAssignments);
        Assert.Equal(90m, r1.AveragePercentScore);
        Assert.Equal(180m, r1.TotalPercentScore);
        Assert.Equal(100m, r1.BestPercentScore);
        Assert.NotNull(r1.LatestSubmittedAt);

        // Student 2 metrics: completed = 1, average = 90, total = 90, best = 90, latest = -40 mins
        Assert.Equal(1, r2.CompletedAssignments);
        Assert.Equal(2, r2.SubmittedAttempts); // count of all submitted attempts (1st and 2nd for Assignment 1)
        Assert.Equal(90m, r2.AveragePercentScore);
        Assert.Equal(90m, r2.TotalPercentScore);
        Assert.Equal(90m, r2.BestPercentScore);

        // Student 3 metrics: completed = 0, average = 0
        Assert.Equal(0, r3.CompletedAssignments);
        Assert.Equal(0m, r3.AveragePercentScore);
        Assert.Null(r3.LatestSubmittedAt);

        // Check ranking order:
        // Rank 1: Student 1 (average = 90, completed = 2)
        // Rank 2: Student 2 (average = 90, completed = 1)
        // Rank 3: Student 3 (average = 0, completed = 0)
        Assert.Equal(1, r1.Rank);
        Assert.Equal(2, r2.Rank);
        Assert.Equal(3, r3.Rank);

        // Verify the entire order list
        Assert.Equal(fixture.Student.Id, leaderboard.Rows[0].UserId);
        Assert.Equal(s2.Id, leaderboard.Rows[1].UserId);
        Assert.Equal(s3.Id, leaderboard.Rows[2].UserId);
    }

    // ======================================================================
    // Test Controller Integration (Basic Endpoint Tests)
    // ======================================================================

    [Fact]
    public async Task ClassroomLeaderboard_ControllerEndpointsReturnOk()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddAssignmentAsync(context, fixture.Workspace, fixture.Teacher, ClassroomAssignmentStatus.Published);
        var controller = CreateController(context, fixture.Teacher);

        var lbAssignmentResult = await controller.GetAssignmentLeaderboard(assignment.Id, CancellationToken.None);
        var okAssignment = Assert.IsType<OkObjectResult>(lbAssignmentResult);
        var respAssignment = Assert.IsType<AssignmentLeaderboardResponse>(okAssignment.Value);
        Assert.Equal(assignment.Id, respAssignment.AssignmentId);

        var lbClassroomResult = await controller.GetClassroomLeaderboard(fixture.Workspace.Id, CancellationToken.None);
        var okClassroom = Assert.IsType<OkObjectResult>(lbClassroomResult);
        var respClassroom = Assert.IsType<ClassroomLeaderboardResponse>(okClassroom.Value);
        Assert.Equal(fixture.Workspace.Id, respClassroom.ClassroomId);
    }

    private sealed record ClassroomFixture(AppUser Teacher, AppUser Student, ClassroomWorkspace Workspace);
}
