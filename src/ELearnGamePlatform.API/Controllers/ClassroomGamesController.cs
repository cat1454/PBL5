using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Authorize]
[Route("api/classroom-workspaces/{classroomId:int}/question-sets/{questionSetId:int}/play")]
public class ClassroomGamesController : AuthenticatedControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IClassroomPermissionService _permissionService;
    private readonly IGameSessionRepository _gameSessionRepository;
    private readonly ILogger<ClassroomGamesController> _logger;

    public ClassroomGamesController(
        ApplicationDbContext dbContext,
        IClassroomPermissionService permissionService,
        IGameSessionRepository gameSessionRepository,
        ILogger<ClassroomGamesController> logger)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
        _gameSessionRepository = gameSessionRepository;
        _logger = logger;
    }

    [HttpPost("session")]
    public async Task<IActionResult> CreateClassroomPlaySession(
        int classroomId,
        int questionSetId,
        [FromBody] CreateClassroomPlaySessionRequest request,
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
            // Verify membership
            var canManage = await _permissionService.CanManageClassroomAsync(classroomId, CurrentUserId.Value, cancellationToken);
            var canView = await _permissionService.CanViewClassroomAsync(classroomId, CurrentUserId.Value, cancellationToken);
            if (!canManage && !canView)
            {
                return ApiForbidden("classroom_access_forbidden", "User is not a member of this classroom.");
            }

            // Load Question Set
            var questionSet = await _dbContext.ClassroomQuestionSets
                .Include(qs => qs.Items)
                    .ThenInclude(item => item.Question)
                .FirstOrDefaultAsync(qs => qs.Id == questionSetId && qs.ClassroomWorkspaceId == classroomId, cancellationToken);

            if (questionSet == null)
            {
                return ApiNotFound("question_set_not_found", "Question set was not found in this classroom.");
            }

            // Check Visibility
            if (questionSet.Visibility != ELearnGamePlatform.Core.Enums.ClassroomQuestionSetVisibility.Published && !canManage)
            {
                return ApiForbidden("question_set_not_published", "This question set is not published yet.");
            }

            // Get active questions
            var questions = questionSet.Items
                .Select(item => item.Question)
                .Where(q => q != null)
                .Select(q => q!)
                .Where(q => !q.IsArchived)
                .ToList();

            if (!questions.Any())
            {
                return ApiBadRequest("questions_unavailable", "No questions available in this question set.");
            }

            // Select random questions
            var questionCount = request.QuestionCount ?? 10;
            if (questionCount <= 0)
            {
                questionCount = 10;
            }

            var selectedQuestions = questions
                .OrderBy(x => Guid.NewGuid())
                .Take(questionCount)
                .ToList();

            var selectedIds = selectedQuestions.Select(q => q.Id).ToList();

            // Determine DocumentId
            int? docId = questionSet.DocumentId ?? selectedQuestions.FirstOrDefault()?.DocumentId;
            if (docId == null)
            {
                return ApiBadRequest("document_context_missing", "Cannot determine document context for this question set.");
            }

            // Map GameType
            GameType mappedGameType = GameType.Quiz;
            if (string.Equals(request.GameType, "Flashcard", StringComparison.OrdinalIgnoreCase))
            {
                mappedGameType = GameType.Flashcard;
            }
            else if (string.Equals(request.GameType, "Streak", StringComparison.OrdinalIgnoreCase))
            {
                mappedGameType = GameType.Quiz; // Map Streak to Quiz in database
            }

            // Create GameSession
            var session = new GameSession
            {
                DocumentId = docId.Value,
                GameType = mappedGameType,
                UserId = CurrentUserIdAsString,
                TotalQuestions = selectedIds.Count,
                Status = GameStatus.InProgress
            };
            session.SetQuestionIds(selectedIds);

            var createdSession = await _gameSessionRepository.CreateAsync(session);

            // Return response based on game mode
            if (mappedGameType == GameType.Flashcard)
            {
                var flashcards = selectedQuestions.Select(q => new
                {
                    id = q.Id,
                    front = NormalizeGameQuestionText(q.QuestionText, q.QuestionType),
                    back = NormalizeGameText(ResolveFlashcardAnswer(q)),
                    explanation = NormalizeGameExplanation(q.Explanation),
                    topic = q.Topic,
                    quality = BuildQuestionQualityPayload(q)
                });

                return Ok(new
                {
                    sessionId = createdSession.Id,
                    gameType = request.GameType,
                    flashcards = flashcards
                });
            }
            else
            {
                // Quiz or Streak
                var questionsPayload = selectedQuestions.Select(q => ToGameQuestionPayload(q, true)).ToList(); // Include answers so frontend can check locally
                return Ok(new
                {
                    sessionId = createdSession.Id,
                    gameType = request.GameType,
                    questions = questionsPayload
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating classroom play session");
            return ApiServerError("classroom_play_session_failed", "Error creating classroom play session");
        }
    }

    #region Helpers
    private static string ResolveFlashcardAnswer(Question question)
    {
        var options = question.GetOptions();
        var correctOption = options.FirstOrDefault(option => option.IsCorrect)
            ?? options.FirstOrDefault(option => string.Equals(option.Key, question.CorrectAnswer, StringComparison.OrdinalIgnoreCase));

        if (correctOption != null)
        {
            return $"{correctOption.Key}. {correctOption.Text}";
        }

        return question.CorrectAnswer ?? "Khong co dap an";
    }

    private static string NormalizeGameQuestionText(string? questionText, QuestionType questionType)
    {
        var normalized = NormalizeGameText(questionText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Cau hoi dang duoc cap nhat";
        }

        normalized = Regex.Replace(
            normalized,
            @"^(Cau\s+\d+:\s+)Theo tai lieu,\s*dau la noi dung dung nhat ve\s+(.+?)([?!.]?)$",
            "$1Theo tai lieu, nhan dinh nao mo ta dung nhat ve $2?",
            RegexOptions.IgnoreCase);

        if (questionType != QuestionType.FillInTheBlank && !Regex.IsMatch(normalized, @"[.!?]$"))
        {
            normalized += questionType == QuestionType.ShortAnswer ? "." : "?";
        }

        return normalized;
    }

    private static string NormalizeGameExplanation(string? explanation)
    {
        var normalized = NormalizeGameText(explanation);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Contains("cau hoi du phong", StringComparison.OrdinalIgnoreCase))
        {
            var evidenceMatch = Regex.Match(normalized, @"Can cu:\s*(.+)$", RegexOptions.IgnoreCase);
            return evidenceMatch.Success
                ? $"Cau hoi nay duoc tao tu cac y chinh trong tai lieu. Can cu: {evidenceMatch.Groups[1].Value}"
                : "Cau hoi nay duoc tao tu cac y chinh trong tai lieu.";
        }

        return normalized;
    }

    private static string NormalizeGameText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace('\u00A0', ' ');
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"(?<=[\p{L}])(?=\d)", " ");
        normalized = Regex.Replace(normalized, @"(?<=\d)(?=[\p{L}])", " ");
        normalized = Regex.Replace(normalized, @"\s+([,.;:?!])", "$1");
        normalized = Regex.Replace(normalized, @"([,.;:?!])(?=[\p{L}\p{N}])", "$1 ");
        normalized = Regex.Replace(normalized, @"\big\b", "g"); // preserve minor fixes
        normalized = Regex.Replace(normalized, @"\(\s+", "(");
        normalized = Regex.Replace(normalized, @"\s+\)", ")");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized;
    }

    private static object BuildQuestionQualityPayload(Question question)
    {
        var issues = question.GetVerifierIssues();
        return new
        {
            score = question.VerifierScore,
            issues,
            isLowConfidence = question.VerifierScore.HasValue && question.VerifierScore.Value < 70,
            isUnknown = !question.VerifierScore.HasValue
        };
    }

    private static object ToGameQuestionPayload(Question q, bool includeAnswers)
    {
        var options = q.GetOptions().Select(option => includeAnswers
            ? (object)new
            {
                key = option.Key,
                text = NormalizeGameText(option.Text),
                isCorrect = option.IsCorrect
            }
            : new
            {
                key = option.Key,
                text = NormalizeGameText(option.Text)
            });

        if (!includeAnswers)
        {
            return new
            {
                id = q.Id,
                questionText = NormalizeGameQuestionText(q.QuestionText, q.QuestionType),
                questionType = q.QuestionType.ToString(),
                options,
                difficulty = q.Difficulty.ToString(),
                topic = q.Topic,
                quality = BuildQuestionQualityPayload(q)
            };
        }

        return new
        {
            id = q.Id,
            questionText = NormalizeGameQuestionText(q.QuestionText, q.QuestionType),
            questionType = q.QuestionType.ToString(),
            options,
            correctAnswer = q.CorrectAnswer,
            explanation = NormalizeGameExplanation(q.Explanation),
            difficulty = q.Difficulty.ToString(),
            topic = q.Topic,
            quality = BuildQuestionQualityPayload(q)
        };
    }
    #endregion
}

public class CreateClassroomPlaySessionRequest
{
    public required string GameType { get; set; } // "Quiz", "Flashcard", "Streak"
    public int? QuestionCount { get; set; }
}
