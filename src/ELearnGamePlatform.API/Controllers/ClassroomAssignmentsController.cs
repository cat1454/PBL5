using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Authorize]
[Route("api/classroom-assignments")]
public sealed class ClassroomAssignmentsController : AuthenticatedControllerBase
{
    private readonly IClassroomAssignmentService _assignmentService;
    private readonly IClassroomPermissionService _permissionService;

    public ClassroomAssignmentsController(
        IClassroomAssignmentService assignmentService,
        IClassroomPermissionService permissionService)
    {
        _assignmentService = assignmentService;
        _permissionService = permissionService;
    }

    [HttpPost("/api/classroom-workspaces/{classroomId:int}/assignments")]
    public async Task<IActionResult> Create(
        int classroomId,
        [FromBody] CreateClassroomAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ApiBadRequest("request_required", "Request body is required.");
        }

        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var assignment = await _assignmentService.CreateAssignmentAsync(
                classroomId,
                CurrentUserId.Value,
                ToCreateInput(request),
                cancellationToken);

            return Ok(MapTeacherAssignment(assignment, includeItems: true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_assignment_manage_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_assignment_invalid", ex.Message);
        }
    }

    [HttpGet("/api/classroom-workspaces/{classroomId:int}/assignments")]
    public async Task<IActionResult> GetForClassroom(int classroomId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var assignments = await _assignmentService.GetAssignmentsForClassroomAsync(
                classroomId,
                CurrentUserId.Value,
                studentViewOnly: false,
                cancellationToken);

            return Ok(assignments.Select(assignment => MapTeacherAssignment(assignment)));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_assignment_view_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_assignment_invalid", ex.Message);
        }
    }

    [HttpGet("/api/classroom-workspaces/{classroomId:int}/student/assignments")]
    public async Task<IActionResult> GetStudentAssignments(int classroomId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var assignments = await _assignmentService.GetAssignmentsForClassroomAsync(
                classroomId,
                CurrentUserId.Value,
                studentViewOnly: true,
                cancellationToken);

            return Ok(assignments.Select(assignment => MapStudentAssignment(assignment)));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_assignment_view_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_assignment_invalid", ex.Message);
        }
    }

    [HttpGet("{assignmentId:int}")]
    public async Task<IActionResult> GetById(int assignmentId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        var assignment = await _assignmentService.GetAssignmentDetailAsync(
            assignmentId,
            CurrentUserId.Value,
            cancellationToken);
        if (assignment == null)
        {
            return ApiNotFound("classroom_assignment_not_found", "Assignment was not found or is not available to this user.");
        }

        var canManage = await _permissionService.CanManageClassroomAsync(
            assignment.ClassroomWorkspaceId,
            CurrentUserId.Value,
            cancellationToken);
        return Ok(canManage
            ? MapTeacherAssignment(assignment, includeItems: true)
            : MapStudentAssignment(assignment, includeItems: true));
    }

    [HttpPut("{assignmentId:int}")]
    public async Task<IActionResult> Update(
        int assignmentId,
        [FromBody] UpdateClassroomAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ApiBadRequest("request_required", "Request body is required.");
        }

        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var assignment = await _assignmentService.UpdateAssignmentAsync(
                assignmentId,
                CurrentUserId.Value,
                ToUpdateInput(request),
                cancellationToken);

            return Ok(MapTeacherAssignment(assignment, includeItems: true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_assignment_manage_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_assignment_invalid", ex.Message);
        }
    }

    [HttpDelete("{assignmentId:int}")]
    public async Task<IActionResult> Delete(int assignmentId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            await _assignmentService.DeleteAssignmentAsync(assignmentId, CurrentUserId.Value, cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_assignment_manage_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_assignment_invalid", ex.Message);
        }
    }

    [HttpPost("{assignmentId:int}/publish")]
    public async Task<IActionResult> Publish(int assignmentId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var assignment = await _assignmentService.PublishAssignmentAsync(assignmentId, CurrentUserId.Value, cancellationToken);
            return Ok(MapTeacherAssignment(assignment, includeItems: true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_assignment_manage_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_assignment_invalid", ex.Message);
        }
    }

    [HttpPost("{assignmentId:int}/close")]
    public async Task<IActionResult> Close(int assignmentId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var assignment = await _assignmentService.CloseAssignmentAsync(assignmentId, CurrentUserId.Value, cancellationToken);
            return Ok(MapTeacherAssignment(assignment, includeItems: true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_assignment_manage_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_assignment_invalid", ex.Message);
        }
    }

    [HttpGet("{assignmentId:int}/attempts")]
    public async Task<IActionResult> GetAttemptsForTeacher(int assignmentId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var attempts = await _assignmentService.GetAssignmentAttemptsForTeacherAsync(
                assignmentId,
                CurrentUserId.Value,
                cancellationToken);

            return Ok(attempts.Select(attempt => MapTeacherAttempt(attempt, includeAnswers: true)));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_assignment_manage_required", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_assignment_invalid", ex.Message);
        }
    }

    [HttpPost("{assignmentId:int}/attempts/start")]
    public async Task<IActionResult> StartAttempt(int assignmentId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var attempt = await _assignmentService.StartAttemptAsync(
                assignmentId,
                CurrentUserId.Value,
                cancellationToken);

            return Ok(MapStudentAttempt(attempt, includeAnswers: true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_assignment_attempt_forbidden", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_assignment_attempt_invalid", ex.Message);
        }
    }

    [HttpPost("/api/classroom-assignment-attempts/{attemptId:int}/answers")]
    public async Task<IActionResult> SubmitAnswer(
        int attemptId,
        [FromBody] SubmitClassroomAssignmentAnswerRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ApiBadRequest("request_required", "Request body is required.");
        }

        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var answer = await _assignmentService.SubmitAnswerAsync(
                attemptId,
                CurrentUserId.Value,
                new SubmitClassroomAssignmentAnswerInput(request.QuestionId, request.SelectedAnswer, request.TimeSpentSeconds),
                cancellationToken);

            return Ok(MapStudentAnswer(answer, revealGrading: false));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_assignment_attempt_forbidden", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_assignment_answer_invalid", ex.Message);
        }
    }

    [HttpPost("/api/classroom-assignment-attempts/{attemptId:int}/submit")]
    public async Task<IActionResult> SubmitAttempt(int attemptId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        try
        {
            var attempt = await _assignmentService.SubmitAttemptAsync(
                attemptId,
                CurrentUserId.Value,
                cancellationToken);

            return Ok(MapStudentAttempt(attempt, includeAnswers: true));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_assignment_attempt_forbidden", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("classroom_assignment_attempt_invalid", ex.Message);
        }
    }

    [HttpGet("/api/classroom-assignment-attempts/my")]
    public async Task<IActionResult> GetMyAttempts(CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        var attempts = await _assignmentService.GetMyAttemptsAsync(CurrentUserId.Value, cancellationToken);
        return Ok(attempts.Select(attempt => MapStudentAttempt(attempt, includeAnswers: true)));
    }

    [HttpGet("/api/classroom-assignment-attempts/{attemptId:int}")]
    public async Task<IActionResult> GetAttemptById(int attemptId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(ApiErrorResponse.Create("user_context_required", "User context is required."));
        }

        var attempt = await _assignmentService.GetAttemptDetailAsync(attemptId, CurrentUserId.Value, cancellationToken);
        if (attempt == null)
        {
            return ApiNotFound("classroom_assignment_attempt_not_found", "Assignment attempt was not found or is not available to this user.");
        }

        if (attempt.UserId == CurrentUserId.Value)
        {
            return Ok(MapStudentAttempt(attempt, includeAnswers: true));
        }

        return Ok(MapTeacherAttempt(attempt, includeAnswers: true));
    }

    private static CreateClassroomAssignmentInput ToCreateInput(CreateClassroomAssignmentRequest request)
    {
        return new CreateClassroomAssignmentInput(
            request.QuestionSetId,
            request.Title,
            request.Description,
            request.Type,
            request.StartAt,
            request.DueAt,
            request.TimeLimitMinutes,
            request.AttemptLimit,
            request.ShuffleQuestions,
            request.ShuffleOptions,
            request.ShowAnswerAfterSubmit);
    }

    private static UpdateClassroomAssignmentInput ToUpdateInput(UpdateClassroomAssignmentRequest request)
    {
        return new UpdateClassroomAssignmentInput(
            request.Title,
            request.Description,
            request.Type,
            request.StartAt,
            request.DueAt,
            request.TimeLimitMinutes,
            request.AttemptLimit,
            request.ShuffleQuestions,
            request.ShuffleOptions,
            request.ShowAnswerAfterSubmit);
    }

    private static object MapTeacherAssignment(ClassroomAssignment assignment, bool includeItems = false)
    {
        var items = assignment.QuestionSet?.Items
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToList() ?? new List<ClassroomQuestionSetItem>();

        return new
        {
            id = assignment.Id,
            classroomWorkspaceId = assignment.ClassroomWorkspaceId,
            questionSetId = assignment.QuestionSetId,
            title = assignment.Title,
            description = assignment.Description,
            type = assignment.Type.ToString(),
            status = assignment.Status.ToString(),
            startAt = assignment.StartAt,
            dueAt = assignment.DueAt,
            timeLimitMinutes = assignment.TimeLimitMinutes,
            attemptLimit = assignment.AttemptLimit,
            shuffleQuestions = assignment.ShuffleQuestions,
            shuffleOptions = assignment.ShuffleOptions,
            showAnswerAfterSubmit = assignment.ShowAnswerAfterSubmit,
            createdByUserId = assignment.CreatedByUserId,
            createdAt = assignment.CreatedAt,
            updatedAt = assignment.UpdatedAt,
            itemCount = items.Count,
            totalPoints = items.Sum(item => item.PointWeight),
            questionSet = assignment.QuestionSet == null
                ? null
                : new
                {
                    id = assignment.QuestionSet.Id,
                    title = assignment.QuestionSet.Title,
                    visibility = assignment.QuestionSet.Visibility.ToString()
                },
            items = includeItems ? items.Select(MapTeacherQuestionSetItem) : null
        };
    }

    private static object MapStudentAssignment(ClassroomAssignment assignment, bool includeItems = false)
    {
        var items = assignment.QuestionSet?.Items
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToList() ?? new List<ClassroomQuestionSetItem>();

        return new
        {
            id = assignment.Id,
            classroomWorkspaceId = assignment.ClassroomWorkspaceId,
            questionSetId = assignment.QuestionSetId,
            title = assignment.Title,
            description = assignment.Description,
            type = assignment.Type.ToString(),
            status = assignment.Status.ToString(),
            startAt = assignment.StartAt,
            dueAt = assignment.DueAt,
            timeLimitMinutes = assignment.TimeLimitMinutes,
            attemptLimit = assignment.AttemptLimit,
            shuffleQuestions = assignment.ShuffleQuestions,
            shuffleOptions = assignment.ShuffleOptions,
            showAnswerAfterSubmit = assignment.ShowAnswerAfterSubmit,
            createdAt = assignment.CreatedAt,
            updatedAt = assignment.UpdatedAt,
            itemCount = items.Count,
            totalPoints = items.Sum(item => item.PointWeight),
            questionSet = assignment.QuestionSet == null
                ? null
                : new
                {
                    id = assignment.QuestionSet.Id,
                    title = assignment.QuestionSet.Title
                },
            items = includeItems ? items.Select(MapStudentQuestionSetItem) : null
        };
    }

    private static object MapTeacherQuestionSetItem(ClassroomQuestionSetItem item)
    {
        return new
        {
            id = item.Id,
            questionId = item.QuestionId,
            orderIndex = item.OrderIndex,
            pointWeight = item.PointWeight,
            sectionCode = item.SectionCode,
            question = item.Question == null
                ? null
                : new
                {
                    id = item.Question.Id,
                    documentId = item.Question.DocumentId,
                    questionText = item.Question.QuestionText,
                    questionType = item.Question.QuestionType.ToString(),
                    options = item.Question.OptionsJson,
                    correctAnswer = item.Question.CorrectAnswer,
                    explanation = item.Question.Explanation,
                    difficulty = item.Question.Difficulty.ToString(),
                    topic = item.Question.Topic
                }
        };
    }

    private static object MapStudentQuestionSetItem(ClassroomQuestionSetItem item)
    {
        return new
        {
            id = item.Id,
            questionId = item.QuestionId,
            orderIndex = item.OrderIndex,
            pointWeight = item.PointWeight,
            sectionCode = item.SectionCode,
            question = item.Question == null
                ? null
                : new
                {
                    id = item.Question.Id,
                    documentId = item.Question.DocumentId,
                    questionText = item.Question.QuestionText,
                    questionType = item.Question.QuestionType.ToString(),
                    options = item.Question.OptionsJson,
                    difficulty = item.Question.Difficulty.ToString(),
                    topic = item.Question.Topic
                }
        };
    }

    private static object MapTeacherAttempt(ClassroomAssignmentAttempt attempt, bool includeAnswers = false)
    {
        return new
        {
            id = attempt.Id,
            classroomAssignmentId = attempt.ClassroomAssignmentId,
            userId = attempt.UserId,
            startedAt = attempt.StartedAt,
            submittedAt = attempt.SubmittedAt,
            status = attempt.Status.ToString(),
            rawScore = attempt.RawScore,
            percentScore = attempt.PercentScore,
            durationSeconds = attempt.DurationSeconds,
            attemptNumber = attempt.AttemptNumber,
            assignment = attempt.Assignment == null
                ? null
                : new
                {
                    id = attempt.Assignment.Id,
                    classroomWorkspaceId = attempt.Assignment.ClassroomWorkspaceId,
                    title = attempt.Assignment.Title,
                    status = attempt.Assignment.Status.ToString(),
                    showAnswerAfterSubmit = attempt.Assignment.ShowAnswerAfterSubmit,
                    items = attempt.Assignment.QuestionSet?.Items
                        .OrderBy(item => item.OrderIndex)
                        .ThenBy(item => item.Id)
                        .Select(MapTeacherQuestionSetItem)
                },
            user = attempt.User == null
                ? null
                : new
                {
                    id = attempt.User.Id,
                    fullName = attempt.User.FullName,
                    email = attempt.User.Email
                },
            answers = includeAnswers ? attempt.Answers.OrderBy(answer => answer.Id).Select(MapTeacherAnswer) : null
        };
    }

    private static object MapStudentAttempt(ClassroomAssignmentAttempt attempt, bool includeAnswers = false)
    {
        var canRevealGrading = CanRevealStudentGrading(attempt);
        if (attempt.Status != ClassroomAttemptStatus.Submitted)
        {
            return new
            {
                id = attempt.Id,
                classroomAssignmentId = attempt.ClassroomAssignmentId,
                userId = attempt.UserId,
                startedAt = attempt.StartedAt,
                submittedAt = attempt.SubmittedAt,
                status = attempt.Status.ToString(),
                durationSeconds = attempt.DurationSeconds,
                attemptNumber = attempt.AttemptNumber,
                assignment = MapStudentAttemptAssignment(attempt),
                answers = includeAnswers
                    ? attempt.Answers.OrderBy(answer => answer.Id).Select(answer => MapStudentAnswer(answer, revealGrading: false))
                    : null
            };
        }

        return new
        {
            id = attempt.Id,
            classroomAssignmentId = attempt.ClassroomAssignmentId,
            userId = attempt.UserId,
            startedAt = attempt.StartedAt,
            submittedAt = attempt.SubmittedAt,
            status = attempt.Status.ToString(),
            rawScore = attempt.RawScore,
            percentScore = attempt.PercentScore,
            durationSeconds = attempt.DurationSeconds,
            attemptNumber = attempt.AttemptNumber,
            assignment = MapStudentAttemptAssignment(attempt),
            answers = includeAnswers
                ? attempt.Answers.OrderBy(answer => answer.Id).Select(answer => MapStudentAnswer(answer, canRevealGrading))
                : null
        };
    }

    private static object? MapStudentAttemptAssignment(ClassroomAssignmentAttempt attempt)
    {
        return attempt.Assignment == null
            ? null
            : new
            {
                id = attempt.Assignment.Id,
                classroomWorkspaceId = attempt.Assignment.ClassroomWorkspaceId,
                title = attempt.Assignment.Title,
                status = attempt.Assignment.Status.ToString(),
                showAnswerAfterSubmit = attempt.Assignment.ShowAnswerAfterSubmit,
                items = attempt.Assignment.QuestionSet?.Items
                    .OrderBy(item => item.OrderIndex)
                    .ThenBy(item => item.Id)
                    .Select(MapStudentQuestionSetItem)
            };
    }

    private static object MapTeacherAnswer(ClassroomAssignmentAnswer answer)
    {
        return new
        {
            id = answer.Id,
            attemptId = answer.AttemptId,
            questionId = answer.QuestionId,
            selectedAnswer = answer.SelectedAnswer,
            isCorrect = answer.IsCorrect,
            pointEarned = answer.PointEarned,
            timeSpentSeconds = answer.TimeSpentSeconds,
            answeredAt = answer.AnsweredAt,
            question = answer.Question == null
                ? null
                : new
                {
                    id = answer.Question.Id,
                    questionText = answer.Question.QuestionText,
                    questionType = answer.Question.QuestionType.ToString(),
                    options = answer.Question.OptionsJson,
                    correctAnswer = answer.Question.CorrectAnswer,
                    explanation = answer.Question.Explanation
                }
        };
    }

    private static object MapStudentAnswer(ClassroomAssignmentAnswer answer, bool revealGrading)
    {
        if (revealGrading)
        {
            return new
            {
                id = answer.Id,
                attemptId = answer.AttemptId,
                questionId = answer.QuestionId,
                selectedAnswer = answer.SelectedAnswer,
                isCorrect = (bool?)answer.IsCorrect,
                pointEarned = (decimal?)answer.PointEarned,
                timeSpentSeconds = answer.TimeSpentSeconds,
                answeredAt = answer.AnsweredAt,
                question = answer.Question == null
                    ? null
                    : new
                    {
                        id = answer.Question.Id,
                        questionText = answer.Question.QuestionText,
                        questionType = answer.Question.QuestionType.ToString(),
                        options = answer.Question.OptionsJson,
                        correctAnswer = answer.Question.CorrectAnswer,
                        explanation = answer.Question.Explanation
                    }
            };
        }

        return new
        {
            id = answer.Id,
            attemptId = answer.AttemptId,
            questionId = answer.QuestionId,
            selectedAnswer = answer.SelectedAnswer,
            timeSpentSeconds = answer.TimeSpentSeconds,
            answeredAt = answer.AnsweredAt,
            question = answer.Question == null
                ? null
                : new
                {
                    id = answer.Question.Id,
                    questionText = answer.Question.QuestionText,
                    questionType = answer.Question.QuestionType.ToString(),
                    options = answer.Question.OptionsJson
                }
        };
    }

    private static bool CanRevealStudentGrading(ClassroomAssignmentAttempt attempt)
    {
        return attempt.Status == ClassroomAttemptStatus.Submitted
            && attempt.Assignment?.ShowAnswerAfterSubmit == true;
    }
}

public sealed class CreateClassroomAssignmentRequest
{
    public int QuestionSetId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ClassroomAssignmentType Type { get; set; } = ClassroomAssignmentType.Quiz;
    public DateTime? StartAt { get; set; }
    public DateTime? DueAt { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public int AttemptLimit { get; set; } = 1;
    public bool ShuffleQuestions { get; set; }
    public bool ShuffleOptions { get; set; }
    public bool ShowAnswerAfterSubmit { get; set; }
}

public sealed class UpdateClassroomAssignmentRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ClassroomAssignmentType Type { get; set; } = ClassroomAssignmentType.Quiz;
    public DateTime? StartAt { get; set; }
    public DateTime? DueAt { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public int AttemptLimit { get; set; } = 1;
    public bool ShuffleQuestions { get; set; }
    public bool ShuffleOptions { get; set; }
    public bool ShowAnswerAfterSubmit { get; set; }
}

public sealed class SubmitClassroomAssignmentAnswerRequest
{
    public int QuestionId { get; set; }
    public string? SelectedAnswer { get; set; }
    public int? TimeSpentSeconds { get; set; }
}
