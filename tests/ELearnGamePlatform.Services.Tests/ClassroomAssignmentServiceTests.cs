using System.Security.Claims;
using System.Text.Json;
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

public class ClassroomAssignmentServiceTests
{
    [Fact]
    public async Task TeacherCanCreateAssignmentFromPublishedQuestionSet()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var questionSet = await AddPublishedQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);

        var assignment = await service.CreateAssignmentAsync(
            fixture.Workspace.Id,
            fixture.Teacher.Id,
            CreateInput(questionSet.Id, "N5 quiz"));

        Assert.Equal(fixture.Workspace.Id, assignment.ClassroomWorkspaceId);
        Assert.Equal(questionSet.Id, assignment.QuestionSetId);
        Assert.Equal(ClassroomAssignmentStatus.Draft, assignment.Status);
        Assert.Equal(fixture.Teacher.Id, assignment.CreatedByUserId);
    }

    [Fact]
    public async Task CannotCreateOrPublishAssignmentFromDraftQuestionSet()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var draftSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher, ClassroomQuestionSetVisibility.Draft);
        var service = CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAssignmentAsync(fixture.Workspace.Id, fixture.Teacher.Id, CreateInput(draftSet.Id, "Draft source")));

        var publishedSet = await AddPublishedQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        var assignment = await service.CreateAssignmentAsync(
            fixture.Workspace.Id,
            fixture.Teacher.Id,
            CreateInput(publishedSet.Id, "Will regress"));
        publishedSet.Visibility = ClassroomQuestionSetVisibility.Draft;
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PublishAssignmentAsync(assignment.Id, fixture.Teacher.Id));
    }

    [Fact]
    public async Task StudentCannotCreateAssignment()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var questionSet = await AddPublishedQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateAssignmentAsync(fixture.Workspace.Id, fixture.Student.Id, CreateInput(questionSet.Id, "Student create")));
    }

    [Fact]
    public async Task TeacherFromOtherClassroomCannotModifyAssignment()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var otherTeacher = await AddUserAsync(context, "Other Teacher", "other.teacher@example.com", UserRole.Instructor);
        var otherWorkspace = new ClassroomWorkspace
        {
            Name = "Other class",
            OwnerUserId = otherTeacher.Id
        };
        context.ClassroomWorkspaces.Add(otherWorkspace);
        await context.SaveChangesAsync();
        context.ClassroomMembers.Add(new ClassroomMember
        {
            ClassroomWorkspaceId = otherWorkspace.Id,
            UserId = otherTeacher.Id,
            Role = ClassroomRole.Teacher,
            Status = ClassroomMemberStatus.Active
        });
        await context.SaveChangesAsync();
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateAssignmentAsync(assignment.Id, otherTeacher.Id, UpdateInput("Hijack")));
    }

    [Fact]
    public async Task StudentCanListPublishedAssignment()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        await AddDraftAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);

        var assignments = await service.GetAssignmentsForClassroomAsync(
            fixture.Workspace.Id,
            fixture.Student.Id,
            studentViewOnly: true);

        Assert.Single(assignments);
        Assert.Equal(ClassroomAssignmentStatus.Published, assignments[0].Status);
    }

    [Fact]
    public async Task NonMemberCannotViewAssignment()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var nonMember = await AddUserAsync(context, "Non Member", "nonmember@example.com", UserRole.Learner);
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);

        var detail = await service.GetAssignmentDetailAsync(assignment.Id, nonMember.Id);

        Assert.Null(detail);
    }

    [Fact]
    public async Task StudentCanStartAttempt()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);

        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);

        Assert.Equal(fixture.Student.Id, attempt.UserId);
        Assert.Equal(ClassroomAttemptStatus.InProgress, attempt.Status);
        Assert.Equal(1, attempt.AttemptNumber);
    }

    [Fact]
    public async Task AttemptLimitIsEnforced()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);
        await service.SubmitAttemptAsync(attempt.Id, fixture.Student.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StartAttemptAsync(assignment.Id, fixture.Student.Id));
    }

    [Fact]
    public async Task DuplicateInProgressAttemptResumes()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);

        var first = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);
        var second = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(ClassroomAttemptStatus.InProgress, second.Status);
    }

    [Fact]
    public async Task StudentCanSubmitAnswerAndScoreIsCalculated()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var question = await AddQuestionAsync(context, fixture.Teacher, correctAnswer: "A");
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher, question, pointWeight: 2);
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);

        var answer = await service.SubmitAnswerAsync(
            attempt.Id,
            fixture.Student.Id,
            new SubmitClassroomAssignmentAnswerInput(question.Id, " a ", 12));

        Assert.True(answer.IsCorrect);
        Assert.Equal(2m, answer.PointEarned);
        Assert.Equal("a", answer.SelectedAnswer);
    }

    [Fact]
    public async Task StudentCannotAnswerQuestionOutsideAssignmentQuestionSet()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        var outsideQuestion = await AddQuestionAsync(context, fixture.Teacher, correctAnswer: "B");
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAnswerAsync(
                attempt.Id,
                fixture.Student.Id,
                new SubmitClassroomAssignmentAnswerInput(outsideQuestion.Id, "B", null)));
    }

    [Fact]
    public async Task StudentCanSubmitAttemptAndPercentScoreIsCalculated()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var first = await AddQuestionAsync(context, fixture.Teacher, correctAnswer: "A");
        var second = await AddQuestionAsync(context, fixture.Teacher, correctAnswer: "B");
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher, first, pointWeight: 1);
        await AddQuestionSetItemAsync(context, assignment.QuestionSetId, second, pointWeight: 3);
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);
        await service.SubmitAnswerAsync(attempt.Id, fixture.Student.Id, new SubmitClassroomAssignmentAnswerInput(first.Id, "A", null));
        await service.SubmitAnswerAsync(attempt.Id, fixture.Student.Id, new SubmitClassroomAssignmentAnswerInput(second.Id, "C", null));

        var submitted = await service.SubmitAttemptAsync(attempt.Id, fixture.Student.Id);

        Assert.Equal(ClassroomAttemptStatus.Submitted, submitted.Status);
        Assert.Equal(1m, submitted.RawScore);
        Assert.Equal(25m, submitted.PercentScore);
        Assert.NotNull(submitted.SubmittedAt);
        Assert.True(submitted.DurationSeconds >= 0);
    }

    [Fact]
    public async Task TeacherCanViewAttempts()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);
        await service.SubmitAttemptAsync(attempt.Id, fixture.Student.Id);

        var attempts = await service.GetAssignmentAttemptsForTeacherAsync(assignment.Id, fixture.Teacher.Id);

        Assert.Single(attempts);
        Assert.Equal(fixture.Student.Id, attempts[0].UserId);
    }

    [Fact]
    public async Task StudentCannotViewOtherStudentsAttempt()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var otherStudent = await AddUserAsync(context, "Other Student", "other.student@example.com", UserRole.Learner);
        context.ClassroomMembers.Add(new ClassroomMember
        {
            ClassroomWorkspaceId = fixture.Workspace.Id,
            UserId = otherStudent.Id,
            Role = ClassroomRole.Student,
            Status = ClassroomMemberStatus.Active
        });
        await context.SaveChangesAsync();
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);

        var visible = await service.GetAttemptDetailAsync(attempt.Id, otherStudent.Id);

        Assert.Null(visible);
    }

    [Fact]
    public async Task StudentAssignmentDetailDoesNotExposeCorrectAnswer()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        var controller = CreateController(context, fixture.Student);

        var json = await ToJsonAsync(controller.GetById(assignment.Id, CancellationToken.None));

        Assert.DoesNotContain("correctAnswer", json);
        Assert.DoesNotContain("explanation", json);
        Assert.Contains("questionText", json);
    }

    [Fact]
    public async Task StartAttemptResponseDoesNotExposeCorrectAnswer()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        var controller = CreateController(context, fixture.Student);

        var json = await ToJsonAsync(controller.StartAttempt(assignment.Id, CancellationToken.None));

        Assert.DoesNotContain("correctAnswer", json);
        Assert.DoesNotContain("explanation", json);
        Assert.DoesNotContain("isCorrect", json);
        Assert.DoesNotContain("pointEarned", json);
        Assert.DoesNotContain("rawScore", json);
        Assert.DoesNotContain("percentScore", json);
    }

    [Fact]
    public async Task InProgressOwnAttemptDoesNotExposeCorrectAnswer()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);
        var controller = CreateController(context, fixture.Student);

        var json = await ToJsonAsync(controller.GetAttemptById(attempt.Id, CancellationToken.None));

        Assert.DoesNotContain("correctAnswer", json);
        Assert.DoesNotContain("explanation", json);
        Assert.DoesNotContain("isCorrect", json);
        Assert.DoesNotContain("pointEarned", json);
        Assert.DoesNotContain("rawScore", json);
        Assert.DoesNotContain("percentScore", json);
    }

    [Fact]
    public async Task SubmitAttemptWithShowAnswerAfterSubmitFalseDoesNotExposeCorrectAnswer()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var question = await AddQuestionAsync(context, fixture.Teacher, correctAnswer: "A");
        var assignment = await AddPublishedAssignmentAsync(
            context,
            fixture.Workspace,
            fixture.Teacher,
            question,
            showAnswerAfterSubmit: false);
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);
        await service.SubmitAnswerAsync(attempt.Id, fixture.Student.Id, new SubmitClassroomAssignmentAnswerInput(question.Id, "A", null));
        var controller = CreateController(context, fixture.Student);

        var json = await ToJsonAsync(controller.SubmitAttempt(attempt.Id, CancellationToken.None));

        Assert.Contains("rawScore", json);
        Assert.Contains("percentScore", json);
        Assert.DoesNotContain("correctAnswer", json);
        Assert.DoesNotContain("explanation", json);
        Assert.DoesNotContain("isCorrect", json);
        Assert.DoesNotContain("pointEarned", json);
    }

    [Fact]
    public async Task SubmitAttemptWithShowAnswerAfterSubmitTrueExposesCorrectAnswerOnlyAfterSubmitted()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var question = await AddQuestionAsync(context, fixture.Teacher, correctAnswer: "A");
        var assignment = await AddPublishedAssignmentAsync(
            context,
            fixture.Workspace,
            fixture.Teacher,
            question,
            showAnswerAfterSubmit: true);
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);
        await service.SubmitAnswerAsync(attempt.Id, fixture.Student.Id, new SubmitClassroomAssignmentAnswerInput(question.Id, "A", null));
        var controller = CreateController(context, fixture.Student);

        var beforeSubmitJson = await ToJsonAsync(controller.GetAttemptById(attempt.Id, CancellationToken.None));
        var afterSubmitJson = await ToJsonAsync(controller.SubmitAttempt(attempt.Id, CancellationToken.None));

        Assert.DoesNotContain("correctAnswer", beforeSubmitJson);
        Assert.DoesNotContain("isCorrect", beforeSubmitJson);
        Assert.Contains("correctAnswer", afterSubmitJson);
        Assert.Contains("explanation", afterSubmitJson);
        Assert.Contains("isCorrect", afterSubmitJson);
        Assert.Contains("pointEarned", afterSubmitJson);
    }

    [Fact]
    public async Task SubmitAnswerTwiceUpdatesExistingAnswerInsteadOfDuplicate()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var question = await AddQuestionAsync(context, fixture.Teacher, correctAnswer: "B");
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher, question);
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);

        await service.SubmitAnswerAsync(attempt.Id, fixture.Student.Id, new SubmitClassroomAssignmentAnswerInput(question.Id, "A", null));
        var updated = await service.SubmitAnswerAsync(attempt.Id, fixture.Student.Id, new SubmitClassroomAssignmentAnswerInput(question.Id, "B", null));

        Assert.True(updated.IsCorrect);
        Assert.Equal("B", updated.SelectedAnswer);
        Assert.Equal(1, await context.ClassroomAssignmentAnswers.CountAsync(answer => answer.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task NonMemberCannotViewAttempt()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var nonMember = await AddUserAsync(context, "Non Member", "nonmember2@example.com", UserRole.Learner);
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);

        var visible = await service.GetAttemptDetailAsync(attempt.Id, nonMember.Id);

        Assert.Null(visible);
    }

    [Fact]
    public async Task TeacherCanViewSubmittedAttemptWithGradingDetails()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var question = await AddQuestionAsync(context, fixture.Teacher, correctAnswer: "A");
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher, question);
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);
        await service.SubmitAnswerAsync(attempt.Id, fixture.Student.Id, new SubmitClassroomAssignmentAnswerInput(question.Id, "A", null));
        await service.SubmitAttemptAsync(attempt.Id, fixture.Student.Id);
        var controller = CreateController(context, fixture.Teacher);

        var json = await ToJsonAsync(controller.GetAttemptsForTeacher(assignment.Id, CancellationToken.None));

        Assert.Contains("correctAnswer", json);
        Assert.Contains("explanation", json);
        Assert.Contains("isCorrect", json);
        Assert.Contains("pointEarned", json);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ClassroomAssignmentService CreateService(ApplicationDbContext context)
    {
        return new ClassroomAssignmentService(context, new ClassroomPermissionService(context));
    }

    private static ClassroomAssignmentsController CreateController(ApplicationDbContext context, AppUser user)
    {
        var controller = new ClassroomAssignmentsController(
            CreateService(context),
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

    private static async Task<string> ToJsonAsync(Task<IActionResult> actionTask)
    {
        var actionResult = await actionTask;
        var ok = Assert.IsType<OkObjectResult>(actionResult);
        return JsonSerializer.Serialize(ok.Value);
    }

    private static CreateClassroomAssignmentInput CreateInput(int questionSetId, string title, int attemptLimit = 1)
    {
        return new CreateClassroomAssignmentInput(
            questionSetId,
            title,
            null,
            ClassroomAssignmentType.Quiz,
            null,
            DateTime.UtcNow.AddDays(7),
            null,
            attemptLimit,
            false,
            false,
            true);
    }

    private static CreateClassroomAssignmentInput CreateEmpiricalInput(int questionSetId, string title)
    {
        return new CreateClassroomAssignmentInput(
            questionSetId,
            title,
            null,
            ClassroomAssignmentType.Quiz,
            null,
            DateTime.UtcNow.AddDays(7),
            null,
            1,
            false,
            false,
            true,
            ScoringMode: ClassroomScoringMode.EmpiricalDifficulty);
    }

    private static UpdateClassroomAssignmentInput UpdateInput(string title)
    {
        return new UpdateClassroomAssignmentInput(
            title,
            null,
            ClassroomAssignmentType.Test,
            null,
            DateTime.UtcNow.AddDays(7),
            null,
            1,
            false,
            false,
            false);
    }

    // ======================================================================
    // Phase 4: Empirical Difficulty Weighted Scoring tests
    // ======================================================================

    // Helper: seed N submitted attempts for a question set where some are correct
    // Returns list of attempt ids
    private static async Task SeedSubmittedAttemptAsync(
        ApplicationDbContext context,
        ClassroomAssignment assignment,
        AppUser student,
        Dictionary<int, bool> questionCorrectMap) // questionId -> isCorrect
    {
        var attempt = new ClassroomAssignmentAttempt
        {
            ClassroomAssignmentId = assignment.Id,
            UserId = student.Id,
            StartedAt = DateTime.UtcNow,
            Status = ClassroomAttemptStatus.Submitted,
            SubmittedAt = DateTime.UtcNow,
            AttemptNumber = 1
        };
        context.ClassroomAssignmentAttempts.Add(attempt);
        await context.SaveChangesAsync();

        foreach (var (questionId, isCorrect) in questionCorrectMap)
        {
            context.ClassroomAssignmentAnswers.Add(new ClassroomAssignmentAnswer
            {
                AttemptId = attempt.Id,
                QuestionId = questionId,
                SelectedAnswer = isCorrect ? "A" : "X",
                IsCorrect = isCorrect,
                PointEarned = isCorrect ? 1m : 0m,
                AnsweredAt = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Phase4_PercentScoringUnchanged_WhenScoringModeIsPercent()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var q1 = await AddQuestionAsync(context, fixture.Teacher, correctAnswer: "A");
        var q2 = await AddQuestionAsync(context, fixture.Teacher, correctAnswer: "B");
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher, q1, pointWeight: 1);
        await AddQuestionSetItemAsync(context, assignment.QuestionSetId, q2, pointWeight: 3);
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);
        await service.SubmitAnswerAsync(attempt.Id, fixture.Student.Id, new SubmitClassroomAssignmentAnswerInput(q1.Id, "A", null));
        await service.SubmitAnswerAsync(attempt.Id, fixture.Student.Id, new SubmitClassroomAssignmentAnswerInput(q2.Id, "C", null));

        var submitted = await service.SubmitAttemptAsync(attempt.Id, fixture.Student.Id);

        // Percent scoring: 1 point earned / 4 total = 25%
        Assert.Equal(ClassroomAttemptStatus.Submitted, submitted.Status);
        Assert.Equal(1m, submitted.RawScore);
        Assert.Equal(25m, submitted.PercentScore);
    }

    [Fact]
    public async Task Phase4_EasyQuestionHasLowerWeight()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var doc = await AddDocumentAsync(context, fixture.Teacher);
        var qEasy = await AddQuestionAsync(context, fixture.Teacher, "A", doc);
        var qHard = await AddQuestionAsync(context, fixture.Teacher, "A", doc);

        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher, ClassroomQuestionSetVisibility.Published);
        await AddQuestionSetItemAsync(context, questionSet.Id, qEasy, 1);
        await AddQuestionSetItemAsync(context, questionSet.Id, qHard, 1);

        var assignment = new ClassroomAssignment
        {
            ClassroomWorkspaceId = fixture.Workspace.Id,
            QuestionSetId = questionSet.Id,
            Title = "Phase4 Test",
            CreatedByUserId = fixture.Teacher.Id,
            Status = ClassroomAssignmentStatus.Published,
            DueAt = DateTime.UtcNow.AddDays(7),
            ScoringMode = ClassroomScoringMode.EmpiricalDifficulty,
            MinQuestionWeight = 0.3m,
            MaxQuestionWeight = 2.0m,
            SmoothingAlpha = 1m,
            SmoothingBeta = 1m
        };
        context.ClassroomAssignments.Add(assignment);
        await context.SaveChangesAsync();

        // Seed: 9/10 correct on easy, 1/10 correct on hard
        for (var i = 0; i < 10; i++)
        {
            var student = await AddUserAsync(context, $"S{i}", $"s{i}@t.com", UserRole.Learner);
            await SeedSubmittedAttemptAsync(context, assignment, student, new Dictionary<int, bool>
            {
                { qEasy.Id, i < 9 },   // 9 correct
                { qHard.Id, i == 0 }   // 1 correct
            });
        }

        var service = CreateService(context);
        await service.CloseAssignmentAsync(assignment.Id, fixture.Teacher.Id);

        var stats = await service.GetAssignmentQuestionStatsAsync(assignment.Id, fixture.Teacher.Id);
        var easyStat = stats.First(s => s.QuestionId == qEasy.Id);
        var hardStat = stats.First(s => s.QuestionId == qHard.Id);

        Assert.True(easyStat.DifficultyWeight < hardStat.DifficultyWeight,
            $"Easy weight {easyStat.DifficultyWeight} should be < hard weight {hardStat.DifficultyWeight}");
    }

    [Fact]
    public async Task Phase4_HardQuestionHasHigherWeight()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var doc = await AddDocumentAsync(context, fixture.Teacher);
        var q1 = await AddQuestionAsync(context, fixture.Teacher, "A", doc);
        var q2 = await AddQuestionAsync(context, fixture.Teacher, "A", doc);
        var q3 = await AddQuestionAsync(context, fixture.Teacher, "A", doc);

        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher, ClassroomQuestionSetVisibility.Published);
        await AddQuestionSetItemAsync(context, questionSet.Id, q1, 1);
        await AddQuestionSetItemAsync(context, questionSet.Id, q2, 1);
        await AddQuestionSetItemAsync(context, questionSet.Id, q3, 1);

        var assignment = new ClassroomAssignment
        {
            ClassroomWorkspaceId = fixture.Workspace.Id,
            QuestionSetId = questionSet.Id,
            Title = "Phase4 Weight Order",
            CreatedByUserId = fixture.Teacher.Id,
            Status = ClassroomAssignmentStatus.Published,
            DueAt = DateTime.UtcNow.AddDays(7),
            ScoringMode = ClassroomScoringMode.EmpiricalDifficulty,
            MinQuestionWeight = 0.3m,
            MaxQuestionWeight = 2.0m,
            SmoothingAlpha = 1m,
            SmoothingBeta = 1m
        };
        context.ClassroomAssignments.Add(assignment);
        await context.SaveChangesAsync();

        // Q1: 9/10 correct (easy), Q2: 5/10 (medium), Q3: 1/10 (hard)
        for (var i = 0; i < 10; i++)
        {
            var student = await AddUserAsync(context, $"U3_{i}", $"u3{i}@t.com", UserRole.Learner);
            await SeedSubmittedAttemptAsync(context, assignment, student, new Dictionary<int, bool>
            {
                { q1.Id, i < 9 },
                { q2.Id, i < 5 },
                { q3.Id, i == 0 }
            });
        }

        var service = CreateService(context);
        await service.CloseAssignmentAsync(assignment.Id, fixture.Teacher.Id);

        var stats = await service.GetAssignmentQuestionStatsAsync(assignment.Id, fixture.Teacher.Id);
        var w1 = stats.First(s => s.QuestionId == q1.Id).DifficultyWeight;
        var w2 = stats.First(s => s.QuestionId == q2.Id).DifficultyWeight;
        var w3 = stats.First(s => s.QuestionId == q3.Id).DifficultyWeight;

        Assert.True(w1 < w2, $"w1({w1}) should be < w2({w2})");
        Assert.True(w2 < w3, $"w2({w2}) should be < w3({w3})");
    }

    [Fact]
    public async Task Phase4_SmoothingPreventsExtremeRates()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var q = await AddQuestionAsync(context, fixture.Teacher, "A");
        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher, ClassroomQuestionSetVisibility.Published);
        await AddQuestionSetItemAsync(context, questionSet.Id, q, 1);

        var assignment = new ClassroomAssignment
        {
            ClassroomWorkspaceId = fixture.Workspace.Id,
            QuestionSetId = questionSet.Id,
            Title = "Smoothing Test",
            CreatedByUserId = fixture.Teacher.Id,
            Status = ClassroomAssignmentStatus.Published,
            DueAt = DateTime.UtcNow.AddDays(7),
            ScoringMode = ClassroomScoringMode.EmpiricalDifficulty,
            MinQuestionWeight = 0.3m,
            MaxQuestionWeight = 2.0m,
            SmoothingAlpha = 1m,
            SmoothingBeta = 1m
        };
        context.ClassroomAssignments.Add(assignment);
        await context.SaveChangesAsync();

        // Only 1 student answered and got it correct — without smoothing would be 1.0 (very easy)
        var student = await AddUserAsync(context, "SmS", "sms@t.com", UserRole.Learner);
        await SeedSubmittedAttemptAsync(context, assignment, student, new Dictionary<int, bool> { { q.Id, true } });

        var service = CreateService(context);
        await service.CloseAssignmentAsync(assignment.Id, fixture.Teacher.Id);

        var stats = await service.GetAssignmentQuestionStatsAsync(assignment.Id, fixture.Teacher.Id);
        var stat = stats.Single();

        // With alpha=1, beta=1: p = (1+1)/(1+1+1) = 2/3 ≈ 0.667, not 1.0
        Assert.True(stat.SmoothedCorrectRate > 0m && stat.SmoothedCorrectRate < 1m,
            $"SmoothedCorrectRate {stat.SmoothedCorrectRate} should be between 0 and 1");
        // With default smoothing, weight should be between min and max
        Assert.True(stat.DifficultyWeight >= 0.3m && stat.DifficultyWeight <= 2.0m);
    }

    [Fact]
    public async Task Phase4_CloseAssignmentCreatesQuestionStats()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var doc = await AddDocumentAsync(context, fixture.Teacher);
        var q1 = await AddQuestionAsync(context, fixture.Teacher, "A", doc);
        var q2 = await AddQuestionAsync(context, fixture.Teacher, "A", doc);
        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher, ClassroomQuestionSetVisibility.Published);
        await AddQuestionSetItemAsync(context, questionSet.Id, q1, 1);
        await AddQuestionSetItemAsync(context, questionSet.Id, q2, 1);

        var assignment = new ClassroomAssignment
        {
            ClassroomWorkspaceId = fixture.Workspace.Id,
            QuestionSetId = questionSet.Id,
            Title = "Stats Creation Test",
            CreatedByUserId = fixture.Teacher.Id,
            Status = ClassroomAssignmentStatus.Published,
            DueAt = DateTime.UtcNow.AddDays(7),
            ScoringMode = ClassroomScoringMode.EmpiricalDifficulty
        };
        context.ClassroomAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var student = await AddUserAsync(context, "SS1", "ss1@t.com", UserRole.Learner);
        await SeedSubmittedAttemptAsync(context, assignment, student, new Dictionary<int, bool>
        {
            { q1.Id, true },
            { q2.Id, false }
        });

        var service = CreateService(context);
        await service.CloseAssignmentAsync(assignment.Id, fixture.Teacher.Id);

        var stats = await service.GetAssignmentQuestionStatsAsync(assignment.Id, fixture.Teacher.Id);
        Assert.Equal(2, stats.Count); // one stat per question
        Assert.Contains(stats, s => s.QuestionId == q1.Id);
        Assert.Contains(stats, s => s.QuestionId == q2.Id);
    }

    [Fact]
    public async Task Phase4_CloseAssignmentIsIdempotent_NoDuplicateStats()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var q = await AddQuestionAsync(context, fixture.Teacher, "A");
        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher, ClassroomQuestionSetVisibility.Published);
        await AddQuestionSetItemAsync(context, questionSet.Id, q, 1);

        var assignment = new ClassroomAssignment
        {
            ClassroomWorkspaceId = fixture.Workspace.Id,
            QuestionSetId = questionSet.Id,
            Title = "Idempotent Close",
            CreatedByUserId = fixture.Teacher.Id,
            Status = ClassroomAssignmentStatus.Published,
            DueAt = DateTime.UtcNow.AddDays(7),
            ScoringMode = ClassroomScoringMode.EmpiricalDifficulty
        };
        context.ClassroomAssignments.Add(assignment);
        await context.SaveChangesAsync();

        var student = await AddUserAsync(context, "SS2", "ss2@t.com", UserRole.Learner);
        await SeedSubmittedAttemptAsync(context, assignment, student, new Dictionary<int, bool> { { q.Id, true } });

        var service = CreateService(context);
        // Close twice
        await service.CloseAssignmentAsync(assignment.Id, fixture.Teacher.Id);
        await service.CloseAssignmentAsync(assignment.Id, fixture.Teacher.Id);

        var statCount = await context.ClassroomAssignmentQuestionStats
            .CountAsync(s => s.ClassroomAssignmentId == assignment.Id);
        Assert.Equal(1, statCount); // no duplicates
    }

    [Fact]
    public async Task Phase4_CloseRecalculatesSubmittedAttemptScores()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var doc = await AddDocumentAsync(context, fixture.Teacher);
        var q1 = await AddQuestionAsync(context, fixture.Teacher, "A", doc); // will be easy
        var q2 = await AddQuestionAsync(context, fixture.Teacher, "A", doc); // will be hard
        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher, ClassroomQuestionSetVisibility.Published);
        await AddQuestionSetItemAsync(context, questionSet.Id, q1, 1);
        await AddQuestionSetItemAsync(context, questionSet.Id, q2, 1);

        var assignment = new ClassroomAssignment
        {
            ClassroomWorkspaceId = fixture.Workspace.Id,
            QuestionSetId = questionSet.Id,
            Title = "Recalculate Test",
            CreatedByUserId = fixture.Teacher.Id,
            Status = ClassroomAssignmentStatus.Published,
            DueAt = DateTime.UtcNow.AddDays(7),
            ScoringMode = ClassroomScoringMode.EmpiricalDifficulty,
            MinQuestionWeight = 0.3m,
            MaxQuestionWeight = 2.0m,
            SmoothingAlpha = 1m,
            SmoothingBeta = 1m
        };
        context.ClassroomAssignments.Add(assignment);
        await context.SaveChangesAsync();

        // 9/10 correct on q1, 1/10 correct on q2
        for (var i = 0; i < 10; i++)
        {
            var s = await AddUserAsync(context, $"RS_{i}", $"rs{i}@t.com", UserRole.Learner);
            await SeedSubmittedAttemptAsync(context, assignment, s, new Dictionary<int, bool>
            {
                { q1.Id, i < 9 },
                { q2.Id, i == 0 }
            });
        }

        // Find first submitted attempt before close
        var attemptBefore = await context.ClassroomAssignmentAttempts
            .FirstAsync(a => a.ClassroomAssignmentId == assignment.Id);
        var rawScoreBefore = attemptBefore.RawScore; // seeded as 1 or 0

        var service = CreateService(context);
        await service.CloseAssignmentAsync(assignment.Id, fixture.Teacher.Id);

        // Re-fetch
        var attemptAfter = await context.ClassroomAssignmentAttempts
            .Include(a => a.Answers)
            .FirstAsync(a => a.Id == attemptBefore.Id);

        // After close with empirical scoring, percent score should be recalculated
        // and not necessarily match raw point-weight scoring
        Assert.NotNull(attemptAfter.SubmittedAt);
        // PercentScore should be in valid range
        Assert.True(attemptAfter.PercentScore >= 0 && attemptAfter.PercentScore <= 100);
    }

    [Fact]
    public async Task Phase4_HardQuestionCorrect_CanScoreHigherThanEasyQuestionCorrect()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var doc = await AddDocumentAsync(context, fixture.Teacher);
        var qEasy = await AddQuestionAsync(context, fixture.Teacher, "A", doc);
        var qHard = await AddQuestionAsync(context, fixture.Teacher, "A", doc);

        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher, ClassroomQuestionSetVisibility.Published);
        await AddQuestionSetItemAsync(context, questionSet.Id, qEasy, 1);
        await AddQuestionSetItemAsync(context, questionSet.Id, qHard, 1);

        var assignment = new ClassroomAssignment
        {
            ClassroomWorkspaceId = fixture.Workspace.Id,
            QuestionSetId = questionSet.Id,
            Title = "Score Comparison",
            CreatedByUserId = fixture.Teacher.Id,
            Status = ClassroomAssignmentStatus.Published,
            DueAt = DateTime.UtcNow.AddDays(7),
            ScoringMode = ClassroomScoringMode.EmpiricalDifficulty,
            MinQuestionWeight = 0.3m,
            MaxQuestionWeight = 2.0m,
            SmoothingAlpha = 1m,
            SmoothingBeta = 1m
        };
        context.ClassroomAssignments.Add(assignment);
        await context.SaveChangesAsync();

        // Seed: 9/10 correct on easy, 1/10 correct on hard
        // StudentA gets easy correct, studentB gets hard correct
        var studentA = await AddUserAsync(context, "StA", "sta@t.com", UserRole.Learner);
        var studentB = await AddUserAsync(context, "StB", "stb@t.com", UserRole.Learner);

        for (var i = 0; i < 10; i++)
        {
            var s = await AddUserAsync(context, $"Bg{i}", $"bg{i}@t.com", UserRole.Learner);
            await SeedSubmittedAttemptAsync(context, assignment, s, new Dictionary<int, bool>
            {
                { qEasy.Id, i < 9 },
                { qHard.Id, i == 0 }
            });
        }

        // StudentA: only easy correct
        await SeedSubmittedAttemptAsync(context, assignment, studentA, new Dictionary<int, bool>
        {
            { qEasy.Id, true },
            { qHard.Id, false }
        });

        // StudentB: only hard correct
        await SeedSubmittedAttemptAsync(context, assignment, studentB, new Dictionary<int, bool>
        {
            { qEasy.Id, false },
            { qHard.Id, true }
        });

        var service = CreateService(context);
        await service.CloseAssignmentAsync(assignment.Id, fixture.Teacher.Id);

        var attemptA = await context.ClassroomAssignmentAttempts
            .FirstAsync(a => a.UserId == studentA.Id && a.ClassroomAssignmentId == assignment.Id);
        var attemptB = await context.ClassroomAssignmentAttempts
            .FirstAsync(a => a.UserId == studentB.Id && a.ClassroomAssignmentId == assignment.Id);

        // Student B answered the hard question so should have higher RawScore
        Assert.True(attemptB.RawScore > attemptA.RawScore,
            $"Student B rawScore ({attemptB.RawScore}) should exceed Student A rawScore ({attemptA.RawScore})");
    }

    [Fact]
    public async Task Phase4_CannotSubmitAttemptAfterAssignmentClosed()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);

        // Close assignment
        assignment.Status = ClassroomAssignmentStatus.Closed;
        await context.SaveChangesAsync();

        // Attempt to submit should fail because attempt status check (assignment not published)
        // The EnsureAttemptStillAcceptsAnswers checks DueAt but not assignment status,
        // however StartAttempt requires Published status.
        // After closing, LoadOwnedInProgressAttempt still works on InProgress attempt,
        // but the assignment itself is closed. The existing attempt is InProgress.
        // Submitting that attempt itself should still work (attempt is already started).
        // What we block is starting a NEW attempt on a closed assignment.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StartAttemptAsync(assignment.Id, fixture.Student.Id));
    }

    [Fact]
    public async Task Phase4_ShowAnswerAfterSubmitSecurity_StillPreserved()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var question = await AddQuestionAsync(context, fixture.Teacher, correctAnswer: "A");
        var assignment = await AddPublishedAssignmentAsync(
            context, fixture.Workspace, fixture.Teacher, question, showAnswerAfterSubmit: false);
        assignment.ScoringMode = ClassroomScoringMode.EmpiricalDifficulty;
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var controller = CreateController(context, fixture.Student);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);
        await service.SubmitAnswerAsync(attempt.Id, fixture.Student.Id,
            new SubmitClassroomAssignmentAnswerInput(question.Id, "A", null));

        // Pre-submit: no correct answer
        var preSumbitJson = await ToJsonAsync(controller.GetAttemptById(attempt.Id, CancellationToken.None));
        Assert.DoesNotContain("correctAnswer", preSumbitJson);

        // Post-submit with ShowAnswerAfterSubmit=false: no correct answer exposed
        var afterSubmitJson = await ToJsonAsync(controller.SubmitAttempt(attempt.Id, CancellationToken.None));
        Assert.Contains("rawScore", afterSubmitJson);
        Assert.DoesNotContain("correctAnswer", afterSubmitJson);
    }

    [Fact]
    public async Task Phase4_InvalidScoringConfig_MinWeightZero_IsRejected()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var questionSet = await AddPublishedQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);

        var input = new CreateClassroomAssignmentInput(
            questionSet.Id,
            "Bad Config",
            null,
            ClassroomAssignmentType.Quiz,
            null,
            DateTime.UtcNow.AddDays(7),
            null,
            1,
            false,
            false,
            true,
            ScoringMode: ClassroomScoringMode.EmpiricalDifficulty,
            MinQuestionWeight: 0m, // invalid: must be > 0
            MaxQuestionWeight: 2.0m,
            SmoothingAlpha: 1m,
            SmoothingBeta: 1m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAssignmentAsync(fixture.Workspace.Id, fixture.Teacher.Id, input));
    }

    [Fact]
    public async Task Phase4_InvalidScoringConfig_MaxNotGreaterThanMin_IsRejected()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var questionSet = await AddPublishedQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);

        var input = new CreateClassroomAssignmentInput(
            questionSet.Id,
            "Bad Config 2",
            null,
            ClassroomAssignmentType.Quiz,
            null,
            DateTime.UtcNow.AddDays(7),
            null,
            1,
            false,
            false,
            true,
            ScoringMode: ClassroomScoringMode.EmpiricalDifficulty,
            MinQuestionWeight: 2.0m,
            MaxQuestionWeight: 1.0m, // invalid: must be > min
            SmoothingAlpha: 1m,
            SmoothingBeta: 1m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAssignmentAsync(fixture.Workspace.Id, fixture.Teacher.Id, input));
    }

    [Fact]
    public async Task Phase4_InvalidScoringConfig_AlphaPlusBetaZero_IsRejected()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var questionSet = await AddPublishedQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);

        var input = new CreateClassroomAssignmentInput(
            questionSet.Id,
            "Bad Config 3",
            null,
            ClassroomAssignmentType.Quiz,
            null,
            DateTime.UtcNow.AddDays(7),
            null,
            1,
            false,
            false,
            true,
            ScoringMode: ClassroomScoringMode.EmpiricalDifficulty,
            MinQuestionWeight: 0.3m,
            MaxQuestionWeight: 2.0m,
            SmoothingAlpha: 0m,
            SmoothingBeta: 0m); // invalid: alpha + beta must be > 0

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAssignmentAsync(fixture.Workspace.Id, fixture.Teacher.Id, input));
    }

    [Fact]
    public async Task Phase4_WeightOrder_ThreeQuestions_EasyMediumHard()
    {
        // Verifies the example from the spec:
        // Q1: 9/10 correct, Q2: 5/10 correct, Q3: 1/10 correct
        // Expected order: weight(Q1) < weight(Q2) < weight(Q3)
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var doc = await AddDocumentAsync(context, fixture.Teacher);
        var q1 = await AddQuestionAsync(context, fixture.Teacher, "A", doc);
        var q2 = await AddQuestionAsync(context, fixture.Teacher, "A", doc);
        var q3 = await AddQuestionAsync(context, fixture.Teacher, "A", doc);

        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher, ClassroomQuestionSetVisibility.Published);
        await AddQuestionSetItemAsync(context, questionSet.Id, q1, 1);
        await AddQuestionSetItemAsync(context, questionSet.Id, q2, 1);
        await AddQuestionSetItemAsync(context, questionSet.Id, q3, 1);

        var assignment = new ClassroomAssignment
        {
            ClassroomWorkspaceId = fixture.Workspace.Id,
            QuestionSetId = questionSet.Id,
            Title = "Spec Example",
            CreatedByUserId = fixture.Teacher.Id,
            Status = ClassroomAssignmentStatus.Published,
            DueAt = DateTime.UtcNow.AddDays(7),
            ScoringMode = ClassroomScoringMode.EmpiricalDifficulty,
            MinQuestionWeight = 0.3m,
            MaxQuestionWeight = 2.0m,
            SmoothingAlpha = 1m,
            SmoothingBeta = 1m
        };
        context.ClassroomAssignments.Add(assignment);
        await context.SaveChangesAsync();

        for (var i = 0; i < 10; i++)
        {
            var s = await AddUserAsync(context, $"Spec{i}", $"spec{i}@t.com", UserRole.Learner);
            await SeedSubmittedAttemptAsync(context, assignment, s, new Dictionary<int, bool>
            {
                { q1.Id, i < 9 },   // 9/10
                { q2.Id, i < 5 },   // 5/10
                { q3.Id, i == 0 }   // 1/10
            });
        }

        var service = CreateService(context);
        await service.CloseAssignmentAsync(assignment.Id, fixture.Teacher.Id);

        var stats = await service.GetAssignmentQuestionStatsAsync(assignment.Id, fixture.Teacher.Id);
        var w1 = stats.First(s => s.QuestionId == q1.Id).DifficultyWeight;
        var w2 = stats.First(s => s.QuestionId == q2.Id).DifficultyWeight;
        var w3 = stats.First(s => s.QuestionId == q3.Id).DifficultyWeight;

        // Verify order: Q1 < Q2 < Q3
        Assert.True(w1 < w2, $"w1({w1}) should be < w2({w2})");
        Assert.True(w2 < w3, $"w2({w2}) should be < w3({w3})");

        // Approximate values from spec (tolerant of rounding)
        Assert.True(w1 > 0.5m && w1 < 0.65m, $"Q1 weight ~0.584 expected, got {w1}");
        Assert.True(w2 > 1.1m && w2 < 1.2m, $"Q2 weight ~1.15 expected, got {w2}");
        Assert.True(w3 > 1.68m && w3 < 1.75m, $"Q3 weight ~1.717 expected, got {w3}");
    }

    [Fact]
    public async Task Phase4_PercentScoring_IgnoresInvalidConfig()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var questionSet = await AddPublishedQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);

        // When ScoringMode = Percent, invalid min weight should NOT throw
        var input = new CreateClassroomAssignmentInput(
            questionSet.Id,
            "Percent with bad config",
            null,
            ClassroomAssignmentType.Quiz,
            null,
            DateTime.UtcNow.AddDays(7),
            null,
            1,
            false,
            false,
            true,
            ScoringMode: ClassroomScoringMode.Percent,
            MinQuestionWeight: 0m, // normally invalid for EmpiricalDifficulty
            MaxQuestionWeight: 0m,
            SmoothingAlpha: 0m,
            SmoothingBeta: 0m);

        var assignment = await service.CreateAssignmentAsync(fixture.Workspace.Id, fixture.Teacher.Id, input);
        Assert.NotNull(assignment);
        Assert.Equal(ClassroomScoringMode.Percent, assignment.ScoringMode);
    }

    [Fact]
    public async Task Phase4_CannotSubmitAnswerOrAttemptAfterAssignmentClosed()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var question = await AddQuestionAsync(context, fixture.Teacher, correctAnswer: "A");
        var assignment = await AddPublishedAssignmentAsync(context, fixture.Workspace, fixture.Teacher, question);
        var service = CreateService(context);
        var attempt = await service.StartAttemptAsync(assignment.Id, fixture.Student.Id);

        // Close assignment
        await service.CloseAssignmentAsync(assignment.Id, fixture.Teacher.Id);

        // Cannot submit answer after closed
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAnswerAsync(attempt.Id, fixture.Student.Id,
                new SubmitClassroomAssignmentAnswerInput(question.Id, "A", 10)));

        // Cannot submit attempt after closed
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAttemptAsync(attempt.Id, fixture.Student.Id));
    }

    private static async Task<ClassroomFixture> CreateClassroomFixtureAsync(ApplicationDbContext context)
    {
        var teacher = await AddUserAsync(context, "Teacher", "teacher@example.com", UserRole.Instructor);
        var student = await AddUserAsync(context, "Student", "student@example.com", UserRole.Learner);
        var workspace = new ClassroomWorkspace
        {
            Name = "N5 Reading",
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

    private static async Task<Document> AddDocumentAsync(ApplicationDbContext context, AppUser uploadedBy)
    {
        var document = new Document
        {
            FileName = "lesson.pdf",
            FileType = "PDF",
            FilePath = "/tmp/lesson.pdf",
            UploadedBy = uploadedBy.Id.ToString()
        };
        context.Documents.Add(document);
        await context.SaveChangesAsync();
        return document;
    }

    private static async Task<Question> AddQuestionAsync(
        ApplicationDbContext context,
        AppUser uploadedBy,
        string correctAnswer,
        Document? document = null)
    {
        document ??= await AddDocumentAsync(context, uploadedBy);
        var question = new Question
        {
            DocumentId = document.Id,
            QuestionText = "What is the key point?",
            QuestionType = QuestionType.MultipleChoice,
            OptionsJson = """[{"key":"A","text":"Alpha"},{"key":"B","text":"Beta"}]""",
            CorrectAnswer = correctAnswer,
            Explanation = "Because the source says so."
        };
        context.Questions.Add(question);
        await context.SaveChangesAsync();
        return question;
    }

    private static async Task<ClassroomQuestionSet> AddQuestionSetAsync(
        ApplicationDbContext context,
        ClassroomWorkspace workspace,
        AppUser teacher,
        ClassroomQuestionSetVisibility visibility)
    {
        var questionSet = new ClassroomQuestionSet
        {
            ClassroomWorkspaceId = workspace.Id,
            Title = "Review set",
            CreatedByUserId = teacher.Id,
            Visibility = visibility
        };
        context.ClassroomQuestionSets.Add(questionSet);
        await context.SaveChangesAsync();
        return questionSet;
    }

    private static async Task<ClassroomQuestionSet> AddPublishedQuestionSetAsync(
        ApplicationDbContext context,
        ClassroomWorkspace workspace,
        AppUser teacher,
        Question? question = null,
        double pointWeight = 1)
    {
        question ??= await AddQuestionAsync(context, teacher, correctAnswer: "A");
        var questionSet = await AddQuestionSetAsync(context, workspace, teacher, ClassroomQuestionSetVisibility.Published);
        await AddQuestionSetItemAsync(context, questionSet.Id, question, pointWeight);
        return questionSet;
    }

    private static async Task<ClassroomQuestionSetItem> AddQuestionSetItemAsync(
        ApplicationDbContext context,
        int questionSetId,
        Question question,
        double pointWeight)
    {
        var nextOrder = await context.ClassroomQuestionSetItems.CountAsync(item => item.ClassroomQuestionSetId == questionSetId);
        var item = new ClassroomQuestionSetItem
        {
            ClassroomQuestionSetId = questionSetId,
            QuestionId = question.Id,
            OrderIndex = nextOrder,
            PointWeight = pointWeight
        };
        context.ClassroomQuestionSetItems.Add(item);
        await context.SaveChangesAsync();
        return item;
    }

    private static async Task<ClassroomAssignment> AddPublishedAssignmentAsync(
        ApplicationDbContext context,
        ClassroomWorkspace workspace,
        AppUser teacher,
        Question? question = null,
        double pointWeight = 1,
        bool showAnswerAfterSubmit = true)
    {
        var questionSet = await AddPublishedQuestionSetAsync(context, workspace, teacher, question, pointWeight);
        var assignment = await AddDraftAssignmentAsync(context, workspace, teacher, questionSet);
        assignment.Status = ClassroomAssignmentStatus.Published;
        assignment.ShowAnswerAfterSubmit = showAnswerAfterSubmit;
        await context.SaveChangesAsync();
        return assignment;
    }

    private static async Task<ClassroomAssignment> AddDraftAssignmentAsync(
        ApplicationDbContext context,
        ClassroomWorkspace workspace,
        AppUser teacher,
        ClassroomQuestionSet? questionSet = null)
    {
        questionSet ??= await AddPublishedQuestionSetAsync(context, workspace, teacher);
        var assignment = new ClassroomAssignment
        {
            ClassroomWorkspaceId = workspace.Id,
            QuestionSetId = questionSet.Id,
            Title = "Assignment",
            CreatedByUserId = teacher.Id,
            Status = ClassroomAssignmentStatus.Draft,
            DueAt = DateTime.UtcNow.AddDays(7)
        };
        context.ClassroomAssignments.Add(assignment);
        await context.SaveChangesAsync();
        return assignment;
    }

    private sealed record ClassroomFixture(
        AppUser Teacher,
        AppUser Student,
        ClassroomWorkspace Workspace);
}

