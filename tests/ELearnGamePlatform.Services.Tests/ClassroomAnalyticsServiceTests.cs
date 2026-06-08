using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class ClassroomAnalyticsServiceTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Infrastructure helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ClassroomAnalyticsService CreateService(ApplicationDbContext context)
        => new ClassroomAnalyticsService(context, new ClassroomPermissionService(context));

    private static async Task<AppUser> AddUserAsync(ApplicationDbContext ctx, string fullName, string email, UserRole role = UserRole.Learner)
    {
        var user = new AppUser { FullName = fullName, Email = email, PasswordHash = "x", Role = role };
        ctx.AppUsers.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    private static async Task<(AppUser Teacher, AppUser Student, ClassroomWorkspace Workspace)> CreateFixtureAsync(ApplicationDbContext ctx)
    {
        var teacher = await AddUserAsync(ctx, "Teacher", "teacher@test.com", UserRole.Instructor);
        var student = await AddUserAsync(ctx, "Student", "student@test.com");

        var workspace = new ClassroomWorkspace { Name = "Analytics Class", OwnerUserId = teacher.Id };
        ctx.ClassroomWorkspaces.Add(workspace);
        await ctx.SaveChangesAsync();

        ctx.ClassroomMembers.AddRange(
            new ClassroomMember { ClassroomWorkspaceId = workspace.Id, UserId = teacher.Id, Role = ClassroomRole.Teacher, Status = ClassroomMemberStatus.Active },
            new ClassroomMember { ClassroomWorkspaceId = workspace.Id, UserId = student.Id, Role = ClassroomRole.Student, Status = ClassroomMemberStatus.Active });
        await ctx.SaveChangesAsync();

        return (teacher, student, workspace);
    }

    private static async Task<ClassroomAssignment> AddAssignmentAsync(
        ApplicationDbContext ctx,
        int classroomId,
        int teacherId,
        ClassroomAssignmentStatus status,
        string title = "Assignment",
        ClassroomScoringMode scoringMode = ClassroomScoringMode.Percent)
    {
        var qs = new ClassroomQuestionSet { ClassroomWorkspaceId = classroomId, Title = "QS-" + title, CreatedByUserId = teacherId, Visibility = ClassroomQuestionSetVisibility.Published };
        ctx.ClassroomQuestionSets.Add(qs);
        await ctx.SaveChangesAsync();

        var assignment = new ClassroomAssignment
        {
            ClassroomWorkspaceId = classroomId,
            QuestionSetId = qs.Id,
            Title = title,
            CreatedByUserId = teacherId,
            Status = status,
            ScoringMode = scoringMode
        };
        ctx.ClassroomAssignments.Add(assignment);
        await ctx.SaveChangesAsync();
        return assignment;
    }

    private static async Task<ClassroomAssignmentAttempt> AddAttemptAsync(
        ApplicationDbContext ctx,
        int assignmentId,
        int userId,
        ClassroomAttemptStatus status,
        decimal percentScore,
        decimal rawScore = 0m,
        DateTime? submittedAt = null,
        int attemptNumber = 1)
    {
        var attempt = new ClassroomAssignmentAttempt
        {
            ClassroomAssignmentId = assignmentId,
            UserId = userId,
            Status = status,
            PercentScore = percentScore,
            RawScore = rawScore,
            SubmittedAt = submittedAt ?? (status == ClassroomAttemptStatus.Submitted ? DateTime.UtcNow : null),
            AttemptNumber = attemptNumber
        };
        ctx.ClassroomAssignmentAttempts.Add(attempt);
        await ctx.SaveChangesAsync();
        return attempt;
    }

    private static async Task AddQuestionStatAsync(
        ApplicationDbContext ctx,
        int assignmentId,
        int questionId,
        int answeredCount,
        int correctCount,
        decimal difficultyWeight,
        string? qualityFlag = null)
    {
        ctx.ClassroomAssignmentQuestionStats.Add(new ClassroomAssignmentQuestionStat
        {
            ClassroomAssignmentId = assignmentId,
            QuestionId = questionId,
            AnsweredCount = answeredCount,
            CorrectCount = correctCount,
            SmoothedCorrectRate = answeredCount > 0 ? (decimal)correctCount / answeredCount : 0m,
            DifficultyWeight = difficultyWeight,
            QualityFlag = qualityFlag,
            CalculatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private static async Task<Question> AddQuestionAsync(ApplicationDbContext ctx, int documentId = 1)
    {
        var q = new Question
        {
            DocumentId = documentId,
            QuestionText = "Sample question?",
            CorrectAnswer = "A",
            OptionsJson = "[{\"key\":\"A\",\"text\":\"Option A\"}]",
            Difficulty = DifficultyLevel.Medium
        };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        return q;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: Teacher can view classroom analytics
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassroomAnalytics_TeacherCanViewAnalytics()
    {
        await using var ctx = CreateContext();
        var (teacher, _, workspace) = await CreateFixtureAsync(ctx);
        var service = CreateService(ctx);

        var result = await service.GetTeacherAnalyticsAsync(workspace.Id, teacher.Id);

        Assert.NotNull(result);
        Assert.Equal(workspace.Id, result.ClassroomId);
        Assert.Equal(workspace.Name, result.ClassroomName);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: Student cannot view teacher analytics
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassroomAnalytics_StudentCannotViewTeacherAnalytics()
    {
        await using var ctx = CreateContext();
        var (_, student, workspace) = await CreateFixtureAsync(ctx);
        var service = CreateService(ctx);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetTeacherAnalyticsAsync(workspace.Id, student.Id));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3: Non-member blocked from teacher analytics
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassroomAnalytics_NonMemberBlockedFromTeacherAnalytics()
    {
        await using var ctx = CreateContext();
        var (_, _, workspace) = await CreateFixtureAsync(ctx);
        var stranger = await AddUserAsync(ctx, "Stranger", "stranger@test.com");
        var service = CreateService(ctx);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetTeacherAnalyticsAsync(workspace.Id, stranger.Id));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4: Overview counts active students / assignments / attempts correctly
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassroomAnalytics_OverviewCountsCorrectly()
    {
        await using var ctx = CreateContext();
        var (teacher, student, workspace) = await CreateFixtureAsync(ctx);

        var published = await AddAssignmentAsync(ctx, workspace.Id, teacher.Id, ClassroomAssignmentStatus.Published, "Published");
        var closed = await AddAssignmentAsync(ctx, workspace.Id, teacher.Id, ClassroomAssignmentStatus.Closed, "Closed");
        var draft = await AddAssignmentAsync(ctx, workspace.Id, teacher.Id, ClassroomAssignmentStatus.Draft, "Draft");

        await AddAttemptAsync(ctx, published.Id, student.Id, ClassroomAttemptStatus.Submitted, 80m, submittedAt: DateTime.UtcNow);
        await AddAttemptAsync(ctx, closed.Id, student.Id, ClassroomAttemptStatus.Submitted, 90m, submittedAt: DateTime.UtcNow);

        var service = CreateService(ctx);
        var result = await service.GetTeacherAnalyticsAsync(workspace.Id, teacher.Id);

        Assert.Equal(1, result.Overview.ActiveStudentCount);
        Assert.Equal(3, result.Overview.AssignmentCount); // all 3 including draft
        Assert.Equal(1, result.Overview.PublishedAssignmentCount);
        Assert.Equal(1, result.Overview.ClosedAssignmentCount);
        Assert.Equal(2, result.Overview.SubmittedAttemptCount);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5: Completion rate calculated correctly
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassroomAnalytics_CompletionRateCalculatedCorrectly()
    {
        await using var ctx = CreateContext();
        var (teacher, student, workspace) = await CreateFixtureAsync(ctx);
        var student2 = await AddUserAsync(ctx, "S2", "s2@test.com");
        ctx.ClassroomMembers.Add(new ClassroomMember { ClassroomWorkspaceId = workspace.Id, UserId = student2.Id, Role = ClassroomRole.Student, Status = ClassroomMemberStatus.Active });
        await ctx.SaveChangesAsync();

        var assignment1 = await AddAssignmentAsync(ctx, workspace.Id, teacher.Id, ClassroomAssignmentStatus.Published, "A1");
        var assignment2 = await AddAssignmentAsync(ctx, workspace.Id, teacher.Id, ClassroomAssignmentStatus.Published, "A2");

        // Student1: completed A1 only
        await AddAttemptAsync(ctx, assignment1.Id, student.Id, ClassroomAttemptStatus.Submitted, 80m, submittedAt: DateTime.UtcNow);
        // Student2: completed both
        await AddAttemptAsync(ctx, assignment1.Id, student2.Id, ClassroomAttemptStatus.Submitted, 70m, submittedAt: DateTime.UtcNow);
        await AddAttemptAsync(ctx, assignment2.Id, student2.Id, ClassroomAttemptStatus.Submitted, 60m, submittedAt: DateTime.UtcNow);

        // Total possible: 2 assignments * 2 students = 4
        // Completed: S1 completed 1, S2 completed 2 → total = 3
        // CompletionRate = 3/4 * 100 = 75%

        var service = CreateService(ctx);
        var result = await service.GetTeacherAnalyticsAsync(workspace.Id, teacher.Id);

        Assert.Equal(75m, result.Overview.CompletionRate);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 6: Assignment summary counts submitted/in-progress/not-started correctly
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassroomAnalytics_AssignmentSummaryCountsCorrectly()
    {
        await using var ctx = CreateContext();
        var (teacher, student, workspace) = await CreateFixtureAsync(ctx);
        var student2 = await AddUserAsync(ctx, "S2", "s2@test.com");
        var student3 = await AddUserAsync(ctx, "S3", "s3@test.com");
        ctx.ClassroomMembers.AddRange(
            new ClassroomMember { ClassroomWorkspaceId = workspace.Id, UserId = student2.Id, Role = ClassroomRole.Student, Status = ClassroomMemberStatus.Active },
            new ClassroomMember { ClassroomWorkspaceId = workspace.Id, UserId = student3.Id, Role = ClassroomRole.Student, Status = ClassroomMemberStatus.Active });
        await ctx.SaveChangesAsync();

        var assignment = await AddAssignmentAsync(ctx, workspace.Id, teacher.Id, ClassroomAssignmentStatus.Published, "Quiz");

        // S1: submitted
        await AddAttemptAsync(ctx, assignment.Id, student.Id, ClassroomAttemptStatus.Submitted, 80m, submittedAt: DateTime.UtcNow);
        // S2: in progress
        await AddAttemptAsync(ctx, assignment.Id, student2.Id, ClassroomAttemptStatus.InProgress, 0m);
        // S3: not started (no attempt)

        var service = CreateService(ctx);
        var result = await service.GetTeacherAnalyticsAsync(workspace.Id, teacher.Id);

        var summary = result.AssignmentSummaries.Single(s => s.AssignmentId == assignment.Id);
        Assert.Equal(3, summary.TotalStudents);
        Assert.Equal(1, summary.SubmittedStudents);
        Assert.Equal(1, summary.InProgressStudents);
        Assert.Equal(1, summary.NotStartedStudents);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 7: Hardest/easiest questions sorted by difficultyWeight
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassroomAnalytics_HardestEasiestSortedByDifficultyWeight()
    {
        await using var ctx = CreateContext();
        var (teacher, student, workspace) = await CreateFixtureAsync(ctx);

        var assignment = await AddAssignmentAsync(ctx, workspace.Id, teacher.Id, ClassroomAssignmentStatus.Published, "Quiz");

        var q1 = await AddQuestionAsync(ctx);
        var q2 = await AddQuestionAsync(ctx);
        var q3 = await AddQuestionAsync(ctx);

        // q1 is easiest (lowest weight), q3 is hardest (highest weight)
        await AddQuestionStatAsync(ctx, assignment.Id, q1.Id, 10, 9, 0.5m);
        await AddQuestionStatAsync(ctx, assignment.Id, q2.Id, 10, 5, 1.2m);
        await AddQuestionStatAsync(ctx, assignment.Id, q3.Id, 10, 1, 2.0m);

        var service = CreateService(ctx);
        var result = await service.GetTeacherAnalyticsAsync(workspace.Id, teacher.Id);

        Assert.NotEmpty(result.QuestionInsights.HardestQuestions);
        Assert.NotEmpty(result.QuestionInsights.EasiestQuestions);

        // Hardest first should have highest difficultyWeight
        Assert.Equal(2.0m, result.QuestionInsights.HardestQuestions.First().DifficultyWeight);
        // Easiest first should have lowest difficultyWeight
        Assert.Equal(0.5m, result.QuestionInsights.EasiestQuestions.First().DifficultyWeight);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 8: At-risk students sorted correctly (lowest score + fewest completions first)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassroomAnalytics_AtRiskStudentsSortedCorrectly()
    {
        await using var ctx = CreateContext();
        var (teacher, student, workspace) = await CreateFixtureAsync(ctx);
        var student2 = await AddUserAsync(ctx, "S2", "s2@test.com");
        ctx.ClassroomMembers.Add(new ClassroomMember { ClassroomWorkspaceId = workspace.Id, UserId = student2.Id, Role = ClassroomRole.Student, Status = ClassroomMemberStatus.Active });
        await ctx.SaveChangesAsync();

        var assignment = await AddAssignmentAsync(ctx, workspace.Id, teacher.Id, ClassroomAssignmentStatus.Published, "Quiz");

        // student has low score
        await AddAttemptAsync(ctx, assignment.Id, student.Id, ClassroomAttemptStatus.Submitted, 20m, submittedAt: DateTime.UtcNow);
        // student2 has higher score
        await AddAttemptAsync(ctx, assignment.Id, student2.Id, ClassroomAttemptStatus.Submitted, 90m, submittedAt: DateTime.UtcNow);

        var service = CreateService(ctx);
        var result = await service.GetTeacherAnalyticsAsync(workspace.Id, teacher.Id);

        // Most at-risk (lowest score) should be first
        Assert.Equal(student.Id, result.AtRiskStudents.First().UserId);
        Assert.Equal(20m, result.AtRiskStudents.First().AveragePercentScore);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 9: Student can view own analytics
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassroomAnalytics_StudentCanViewOwnAnalytics()
    {
        await using var ctx = CreateContext();
        var (teacher, student, workspace) = await CreateFixtureAsync(ctx);

        var assignment = await AddAssignmentAsync(ctx, workspace.Id, teacher.Id, ClassroomAssignmentStatus.Published, "Quiz");
        await AddAttemptAsync(ctx, assignment.Id, student.Id, ClassroomAttemptStatus.Submitted, 85m, submittedAt: DateTime.UtcNow);

        var service = CreateService(ctx);
        var result = await service.GetStudentAnalyticsAsync(workspace.Id, student.Id);

        Assert.NotNull(result);
        Assert.Equal(workspace.Id, result.ClassroomId);
        Assert.Equal(student.Id, result.UserId);
        Assert.Equal(1, result.Summary.CompletedAssignments);
        Assert.Equal(85m, result.Summary.AveragePercentScore);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 10: Student cannot view teacher analytics (teacher endpoint forbidden)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassroomAnalytics_StudentCannotViewTeacherAnalyticsEndpoint()
    {
        await using var ctx = CreateContext();
        var (_, student, workspace) = await CreateFixtureAsync(ctx);
        var service = CreateService(ctx);

        // Student calling teacher analytics → forbidden
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetTeacherAnalyticsAsync(workspace.Id, student.Id));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 11: Non-member blocked from student analytics
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassroomAnalytics_NonMemberBlockedFromStudentAnalytics()
    {
        await using var ctx = CreateContext();
        var (_, _, workspace) = await CreateFixtureAsync(ctx);
        var stranger = await AddUserAsync(ctx, "Stranger", "stranger@test.com");
        var service = CreateService(ctx);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetStudentAnalyticsAsync(workspace.Id, stranger.Id));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 12: Recent attempts do not include answer details
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassroomAnalytics_StudentRecentAttemptsHaveNoAnswerDetails()
    {
        await using var ctx = CreateContext();
        var (teacher, student, workspace) = await CreateFixtureAsync(ctx);

        var assignment = await AddAssignmentAsync(ctx, workspace.Id, teacher.Id, ClassroomAssignmentStatus.Published, "Quiz");
        var attempt = await AddAttemptAsync(ctx, assignment.Id, student.Id, ClassroomAttemptStatus.Submitted, 70m, submittedAt: DateTime.UtcNow);

        // Add an answer (should NOT appear in student analytics response)
        ctx.ClassroomAssignmentAnswers.Add(new ClassroomAssignmentAnswer
        {
            AttemptId = attempt.Id,
            QuestionId = 999,
            SelectedAnswer = "A",
            IsCorrect = true,
            PointEarned = 1m,
            AnsweredAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx);
        var result = await service.GetStudentAnalyticsAsync(workspace.Id, student.Id);

        // Recent attempts only have summary fields, no answer data
        var recentAttempt = result.RecentAttempts.FirstOrDefault();
        Assert.NotNull(recentAttempt);
        Assert.Equal(attempt.Id, recentAttempt.AttemptId);
        Assert.Equal(70m, recentAttempt.PercentScore);

        // The StudentAttemptSummary type should NOT have fields for answers/correctAnswer/explanation
        // We verify the type does not expose those by checking the property set
        var properties = typeof(StudentAttemptSummary).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Answers", properties);
        Assert.DoesNotContain("CorrectAnswer", properties);
        Assert.DoesNotContain("Explanation", properties);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 13: Pending assignment flag works
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassroomAnalytics_PendingAssignmentFlagWorks()
    {
        await using var ctx = CreateContext();
        var (teacher, student, workspace) = await CreateFixtureAsync(ctx);

        var assignment1 = await AddAssignmentAsync(ctx, workspace.Id, teacher.Id, ClassroomAssignmentStatus.Published, "A1");
        var assignment2 = await AddAssignmentAsync(ctx, workspace.Id, teacher.Id, ClassroomAssignmentStatus.Published, "A2");

        // Student completes only A1
        await AddAttemptAsync(ctx, assignment1.Id, student.Id, ClassroomAttemptStatus.Submitted, 80m, submittedAt: DateTime.UtcNow);

        var service = CreateService(ctx);
        var result = await service.GetStudentAnalyticsAsync(workspace.Id, student.Id);

        Assert.True(result.HasPendingAssignments);
        Assert.Equal(1, result.Summary.CompletedAssignments);
        Assert.Equal(2, result.Summary.TotalAssignments);

        // Now complete A2
        await AddAttemptAsync(ctx, assignment2.Id, student.Id, ClassroomAttemptStatus.Submitted, 90m, submittedAt: DateTime.UtcNow);

        var result2 = await service.GetStudentAnalyticsAsync(workspace.Id, student.Id);
        Assert.False(result2.HasPendingAssignments);
    }
}
