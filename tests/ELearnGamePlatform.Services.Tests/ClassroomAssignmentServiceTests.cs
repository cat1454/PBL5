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
