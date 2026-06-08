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

        // Phase 4: validate and apply scoring config
        var scoringMode = input.ScoringMode ?? ClassroomScoringMode.Percent;
        var minWeight = input.MinQuestionWeight ?? 0.3m;
        var maxWeight = input.MaxQuestionWeight ?? 2.0m;
        var alpha = input.SmoothingAlpha ?? 1m;
        var beta = input.SmoothingBeta ?? 1m;
        if (scoringMode == ClassroomScoringMode.EmpiricalDifficulty)
        {
            ValidateScoringConfig(minWeight, maxWeight, alpha, beta);
        }

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
            ScoringMode = scoringMode,
            MinQuestionWeight = minWeight,
            MaxQuestionWeight = maxWeight,
            SmoothingAlpha = alpha,
            SmoothingBeta = beta,
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

        // Phase 4: apply scoring config if provided, otherwise keep existing
        var minWeight = input.MinQuestionWeight ?? assignment.MinQuestionWeight;
        var maxWeight = input.MaxQuestionWeight ?? assignment.MaxQuestionWeight;
        var alpha = input.SmoothingAlpha ?? assignment.SmoothingAlpha;
        var beta = input.SmoothingBeta ?? assignment.SmoothingBeta;
        var scoringMode = input.ScoringMode ?? assignment.ScoringMode;
        if (scoringMode == ClassroomScoringMode.EmpiricalDifficulty)
        {
            ValidateScoringConfig(minWeight, maxWeight, alpha, beta);
        }

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
        assignment.ScoringMode = input.ScoringMode ?? assignment.ScoringMode;
        assignment.MinQuestionWeight = minWeight;
        assignment.MaxQuestionWeight = maxWeight;
        assignment.SmoothingAlpha = alpha;
        assignment.SmoothingBeta = beta;
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

        // Idempotency: if already closed, skip re-calculation
        if (assignment.Status == ClassroomAssignmentStatus.Closed)
        {
            return assignment;
        }

        if (assignment.ScoringMode == ClassroomScoringMode.EmpiricalDifficulty)
        {
            await ApplyEmpiricalDifficultyScoringAsync(assignment, cancellationToken);
        }

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

    public async Task<IReadOnlyList<ClassroomAssignmentQuestionStat>> GetAssignmentQuestionStatsAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await LoadManagedAssignmentAsync(assignmentId, actorUserId, cancellationToken);
        return await _dbContext.ClassroomAssignmentQuestionStats
            .Where(stat => stat.ClassroomAssignmentId == assignment.Id)
            .OrderBy(stat => stat.QuestionId)
            .ToListAsync(cancellationToken);
    }

    // -----------------------------------------------------------------------
    // Phase 4: Empirical Difficulty Scoring
    // -----------------------------------------------------------------------

    private async Task ApplyEmpiricalDifficultyScoringAsync(
        ClassroomAssignment assignment,
        CancellationToken cancellationToken)
    {
        // 1. Get all questions in the assignment's question set
        var questionIds = assignment.QuestionSet?.Items
            .Select(item => item.QuestionId)
            .ToList() ?? new List<int>();

        if (questionIds.Count == 0)
        {
            return;
        }

        // 2. Get all submitted attempts for this assignment with their answers
        var submittedAttempts = await _dbContext.ClassroomAssignmentAttempts
            .Include(a => a.Answers)
            .Where(a => a.ClassroomAssignmentId == assignment.Id
                        && a.Status == ClassroomAttemptStatus.Submitted)
            .ToListAsync(cancellationToken);

        var alpha = assignment.SmoothingAlpha;
        var beta = assignment.SmoothingBeta;
        var minWeight = assignment.MinQuestionWeight;
        var maxWeight = assignment.MaxQuestionWeight;
        var totalAttempts = submittedAttempts.Count;

        // 3. Compute per-question stats
        var statsMap = new Dictionary<int, (int answered, int correct)>();
        foreach (var qId in questionIds)
        {
            statsMap[qId] = (0, 0);
        }

        foreach (var attempt in submittedAttempts)
        {
            foreach (var answer in attempt.Answers)
            {
                if (!statsMap.ContainsKey(answer.QuestionId))
                {
                    continue;
                }

                var (answered, correct) = statsMap[answer.QuestionId];
                answered++;
                if (answer.IsCorrect) correct++;
                statsMap[answer.QuestionId] = (answered, correct);
            }
        }

        // 4. Compute smoothed correct rate and difficulty weight for each question
        var weightMap = new Dictionary<int, decimal>();
        var now = DateTime.UtcNow;

        foreach (var qId in questionIds)
        {
            var (answered, correct) = statsMap[qId];
            var smoothedRate = (correct + alpha) / (answered + alpha + beta);
            var diffWeight = minWeight + (1m - smoothedRate) * (maxWeight - minWeight);
            weightMap[qId] = diffWeight;

            // Quality flag / discrimination (MVP: InsufficientData if < 5 attempts)
            string? qualityFlag = totalAttempts < 5 ? "InsufficientData" : null;
            decimal? discriminationIndex = null;

            // 5. Upsert ClassroomAssignmentQuestionStat (idempotent)
            var existing = await _dbContext.ClassroomAssignmentQuestionStats
                .FirstOrDefaultAsync(
                    s => s.ClassroomAssignmentId == assignment.Id && s.QuestionId == qId,
                    cancellationToken);

            if (existing == null)
            {
                existing = new ClassroomAssignmentQuestionStat
                {
                    ClassroomAssignmentId = assignment.Id,
                    QuestionId = qId
                };
                _dbContext.ClassroomAssignmentQuestionStats.Add(existing);
            }

            existing.AnsweredCount = answered;
            existing.CorrectCount = correct;
            existing.SmoothedCorrectRate = smoothedRate;
            existing.DifficultyWeight = diffWeight;
            existing.DiscriminationIndex = discriminationIndex;
            existing.QualityFlag = qualityFlag;
            existing.CalculatedAt = now;
        }

        // 6. Calculate totalMaxScore using the computed weights
        var totalMaxScore = questionIds.Sum(qId => weightMap[qId]);

        // 7. Recalculate RawScore and PercentScore for every submitted attempt
        foreach (var attempt in submittedAttempts)
        {
            var rawScore = attempt.Answers
                .Where(a => a.IsCorrect && weightMap.ContainsKey(a.QuestionId))
                .Sum(a => weightMap[a.QuestionId]);

            attempt.RawScore = rawScore;
            attempt.PercentScore = totalMaxScore <= 0
                ? 0
                : Math.Round(rawScore / totalMaxScore * 100m, 2, MidpointRounding.AwayFromZero);
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

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
        if (assignment.Status == ClassroomAssignmentStatus.Closed)
        {
            throw new InvalidOperationException("Assignment has been closed.");
        }

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

    private static void ValidateScoringConfig(
        decimal minWeight,
        decimal maxWeight,
        decimal alpha,
        decimal beta)
    {
        if (minWeight <= 0)
        {
            throw new InvalidOperationException("MinQuestionWeight must be greater than zero.");
        }

        if (maxWeight <= minWeight)
        {
            throw new InvalidOperationException("MaxQuestionWeight must be greater than MinQuestionWeight.");
        }

        if (alpha < 0 || beta < 0)
        {
            throw new InvalidOperationException("SmoothingAlpha and SmoothingBeta must be non-negative.");
        }

        if (alpha + beta <= 0)
        {
            throw new InvalidOperationException("SmoothingAlpha + SmoothingBeta must be greater than zero.");
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
