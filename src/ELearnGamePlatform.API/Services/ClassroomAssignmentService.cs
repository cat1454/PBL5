using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.API.Services;

public sealed class ClassroomAssignmentService : IClassroomAssignmentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IClassroomPermissionService _permissionService;

    public ClassroomAssignmentService(
        ApplicationDbContext dbContext,
        IClassroomPermissionService permissionService)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
    }

    public async Task<ClassroomAssignment> CreateAssignmentAsync(
        int classroomWorkspaceId,
        int actorUserId,
        CreateClassroomAssignmentInput input,
        CancellationToken cancellationToken = default)
    {
        await EnsureClassroomExistsAsync(classroomWorkspaceId, cancellationToken);
        await EnsureCanManageAsync(classroomWorkspaceId, actorUserId, cancellationToken);
        ValidateAssignmentInput(input.Title, input.AttemptLimit, input.TimeLimitMinutes, input.StartAt, input.DueAt);

        var questionSet = await LoadQuestionSetAsync(input.QuestionSetId, cancellationToken)
            ?? throw new InvalidOperationException("Question set was not found.");
        EnsureQuestionSetCanBackAssignment(questionSet, classroomWorkspaceId);

        var now = DateTime.UtcNow;
        var assignment = new ClassroomAssignment
        {
            ClassroomWorkspaceId = classroomWorkspaceId,
            QuestionSetId = questionSet.Id,
            Title = NormalizeRequired(input.Title, "Assignment title is required."),
            Description = NormalizeOptional(input.Description),
            Type = input.Type,
            Status = ClassroomAssignmentStatus.Draft,
            StartAt = input.StartAt,
            DueAt = input.DueAt,
            TimeLimitMinutes = input.TimeLimitMinutes,
            AttemptLimit = input.AttemptLimit,
            ShuffleQuestions = input.ShuffleQuestions,
            ShuffleOptions = input.ShuffleOptions,
            ShowAnswerAfterSubmit = input.ShowAnswerAfterSubmit,
            CreatedByUserId = actorUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.ClassroomAssignments.Add(assignment);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await LoadAssignmentAsync(assignment.Id, cancellationToken) ?? assignment;
    }

    public async Task<IReadOnlyList<ClassroomAssignment>> GetAssignmentsForClassroomAsync(
        int classroomWorkspaceId,
        int actorUserId,
        bool studentViewOnly = false,
        CancellationToken cancellationToken = default)
    {
        await EnsureClassroomExistsAsync(classroomWorkspaceId, cancellationToken);
        var canManage = await _permissionService.CanManageClassroomAsync(classroomWorkspaceId, actorUserId, cancellationToken);
        if (!canManage && !await _permissionService.IsStudentAsync(classroomWorkspaceId, actorUserId, cancellationToken))
        {
            throw new UnauthorizedAccessException("User cannot view assignments for this classroom.");
        }

        var publishedOnly = studentViewOnly || !canManage;
        return await _dbContext.ClassroomAssignments
            .Include(assignment => assignment.QuestionSet)
                .ThenInclude(questionSet => questionSet!.Items)
            .Where(assignment =>
                assignment.ClassroomWorkspaceId == classroomWorkspaceId
                && (!publishedOnly || assignment.Status == ClassroomAssignmentStatus.Published))
            .OrderByDescending(assignment => assignment.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ClassroomAssignment?> GetAssignmentDetailAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await LoadAssignmentAsync(assignmentId, cancellationToken);
        if (assignment == null)
        {
            return null;
        }

        var canManage = await _permissionService.CanManageClassroomAsync(
            assignment.ClassroomWorkspaceId,
            actorUserId,
            cancellationToken);
        if (canManage)
        {
            return assignment;
        }

        var isStudent = await _permissionService.IsStudentAsync(
            assignment.ClassroomWorkspaceId,
            actorUserId,
            cancellationToken);
        return isStudent && assignment.Status == ClassroomAssignmentStatus.Published ? assignment : null;
    }

    public async Task<ClassroomAssignment> UpdateAssignmentAsync(
        int assignmentId,
        int actorUserId,
        UpdateClassroomAssignmentInput input,
        CancellationToken cancellationToken = default)
    {
        var assignment = await LoadManagedAssignmentAsync(assignmentId, actorUserId, cancellationToken);
        ValidateAssignmentInput(input.Title, input.AttemptLimit, input.TimeLimitMinutes, input.StartAt, input.DueAt);

        assignment.Title = NormalizeRequired(input.Title, "Assignment title is required.");
        assignment.Description = NormalizeOptional(input.Description);
        assignment.Type = input.Type;
        assignment.StartAt = input.StartAt;
        assignment.DueAt = input.DueAt;
        assignment.TimeLimitMinutes = input.TimeLimitMinutes;
        assignment.AttemptLimit = input.AttemptLimit;
        assignment.ShuffleQuestions = input.ShuffleQuestions;
        assignment.ShuffleOptions = input.ShuffleOptions;
        assignment.ShowAnswerAfterSubmit = input.ShowAnswerAfterSubmit;
        assignment.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return assignment;
    }

    public async Task DeleteAssignmentAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await LoadManagedAssignmentAsync(assignmentId, actorUserId, cancellationToken);
        _dbContext.ClassroomAssignments.Remove(assignment);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ClassroomAssignment> PublishAssignmentAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await LoadManagedAssignmentAsync(assignmentId, actorUserId, cancellationToken);
        if (assignment.QuestionSet == null)
        {
            throw new InvalidOperationException("Assignment question set was not found.");
        }

        EnsureQuestionSetCanBackAssignment(assignment.QuestionSet, assignment.ClassroomWorkspaceId);
        assignment.Status = ClassroomAssignmentStatus.Published;
        assignment.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return assignment;
    }

    public async Task<ClassroomAssignment> CloseAssignmentAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await LoadManagedAssignmentAsync(assignmentId, actorUserId, cancellationToken);
        assignment.Status = ClassroomAssignmentStatus.Closed;
        assignment.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return assignment;
    }

    public async Task<ClassroomAssignmentAttempt> StartAttemptAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await LoadAssignmentAsync(assignmentId, cancellationToken)
            ?? throw new InvalidOperationException("Assignment was not found.");
        await EnsureActiveStudentAsync(assignment.ClassroomWorkspaceId, actorUserId, cancellationToken);
        EnsureAttemptCanStart(assignment);

        var inProgress = await _dbContext.ClassroomAssignmentAttempts
            .Include(attempt => attempt.Assignment)
                .ThenInclude(candidate => candidate!.QuestionSet)
                    .ThenInclude(questionSet => questionSet!.Items)
                        .ThenInclude(item => item.Question)
            .Include(attempt => attempt.Answers)
                .ThenInclude(answer => answer.Question)
            .FirstOrDefaultAsync(
                attempt =>
                    attempt.ClassroomAssignmentId == assignment.Id
                    && attempt.UserId == actorUserId
                    && attempt.Status == ClassroomAttemptStatus.InProgress,
                cancellationToken);
        if (inProgress != null)
        {
            return inProgress;
        }

        var previousAttempts = await _dbContext.ClassroomAssignmentAttempts.CountAsync(
            attempt => attempt.ClassroomAssignmentId == assignment.Id && attempt.UserId == actorUserId,
            cancellationToken);
        if (previousAttempts >= assignment.AttemptLimit)
        {
            throw new InvalidOperationException("Assignment attempt limit has been reached.");
        }

        var attempt = new ClassroomAssignmentAttempt
        {
            ClassroomAssignmentId = assignment.Id,
            UserId = actorUserId,
            StartedAt = DateTime.UtcNow,
            Status = ClassroomAttemptStatus.InProgress,
            AttemptNumber = previousAttempts + 1
        };

        _dbContext.ClassroomAssignmentAttempts.Add(attempt);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await LoadAttemptAsync(attempt.Id, cancellationToken) ?? attempt;
    }

    public async Task<ClassroomAssignmentAnswer> SubmitAnswerAsync(
        int attemptId,
        int actorUserId,
        SubmitClassroomAssignmentAnswerInput input,
        CancellationToken cancellationToken = default)
    {
        var attempt = await LoadOwnedInProgressAttemptAsync(attemptId, actorUserId, cancellationToken);
        EnsureAttemptStillAcceptsAnswers(attempt);

        var item = attempt.Assignment?.QuestionSet?.Items.FirstOrDefault(candidate => candidate.QuestionId == input.QuestionId);
        if (item == null || item.Question == null)
        {
            throw new InvalidOperationException("Question does not belong to this assignment.");
        }

        if (input.TimeSpentSeconds.HasValue && input.TimeSpentSeconds.Value < 0)
        {
            throw new InvalidOperationException("Time spent seconds cannot be negative.");
        }

        var now = DateTime.UtcNow;
        var selectedAnswer = NormalizeOptional(input.SelectedAnswer);
        var isCorrect = IsCorrectAnswer(item.Question.CorrectAnswer, selectedAnswer);
        var pointEarned = isCorrect ? ConvertPointWeight(item.PointWeight) : 0m;

        var answer = attempt.Answers.FirstOrDefault(candidate => candidate.QuestionId == input.QuestionId);
        if (answer == null)
        {
            answer = new ClassroomAssignmentAnswer
            {
                AttemptId = attempt.Id,
                QuestionId = input.QuestionId
            };
            attempt.Answers.Add(answer);
        }

        answer.SelectedAnswer = selectedAnswer;
        answer.IsCorrect = isCorrect;
        answer.PointEarned = pointEarned;
        answer.TimeSpentSeconds = input.TimeSpentSeconds;
        answer.AnsweredAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return answer;
    }

    public async Task<ClassroomAssignmentAttempt> SubmitAttemptAsync(
        int attemptId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await LoadOwnedInProgressAttemptAsync(attemptId, actorUserId, cancellationToken);
        EnsureAttemptStillAcceptsAnswers(attempt);
        RecalculateScore(attempt);

        var now = DateTime.UtcNow;
        attempt.Status = ClassroomAttemptStatus.Submitted;
        attempt.SubmittedAt = now;
        attempt.DurationSeconds = Math.Max(0, (int)(now - attempt.StartedAt).TotalSeconds);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return attempt;
    }

    public async Task<IReadOnlyList<ClassroomAssignmentAttempt>> GetMyAttemptsAsync(
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ClassroomAssignmentAttempts
            .Include(attempt => attempt.Assignment)
                .ThenInclude(assignment => assignment!.QuestionSet)
            .Include(attempt => attempt.Answers)
                .ThenInclude(answer => answer.Question)
            .Where(attempt => attempt.UserId == actorUserId)
            .OrderByDescending(attempt => attempt.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClassroomAssignmentAttempt>> GetAssignmentAttemptsForTeacherAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await LoadManagedAssignmentAsync(assignmentId, actorUserId, cancellationToken);
        return await _dbContext.ClassroomAssignmentAttempts
            .Include(attempt => attempt.User)
            .Include(attempt => attempt.Answers)
                .ThenInclude(answer => answer.Question)
            .Where(attempt => attempt.ClassroomAssignmentId == assignment.Id)
            .OrderByDescending(attempt => attempt.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ClassroomAssignmentAttempt?> GetAttemptDetailAsync(
        int attemptId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await LoadAttemptAsync(attemptId, cancellationToken);
        if (attempt == null)
        {
            return null;
        }

        if (attempt.UserId == actorUserId)
        {
            return attempt;
        }

        var classroomWorkspaceId = attempt.Assignment?.ClassroomWorkspaceId
            ?? throw new InvalidOperationException("Attempt assignment was not found.");
        return await _permissionService.CanManageClassroomAsync(classroomWorkspaceId, actorUserId, cancellationToken)
            ? attempt
            : null;
    }

    private async Task<ClassroomAssignment> LoadManagedAssignmentAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var assignment = await LoadAssignmentAsync(assignmentId, cancellationToken)
            ?? throw new InvalidOperationException("Assignment was not found.");
        await EnsureCanManageAsync(assignment.ClassroomWorkspaceId, actorUserId, cancellationToken);
        return assignment;
    }

    private async Task<ClassroomAssignment?> LoadAssignmentAsync(
        int assignmentId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ClassroomAssignments
            .Include(assignment => assignment.QuestionSet)
                .ThenInclude(questionSet => questionSet!.Items)
                    .ThenInclude(item => item.Question)
            .Include(assignment => assignment.Attempts)
            .FirstOrDefaultAsync(assignment => assignment.Id == assignmentId, cancellationToken);
    }

    private async Task<ClassroomQuestionSet?> LoadQuestionSetAsync(
        int questionSetId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ClassroomQuestionSets
            .Include(questionSet => questionSet.Items)
                .ThenInclude(item => item.Question)
            .FirstOrDefaultAsync(questionSet => questionSet.Id == questionSetId, cancellationToken);
    }

    private async Task<ClassroomAssignmentAttempt?> LoadAttemptAsync(
        int attemptId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.ClassroomAssignmentAttempts
            .Include(attempt => attempt.Assignment)
                .ThenInclude(assignment => assignment!.QuestionSet)
                    .ThenInclude(questionSet => questionSet!.Items)
                        .ThenInclude(item => item.Question)
            .Include(attempt => attempt.Answers)
                .ThenInclude(answer => answer.Question)
            .Include(attempt => attempt.User)
            .FirstOrDefaultAsync(attempt => attempt.Id == attemptId, cancellationToken);
    }

    private async Task<ClassroomAssignmentAttempt> LoadOwnedInProgressAttemptAsync(
        int attemptId,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var attempt = await LoadAttemptAsync(attemptId, cancellationToken)
            ?? throw new InvalidOperationException("Assignment attempt was not found.");
        if (attempt.UserId != actorUserId)
        {
            throw new UnauthorizedAccessException("User cannot modify another student's assignment attempt.");
        }

        if (attempt.Status != ClassroomAttemptStatus.InProgress)
        {
            throw new InvalidOperationException("Assignment attempt is not in progress.");
        }

        return attempt;
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

    private async Task EnsureCanManageAsync(
        int classroomWorkspaceId,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        if (!await _permissionService.CanManageClassroomAsync(classroomWorkspaceId, actorUserId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Only classroom teachers can manage assignments.");
        }
    }

    private async Task EnsureActiveStudentAsync(
        int classroomWorkspaceId,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        if (!await _permissionService.IsStudentAsync(classroomWorkspaceId, actorUserId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Only active classroom students can start assignment attempts.");
        }
    }

    private static void EnsureQuestionSetCanBackAssignment(
        ClassroomQuestionSet questionSet,
        int classroomWorkspaceId)
    {
        if (questionSet.ClassroomWorkspaceId != classroomWorkspaceId)
        {
            throw new InvalidOperationException("Question set must belong to the same classroom.");
        }

        if (questionSet.Visibility != ClassroomQuestionSetVisibility.Published)
        {
            throw new InvalidOperationException("Question set must be published before it can be assigned.");
        }

        if (questionSet.Items.Count == 0)
        {
            throw new InvalidOperationException("Question set must contain at least one question before assigning.");
        }
    }

    private static void EnsureAttemptCanStart(ClassroomAssignment assignment)
    {
        if (assignment.Status != ClassroomAssignmentStatus.Published)
        {
            throw new InvalidOperationException("Assignment is not published.");
        }

        var now = DateTime.UtcNow;
        if (assignment.StartAt.HasValue && assignment.StartAt.Value > now)
        {
            throw new InvalidOperationException("Assignment has not started yet.");
        }

        if (assignment.DueAt.HasValue && assignment.DueAt.Value <= now)
        {
            throw new InvalidOperationException("Assignment is past due.");
        }
    }

    private static void EnsureAttemptStillAcceptsAnswers(ClassroomAssignmentAttempt attempt)
    {
        var assignment = attempt.Assignment ?? throw new InvalidOperationException("Assignment was not found.");
        var now = DateTime.UtcNow;
        if (assignment.DueAt.HasValue && assignment.DueAt.Value <= now)
        {
            attempt.Status = ClassroomAttemptStatus.Expired;
            throw new InvalidOperationException("Assignment is past due.");
        }

        if (assignment.TimeLimitMinutes.HasValue
            && attempt.StartedAt.AddMinutes(assignment.TimeLimitMinutes.Value) <= now)
        {
            attempt.Status = ClassroomAttemptStatus.Expired;
            throw new InvalidOperationException("Assignment attempt has expired.");
        }
    }

    private static void RecalculateScore(ClassroomAssignmentAttempt attempt)
    {
        var items = attempt.Assignment?.QuestionSet?.Items ?? [];
        var totalPossible = items.Sum(item => ConvertPointWeight(item.PointWeight));
        var earned = attempt.Answers.Sum(answer => answer.PointEarned);

        attempt.RawScore = earned;
        attempt.PercentScore = totalPossible <= 0 ? 0 : Math.Round(earned / totalPossible * 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static void ValidateAssignmentInput(
        string? title,
        int attemptLimit,
        int? timeLimitMinutes,
        DateTime? startAt,
        DateTime? dueAt)
    {
        NormalizeRequired(title, "Assignment title is required.");
        if (attemptLimit <= 0)
        {
            throw new InvalidOperationException("Attempt limit must be greater than zero.");
        }

        if (timeLimitMinutes.HasValue && timeLimitMinutes.Value <= 0)
        {
            throw new InvalidOperationException("Time limit minutes must be greater than zero.");
        }

        if (startAt.HasValue && dueAt.HasValue && dueAt.Value <= startAt.Value)
        {
            throw new InvalidOperationException("Due date must be after start date.");
        }
    }

    private static bool IsCorrectAnswer(string? correctAnswer, string? selectedAnswer)
    {
        return !string.IsNullOrWhiteSpace(correctAnswer)
            && !string.IsNullOrWhiteSpace(selectedAnswer)
            && string.Equals(correctAnswer.Trim(), selectedAnswer.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static decimal ConvertPointWeight(double pointWeight)
    {
        return Convert.ToDecimal(pointWeight);
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
