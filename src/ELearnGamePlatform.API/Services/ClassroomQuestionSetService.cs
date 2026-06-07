using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.API.Services;

public sealed class ClassroomQuestionSetService : IClassroomQuestionSetService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IClassroomPermissionService _permissionService;

    public ClassroomQuestionSetService(
        ApplicationDbContext dbContext,
        IClassroomPermissionService permissionService)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
    }

    public async Task<ClassroomQuestionSet> CreateQuestionSetAsync(
        int classroomWorkspaceId,
        int actorUserId,
        CreateClassroomQuestionSetInput input,
        CancellationToken cancellationToken = default)
    {
        await EnsureClassroomExistsAsync(classroomWorkspaceId, cancellationToken);
        await EnsureCanManageAsync(classroomWorkspaceId, actorUserId, cancellationToken);
        await EnsureOwnedDocumentAsync(input.DocumentId, actorUserId, cancellationToken);

        var now = DateTime.UtcNow;
        var questionSet = new ClassroomQuestionSet
        {
            ClassroomWorkspaceId = classroomWorkspaceId,
            DocumentId = input.DocumentId,
            Title = NormalizeRequired(input.Title, "Question set title is required."),
            Description = NormalizeOptional(input.Description),
            CreatedByUserId = actorUserId,
            Visibility = ClassroomQuestionSetVisibility.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.ClassroomQuestionSets.Add(questionSet);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return questionSet;
    }

    public async Task<IReadOnlyList<ClassroomQuestionSet>> GetQuestionSetsForClassroomAsync(
        int classroomWorkspaceId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureClassroomExistsAsync(classroomWorkspaceId, cancellationToken);
        var canManage = await _permissionService.CanManageClassroomAsync(classroomWorkspaceId, actorUserId, cancellationToken);
        if (!canManage && !await _permissionService.CanViewClassroomAsync(classroomWorkspaceId, actorUserId, cancellationToken))
        {
            throw new UnauthorizedAccessException("User cannot view question sets for this classroom.");
        }

        return await _dbContext.ClassroomQuestionSets
            .Include(questionSet => questionSet.Items)
            .Where(questionSet =>
                questionSet.ClassroomWorkspaceId == classroomWorkspaceId
                && (canManage || questionSet.Visibility == ClassroomQuestionSetVisibility.Published))
            .OrderByDescending(questionSet => questionSet.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Question>> GetAvailableQuestionsAsync(
        int classroomWorkspaceId,
        int actorUserId,
        int? documentId,
        CancellationToken cancellationToken = default)
    {
        await EnsureClassroomExistsAsync(classroomWorkspaceId, cancellationToken);
        await EnsureCanManageAsync(classroomWorkspaceId, actorUserId, cancellationToken);
        await EnsureOwnedDocumentAsync(documentId, actorUserId, cancellationToken);

        var ownerUserId = actorUserId.ToString();
        var query = _dbContext.Questions
            .Include(question => question.Document)
            .Where(question =>
                !question.IsArchived
                && question.Document != null
                && question.Document.UploadedBy == ownerUserId);

        if (documentId.HasValue)
        {
            query = query.Where(question => question.DocumentId == documentId.Value);
        }

        return await query
            .OrderByDescending(question => question.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ClassroomQuestionSet?> GetQuestionSetDetailAsync(
        int questionSetId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var questionSet = await LoadQuestionSetAsync(questionSetId, cancellationToken);
        if (questionSet == null)
        {
            return null;
        }

        var canManage = await _permissionService.CanManageClassroomAsync(
            questionSet.ClassroomWorkspaceId,
            actorUserId,
            cancellationToken);
        if (canManage)
        {
            return questionSet;
        }

        var canViewClassroom = await _permissionService.CanViewClassroomAsync(
            questionSet.ClassroomWorkspaceId,
            actorUserId,
            cancellationToken);
        if (canViewClassroom && questionSet.Visibility == ClassroomQuestionSetVisibility.Published)
        {
            return questionSet;
        }

        return null;
    }

    public async Task<ClassroomQuestionSet> UpdateQuestionSetAsync(
        int questionSetId,
        int actorUserId,
        UpdateClassroomQuestionSetInput input,
        CancellationToken cancellationToken = default)
    {
        var questionSet = await LoadManagedQuestionSetAsync(questionSetId, actorUserId, cancellationToken);
        await EnsureOwnedDocumentAsync(input.DocumentId, actorUserId, cancellationToken);
        EnsureQuestionSetItemsMatchDocument(questionSet, input.DocumentId);

        questionSet.Title = NormalizeRequired(input.Title, "Question set title is required.");
        questionSet.Description = NormalizeOptional(input.Description);
        questionSet.DocumentId = input.DocumentId;
        questionSet.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return questionSet;
    }

    public async Task DeleteQuestionSetAsync(
        int questionSetId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var questionSet = await LoadManagedQuestionSetAsync(questionSetId, actorUserId, cancellationToken);
        _dbContext.ClassroomQuestionSets.Remove(questionSet);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ClassroomQuestionSetItem> AddQuestionToSetAsync(
        int questionSetId,
        int actorUserId,
        AddClassroomQuestionSetItemInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.PointWeight <= 0)
        {
            throw new InvalidOperationException("Point weight must be greater than zero.");
        }

        var questionSet = await LoadManagedQuestionSetAsync(questionSetId, actorUserId, cancellationToken);

        var question = await LoadOwnedQuestionAsync(input.QuestionId, actorUserId, cancellationToken);
        EnsureQuestionMatchesQuestionSetDocument(questionSet, question);

        var duplicate = questionSet.Items.Any(item => item.QuestionId == input.QuestionId);
        if (duplicate)
        {
            throw new InvalidOperationException("Question already exists in this question set.");
        }

        var orderIndex = input.OrderIndex ?? (questionSet.Items.Count == 0 ? 0 : questionSet.Items.Max(item => item.OrderIndex) + 1);
        var item = new ClassroomQuestionSetItem
        {
            ClassroomQuestionSetId = questionSet.Id,
            QuestionId = question.Id,
            OrderIndex = orderIndex,
            PointWeight = input.PointWeight,
            SectionCode = NormalizeOptional(input.SectionCode),
            CreatedAt = DateTime.UtcNow
        };

        questionSet.Items.Add(item);
        questionSet.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<bool> RemoveQuestionFromSetAsync(
        int questionSetId,
        int itemId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var questionSet = await LoadManagedQuestionSetAsync(questionSetId, actorUserId, cancellationToken);
        var item = questionSet.Items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item == null)
        {
            return false;
        }

        _dbContext.ClassroomQuestionSetItems.Remove(item);
        questionSet.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ClassroomQuestionSet> ReorderQuestionSetItemsAsync(
        int questionSetId,
        int actorUserId,
        IReadOnlyList<ReorderClassroomQuestionSetItemInput> items,
        CancellationToken cancellationToken = default)
    {
        var questionSet = await LoadManagedQuestionSetAsync(questionSetId, actorUserId, cancellationToken);
        var existingById = questionSet.Items.ToDictionary(item => item.Id);

        foreach (var input in items)
        {
            if (!existingById.TryGetValue(input.ItemId, out var item))
            {
                throw new InvalidOperationException("Question set item was not found.");
            }

            item.OrderIndex = input.OrderIndex;
        }

        questionSet.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return questionSet;
    }

    public async Task<ClassroomQuestionSet> PublishQuestionSetAsync(
        int questionSetId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var questionSet = await LoadManagedQuestionSetAsync(questionSetId, actorUserId, cancellationToken);
        if (questionSet.Items.Count == 0)
        {
            throw new InvalidOperationException("Question set must contain at least one question before publishing.");
        }

        questionSet.Visibility = ClassroomQuestionSetVisibility.Published;
        questionSet.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return questionSet;
    }

    public async Task<ClassroomQuestionSet> UnpublishQuestionSetAsync(
        int questionSetId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var questionSet = await LoadManagedQuestionSetAsync(questionSetId, actorUserId, cancellationToken);
        questionSet.Visibility = ClassroomQuestionSetVisibility.Draft;
        questionSet.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return questionSet;
    }

    private async Task<ClassroomQuestionSet> LoadManagedQuestionSetAsync(
        int questionSetId,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var questionSet = await LoadQuestionSetAsync(questionSetId, cancellationToken)
            ?? throw new InvalidOperationException("Question set was not found.");

        await EnsureCanManageAsync(questionSet.ClassroomWorkspaceId, actorUserId, cancellationToken);
        return questionSet;
    }

    private async Task<ClassroomQuestionSet?> LoadQuestionSetAsync(
        int questionSetId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ClassroomQuestionSets
            .Include(questionSet => questionSet.Items)
                .ThenInclude(item => item.Question)
            .Include(questionSet => questionSet.Document)
            .FirstOrDefaultAsync(questionSet => questionSet.Id == questionSetId, cancellationToken);
    }

    private async Task EnsureCanManageAsync(
        int classroomWorkspaceId,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        if (!await _permissionService.CanManageClassroomAsync(classroomWorkspaceId, actorUserId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Only classroom teachers can manage question sets.");
        }
    }

    private async Task EnsureClassroomExistsAsync(
        int classroomWorkspaceId,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.ClassroomWorkspaces.AnyAsync(
            workspace => workspace.Id == classroomWorkspaceId && !workspace.IsArchived,
            cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("Classroom workspace was not found.");
        }
    }

    private async Task<Document?> EnsureOwnedDocumentAsync(
        int? documentId,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        if (!documentId.HasValue)
        {
            return null;
        }

        var document = await _dbContext.Documents.FirstOrDefaultAsync(
            candidate => candidate.Id == documentId.Value,
            cancellationToken);
        if (document == null)
        {
            throw new InvalidOperationException("Document was not found.");
        }

        if (!IsOwnedBy(document, actorUserId))
        {
            throw new UnauthorizedAccessException("Document is not available to this classroom teacher.");
        }

        return document;
    }

    private async Task<Question> LoadOwnedQuestionAsync(
        int questionId,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var question = await _dbContext.Questions
            .Include(candidate => candidate.Document)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == questionId && !candidate.IsArchived,
                cancellationToken);
        if (question == null)
        {
            throw new InvalidOperationException("Question was not found.");
        }

        if (question.Document == null)
        {
            throw new InvalidOperationException("Question document was not found.");
        }

        if (!IsOwnedBy(question.Document, actorUserId))
        {
            throw new UnauthorizedAccessException("Question is not available to this classroom teacher.");
        }

        return question;
    }

    private static void EnsureQuestionMatchesQuestionSetDocument(
        ClassroomQuestionSet questionSet,
        Question question)
    {
        if (questionSet.DocumentId.HasValue && question.DocumentId != questionSet.DocumentId.Value)
        {
            throw new InvalidOperationException("Question must belong to the question set document.");
        }
    }

    private static void EnsureQuestionSetItemsMatchDocument(
        ClassroomQuestionSet questionSet,
        int? documentId)
    {
        if (!documentId.HasValue)
        {
            return;
        }

        var hasDifferentDocument = questionSet.Items.Any(
            item => item.Question != null && item.Question.DocumentId != documentId.Value);
        if (hasDifferentDocument)
        {
            throw new InvalidOperationException("Question set contains questions from a different document.");
        }
    }

    private static bool IsOwnedBy(Document document, int actorUserId)
    {
        return string.Equals(document.UploadedBy?.Trim(), actorUserId.ToString(), StringComparison.Ordinal);
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
