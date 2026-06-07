using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.API.Services;

public interface IClassroomQuestionSetService
{
    Task<ClassroomQuestionSet> CreateQuestionSetAsync(
        int classroomWorkspaceId,
        int actorUserId,
        CreateClassroomQuestionSetInput input,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClassroomQuestionSet>> GetQuestionSetsForClassroomAsync(
        int classroomWorkspaceId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Question>> GetAvailableQuestionsAsync(
        int classroomWorkspaceId,
        int actorUserId,
        int? documentId,
        CancellationToken cancellationToken = default);

    Task<ClassroomQuestionSet?> GetQuestionSetDetailAsync(
        int questionSetId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<ClassroomQuestionSet> UpdateQuestionSetAsync(
        int questionSetId,
        int actorUserId,
        UpdateClassroomQuestionSetInput input,
        CancellationToken cancellationToken = default);

    Task DeleteQuestionSetAsync(
        int questionSetId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<ClassroomQuestionSetItem> AddQuestionToSetAsync(
        int questionSetId,
        int actorUserId,
        AddClassroomQuestionSetItemInput input,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveQuestionFromSetAsync(
        int questionSetId,
        int itemId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<ClassroomQuestionSet> ReorderQuestionSetItemsAsync(
        int questionSetId,
        int actorUserId,
        IReadOnlyList<ReorderClassroomQuestionSetItemInput> items,
        CancellationToken cancellationToken = default);

    Task<ClassroomQuestionSet> PublishQuestionSetAsync(
        int questionSetId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<ClassroomQuestionSet> UnpublishQuestionSetAsync(
        int questionSetId,
        int actorUserId,
        CancellationToken cancellationToken = default);
}

public sealed record CreateClassroomQuestionSetInput(
    string Title,
    string? Description,
    int? DocumentId);

public sealed record UpdateClassroomQuestionSetInput(
    string Title,
    string? Description,
    int? DocumentId);

public sealed record AddClassroomQuestionSetItemInput(
    int QuestionId,
    int? OrderIndex,
    double PointWeight,
    string? SectionCode);

public sealed record ReorderClassroomQuestionSetItemInput(
    int ItemId,
    int OrderIndex);
