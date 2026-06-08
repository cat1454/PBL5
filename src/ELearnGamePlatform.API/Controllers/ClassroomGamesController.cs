using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ELearnGamePlatform.API.Controllers;

/// <summary>
/// Classroom-scoped practice game sessions.
/// Any classroom member (student or teacher) can start a practice session
/// from a published ClassroomQuestionSet without authoring permissions.
/// </summary>
[ApiController]
[Authorize]
[Route("api/classroom-workspaces/{classroomId:int}/question-sets/{questionSetId:int}/play")]
public sealed class ClassroomGamesController : AuthenticatedControllerBase
{
    private readonly IClassroomQuestionSetService _questionSetService;
    private readonly IClassroomPermissionService _permissionService;
    private readonly IGameSessionRepository _gameSessionRepository;
    private readonly ILogger<ClassroomGamesController> _logger;

    public ClassroomGamesController(
        IClassroomQuestionSetService questionSetService,
        IClassroomPermissionService permissionService,
        IGameSessionRepository gameSessionRepository,
        ILogger<ClassroomGamesController> logger)
    {
        _questionSetService = questionSetService;
        _permissionService = permissionService;
        _gameSessionRepository = gameSessionRepository;
        _logger = logger;
    }

    /// <summary>
    /// Create a practice game session from a published classroom question set.
    /// Body: { gameType: "Quiz"|"Flashcard"|"Streak", questionCount: int? }
    /// </summary>
    [HttpPost("session")]
    public async Task<IActionResult> CreatePracticeSession(
        int classroomId,
        int questionSetId,
        [FromBody] CreateClassroomPracticeSessionRequest request,
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

        // Check classroom membership (student OR teacher both allowed)
        var canView = await _permissionService.CanViewClassroomAsync(classroomId, CurrentUserId.Value, cancellationToken);
        if (!canView)
        {
            return ApiForbidden("classroom_member_required", "You must be a member of this classroom to practice.");
        }

        // Load the question set (service checks membership access)
        ClassroomQuestionSet? questionSet;
        try
        {
            questionSet = await _questionSetService.GetQuestionSetDetailAsync(
                questionSetId,
                CurrentUserId.Value,
                cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiForbidden("classroom_question_set_view_required", ex.Message);
        }

        if (questionSet == null)
        {
            return ApiNotFound("classroom_question_set_not_found", "Question set not found.");
        }

        // Validate the question set belongs to this classroom
        if (questionSet.ClassroomWorkspaceId != classroomId)
        {
            return ApiNotFound("classroom_question_set_not_found", "Question set not found in this classroom.");
        }

        // Only Published sets are available for practice
        if (questionSet.Visibility != ClassroomQuestionSetVisibility.Published)
        {
            return ApiBadRequest("classroom_question_set_not_published",
                "This question set has not been published yet. Ask your teacher to publish it before practicing.");
        }

        // Resolve questions from items (ordered)
        var orderedItems = questionSet.Items
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToList();

        if (orderedItems.Count == 0)
        {
            return ApiBadRequest("classroom_question_set_empty", "This question set has no questions.");
        }

        // Determine the documentId for the GameSession record.
        // Use the set's linked document if available, otherwise fall back to the
        // document of the first question item.
        var documentId = questionSet.DocumentId
            ?? orderedItems.FirstOrDefault(item => item.Question?.DocumentId != null)?.Question?.DocumentId;

        if (documentId == null)
        {
            return ApiBadRequest("classroom_question_set_no_document",
                "Could not resolve a document for this question set. Ensure questions are linked to a document.");
        }

        // Select questions — shuffle then take questionCount
        var questionCount = request.QuestionCount is > 0
            ? Math.Min(request.QuestionCount.Value, orderedItems.Count)
            : orderedItems.Count;

        var selectedIds = orderedItems
            .Select(item => item.QuestionId)
            .OrderBy(_ => Guid.NewGuid()) // shuffle
            .Take(questionCount)
            .ToList();

        // Map game type (Streak maps to Quiz since GameType has no Streak value)
        var gameType = NormalizeGameType(request.GameType);

        // Create game session
        var session = new GameSession
        {
            DocumentId = documentId.Value,
            GameType = gameType,
            UserId = CurrentUserIdAsString,
            TotalQuestions = selectedIds.Count,
            Status = GameStatus.NotStarted
        };
        session.SetQuestionIds(selectedIds);

        GameSession createdSession;
        try
        {
            createdSession = await _gameSessionRepository.CreateAsync(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating classroom practice session for questionSetId={QuestionSetId}", questionSetId);
            return ApiServerError("classroom_practice_session_create_failed", "Could not create practice session.");
        }

        // Build question payload (no answers revealed)
        var questions = orderedItems
            .Where(item => selectedIds.Contains(item.QuestionId))
            .Select(item => item.Question)
            .Where(q => q != null)
            .OrderBy(_ => Guid.NewGuid()) // maintain shuffle order for response
            .Select(q => ToQuestionPayload(q!))
            .ToList();

        return Ok(new
        {
            sessionId = createdSession.Id,
            gameType = createdSession.GameType.ToString(),
            requestedGameType = request.GameType,
            totalQuestions = createdSession.TotalQuestions,
            status = createdSession.Status.ToString(),
            questionSetId = questionSet.Id,
            questionSetTitle = questionSet.Title,
            classroomWorkspaceId = classroomId,
            questions
        });
    }

    // Map frontend game type string to GameType enum.
    // "Streak" is a frontend concept — it uses Quiz questions on the backend.
    private static GameType NormalizeGameType(string? raw)
    {
        return (raw ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "flashcard" => GameType.Flashcard,
            "streak" => GameType.Quiz, // Streak uses quiz-style questions
            _ => GameType.Quiz
        };
    }

    private static object ToQuestionPayload(Question q)
    {
        var options = q.GetOptions().Select(option => new
        {
            key = option.Key,
            text = option.Text
        });

        return new
        {
            id = q.Id,
            questionText = q.QuestionText,
            questionType = q.QuestionType.ToString(),
            options,
            difficulty = q.Difficulty.ToString(),
            topic = q.Topic
        };
    }
}

public sealed class CreateClassroomPracticeSessionRequest
{
    /// <summary>"Quiz", "Flashcard", or "Streak"</summary>
    public string? GameType { get; set; }

    /// <summary>Number of questions to include. Null = all questions in the set.</summary>
    public int? QuestionCount { get; set; }
}
