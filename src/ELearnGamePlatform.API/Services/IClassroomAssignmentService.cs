using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;

namespace ELearnGamePlatform.API.Services;

public interface IClassroomAssignmentService
{
    Task<ClassroomAssignment> CreateAssignmentAsync(
        int classroomWorkspaceId,
        int actorUserId,
        CreateClassroomAssignmentInput input,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClassroomAssignment>> GetAssignmentsForClassroomAsync(
        int classroomWorkspaceId,
        int actorUserId,
        bool studentViewOnly = false,
        CancellationToken cancellationToken = default);

    Task<ClassroomAssignment?> GetAssignmentDetailAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<ClassroomAssignment> UpdateAssignmentAsync(
        int assignmentId,
        int actorUserId,
        UpdateClassroomAssignmentInput input,
        CancellationToken cancellationToken = default);

    Task DeleteAssignmentAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<ClassroomAssignment> PublishAssignmentAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<ClassroomAssignment> CloseAssignmentAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<ClassroomAssignmentAttempt> StartAttemptAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<ClassroomAssignmentAnswer> SubmitAnswerAsync(
        int attemptId,
        int actorUserId,
        SubmitClassroomAssignmentAnswerInput input,
        CancellationToken cancellationToken = default);

    Task<ClassroomAssignmentAttempt> SubmitAttemptAsync(
        int attemptId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClassroomAssignmentAttempt>> GetMyAttemptsAsync(
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClassroomAssignmentAttempt>> GetAssignmentAttemptsForTeacherAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<ClassroomAssignmentAttempt?> GetAttemptDetailAsync(
        int attemptId,
        int actorUserId,
        CancellationToken cancellationToken = default);
}

public sealed record CreateClassroomAssignmentInput(
    int QuestionSetId,
    string Title,
    string? Description,
    ClassroomAssignmentType Type,
    DateTime? StartAt,
    DateTime? DueAt,
    int? TimeLimitMinutes,
    int AttemptLimit,
    bool ShuffleQuestions,
    bool ShuffleOptions,
    bool ShowAnswerAfterSubmit);

public sealed record UpdateClassroomAssignmentInput(
    string Title,
    string? Description,
    ClassroomAssignmentType Type,
    DateTime? StartAt,
    DateTime? DueAt,
    int? TimeLimitMinutes,
    int AttemptLimit,
    bool ShuffleQuestions,
    bool ShuffleOptions,
    bool ShowAnswerAfterSubmit);

public sealed record SubmitClassroomAssignmentAnswerInput(
    int QuestionId,
    string? SelectedAnswer,
    int? TimeSpentSeconds);
