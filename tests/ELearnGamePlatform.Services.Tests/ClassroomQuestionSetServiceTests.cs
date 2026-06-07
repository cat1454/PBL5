using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class ClassroomQuestionSetServiceTests
{
    [Fact]
    public async Task TeacherCanCreateQuestionSet()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var service = CreateService(context);

        var questionSet = await service.CreateQuestionSetAsync(
            fixture.Workspace.Id,
            fixture.Teacher.Id,
            new CreateClassroomQuestionSetInput("Midterm review", "Core concepts", null));

        Assert.Equal(fixture.Workspace.Id, questionSet.ClassroomWorkspaceId);
        Assert.Equal(fixture.Teacher.Id, questionSet.CreatedByUserId);
        Assert.Equal(ClassroomQuestionSetVisibility.Draft, questionSet.Visibility);
    }

    [Fact]
    public async Task StudentCannotCreateQuestionSet()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var service = CreateService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateQuestionSetAsync(
                fixture.Workspace.Id,
                fixture.Student.Id,
                new CreateClassroomQuestionSetInput("Student set", null, null)));
    }

    [Fact]
    public async Task TeacherCannotCreateQuestionSetWithDocumentOwnedByAnotherUser()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var otherTeacher = await AddUserAsync(context, "Other Teacher", "other.teacher@example.com", UserRole.Instructor);
        var otherDocument = await AddDocumentAsync(context, otherTeacher);
        var service = CreateService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateQuestionSetAsync(
                fixture.Workspace.Id,
                fixture.Teacher.Id,
                new CreateClassroomQuestionSetInput("Borrowed doc", null, otherDocument.Id)));
    }

    [Fact]
    public async Task NonMemberCannotViewQuestionSetDetail()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var nonMember = await AddUserAsync(context, "Non Member", "nonmember@example.com", UserRole.Learner);
        var questionSet = await AddQuestionSetAsync(
            context,
            fixture.Workspace,
            fixture.Teacher,
            visibility: ClassroomQuestionSetVisibility.Published);
        var service = CreateService(context);

        var detail = await service.GetQuestionSetDetailAsync(questionSet.Id, nonMember.Id);

        Assert.Null(detail);
    }

    [Fact]
    public async Task TeacherCanAddExistingQuestionToQuestionSet()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        var question = await AddQuestionAsync(context, fixture.Teacher);
        var service = CreateService(context);

        var item = await service.AddQuestionToSetAsync(
            questionSet.Id,
            fixture.Teacher.Id,
            new AddClassroomQuestionSetItemInput(question.Id, null, 2, "Knowledge"));

        Assert.Equal(question.Id, item.QuestionId);
        Assert.Equal(0, item.OrderIndex);
        Assert.Equal(2, item.PointWeight);
        Assert.Equal("Knowledge", item.SectionCode);
    }

    [Fact]
    public async Task TeacherCanAddQuestionFromOwnDocument()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var document = await AddDocumentAsync(context, fixture.Teacher);
        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher, document.Id);
        var question = await AddQuestionAsync(context, fixture.Teacher, document);
        var service = CreateService(context);

        var item = await service.AddQuestionToSetAsync(
            questionSet.Id,
            fixture.Teacher.Id,
            new AddClassroomQuestionSetItemInput(question.Id, null, 1, null));

        Assert.Equal(question.Id, item.QuestionId);
    }

    [Fact]
    public async Task TeacherCannotAddQuestionFromAnotherUsersDocument()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var otherTeacher = await AddUserAsync(context, "Other Teacher", "other.teacher@example.com", UserRole.Instructor);
        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        var question = await AddQuestionAsync(context, otherTeacher);
        var service = CreateService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.AddQuestionToSetAsync(
                questionSet.Id,
                fixture.Teacher.Id,
                new AddClassroomQuestionSetItemInput(question.Id, null, 1, null)));
    }

    [Fact]
    public async Task QuestionSetWithDocumentIdCannotAddQuestionFromDifferentDocument()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var document = await AddDocumentAsync(context, fixture.Teacher);
        var otherOwnDocument = await AddDocumentAsync(context, fixture.Teacher);
        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher, document.Id);
        var question = await AddQuestionAsync(context, fixture.Teacher, otherOwnDocument);
        var service = CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddQuestionToSetAsync(
                questionSet.Id,
                fixture.Teacher.Id,
                new AddClassroomQuestionSetItemInput(question.Id, null, 1, null)));
    }

    [Fact]
    public async Task StudentCannotAddQuestionToQuestionSet()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        var question = await AddQuestionAsync(context, fixture.Teacher);
        var service = CreateService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.AddQuestionToSetAsync(
                questionSet.Id,
                fixture.Student.Id,
                new AddClassroomQuestionSetItemInput(question.Id, null, 1, null)));
    }

    [Fact]
    public async Task DuplicateQuestionCannotBeAddedTwice()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        var question = await AddQuestionAsync(context, fixture.Teacher);
        var service = CreateService(context);

        await service.AddQuestionToSetAsync(
            questionSet.Id,
            fixture.Teacher.Id,
            new AddClassroomQuestionSetItemInput(question.Id, null, 1, null));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddQuestionToSetAsync(
                questionSet.Id,
                fixture.Teacher.Id,
                new AddClassroomQuestionSetItemInput(question.Id, null, 1, null)));
    }

    [Fact]
    public async Task PublishChangesVisibility()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        var question = await AddQuestionAsync(context, fixture.Teacher);
        var service = CreateService(context);
        await service.AddQuestionToSetAsync(
            questionSet.Id,
            fixture.Teacher.Id,
            new AddClassroomQuestionSetItemInput(question.Id, null, 1, null));

        var published = await service.PublishQuestionSetAsync(questionSet.Id, fixture.Teacher.Id);

        Assert.Equal(ClassroomQuestionSetVisibility.Published, published.Visibility);
    }

    [Fact]
    public async Task CannotPublishEmptyQuestionSet()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        var service = CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PublishQuestionSetAsync(questionSet.Id, fixture.Teacher.Id));
    }

    [Fact]
    public async Task DeletingQuestionSetDeletesItems()
    {
        await using var context = CreateContext();
        var fixture = await CreateClassroomFixtureAsync(context);
        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        var question = await AddQuestionAsync(context, fixture.Teacher);
        var service = CreateService(context);
        await service.AddQuestionToSetAsync(
            questionSet.Id,
            fixture.Teacher.Id,
            new AddClassroomQuestionSetItemInput(question.Id, null, 1, null));

        await service.DeleteQuestionSetAsync(questionSet.Id, fixture.Teacher.Id);

        Assert.False(await context.ClassroomQuestionSets.AnyAsync());
        Assert.False(await context.ClassroomQuestionSetItems.AnyAsync());
    }

    [Fact]
    public async Task TeacherFromAnotherClassroomCannotModifyQuestionSet()
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
        context.ClassroomMembers.Add(new ClassroomMember
        {
            ClassroomWorkspace = otherWorkspace,
            UserId = otherTeacher.Id,
            Role = ClassroomRole.Teacher,
            Status = ClassroomMemberStatus.Active
        });
        var questionSet = await AddQuestionSetAsync(context, fixture.Workspace, fixture.Teacher);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateQuestionSetAsync(
                questionSet.Id,
                otherTeacher.Id,
                new UpdateClassroomQuestionSetInput("Hijack", null, null)));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ClassroomQuestionSetService CreateService(ApplicationDbContext context)
    {
        return new ClassroomQuestionSetService(context, new ClassroomPermissionService(context));
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
        Document? document = null)
    {
        document ??= await AddDocumentAsync(context, uploadedBy);
        var question = new Question
        {
            DocumentId = document.Id,
            QuestionText = "What is the key point?",
            QuestionType = QuestionType.MultipleChoice
        };
        context.Questions.Add(question);
        await context.SaveChangesAsync();
        return question;
    }

    private static async Task<ClassroomQuestionSet> AddQuestionSetAsync(
        ApplicationDbContext context,
        ClassroomWorkspace workspace,
        AppUser teacher,
        int? documentId = null,
        ClassroomQuestionSetVisibility visibility = ClassroomQuestionSetVisibility.Draft)
    {
        var questionSet = new ClassroomQuestionSet
        {
            ClassroomWorkspaceId = workspace.Id,
            DocumentId = documentId,
            Title = "Review set",
            CreatedByUserId = teacher.Id,
            Visibility = visibility
        };
        context.ClassroomQuestionSets.Add(questionSet);
        await context.SaveChangesAsync();
        return questionSet;
    }

    private sealed record ClassroomFixture(
        AppUser Teacher,
        AppUser Student,
        ClassroomWorkspace Workspace);
}
