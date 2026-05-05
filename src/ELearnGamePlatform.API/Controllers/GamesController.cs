using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GamesController : AuthenticatedControllerBase
{
    private readonly IGameSessionRepository _gameSessionRepository;
    private readonly IQuestionRepository _questionRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly ILogger<GamesController> _logger;

    public GamesController(
        IGameSessionRepository gameSessionRepository,
        IQuestionRepository questionRepository,
        IDocumentRepository documentRepository,
        ILogger<GamesController> logger)
    {
        _gameSessionRepository = gameSessionRepository;
        _questionRepository = questionRepository;
        _documentRepository = documentRepository;
        _logger = logger;
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> CreateGameSession([FromBody] CreateGameSessionRequest request)
    {
        try
        {
            // Verify document exists
            var document = await _documentRepository.GetByIdAsync(request.DocumentId);
            if (document == null)
            {
                return ApiNotFound("document_not_found", "Document not found");
            }

            var authResult = EnsureOwnerOrAdmin(document.UploadedBy);
            if (authResult != null)
            {
                return authResult;
            }

            // Get questions for the document
            var questions = await _questionRepository.GetByDocumentIdAsync(request.DocumentId);
            var questionsList = questions.ToList();

            if (!questionsList.Any())
            {
                return ApiBadRequest("questions_unavailable", "No questions available for this document. Please generate questions first.");
            }

            // Select random questions based on game type
            var selectedQuestions = questionsList
                .OrderBy(x => Guid.NewGuid())
                .Take(request.QuestionCount)
                .Select(q => q.Id)
                .ToList();

            // Create game session
            var session = new GameSession
            {
                DocumentId = request.DocumentId,
                GameType = request.GameType,
                UserId = CurrentUserIdAsString,
                TotalQuestions = selectedQuestions.Count,
                Status = GameStatus.NotStarted
            };
            session.SetQuestionIds(selectedQuestions);

            var createdSession = await _gameSessionRepository.CreateAsync(session);

            return Ok(new
            {
                sessionId = createdSession.Id,
                gameType = createdSession.GameType.ToString(),
                totalQuestions = createdSession.TotalQuestions,
                status = createdSession.Status.ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating game session");
            return ApiServerError("game_session_create_failed", "Error creating game session");
        }
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetGameSession(int sessionId)
    {
        var session = await _gameSessionRepository.GetByIdAsync(sessionId);
        
        if (session == null)
        {
            return ApiNotFound("game_session_not_found", "Game session not found");
        }

        var authResult = EnsureOwnerOrAdmin(session.UserId);
        if (authResult != null)
        {
            return authResult;
        }

        // Get questions for the session
        var questions = new List<Question>();
        var questionIds = session.GetQuestionIds();
        foreach (var questionId in questionIds)
        {
            var question = await _questionRepository.GetByIdAsync(questionId);
            if (question != null)
            {
                questions.Add(question);
            }
        }

        var includeAnswers = session.Status == GameStatus.Completed;
        return Ok(new
        {
            session = session,
            questions = questions.Select(question => ToGameQuestionPayload(question, includeAnswers))
        });
    }

    [HttpPost("sessions/{sessionId}/start")]
    public async Task<IActionResult> StartGameSession(int sessionId)
    {
        var session = await _gameSessionRepository.GetByIdAsync(sessionId);
        
        if (session == null)
        {
            return ApiNotFound("game_session_not_found", "Game session not found");
        }

        var authResult = EnsureOwnerOrAdmin(session.UserId);
        if (authResult != null)
        {
            return authResult;
        }

        if (session.Status == GameStatus.Completed)
        {
            return ApiConflict("game_session_completed", "Completed game sessions cannot be started again.");
        }

        session.Status = GameStatus.InProgress;
        session.StartedAt = DateTime.UtcNow;
        await _gameSessionRepository.UpdateAsync(sessionId, session);

        return Ok(session);
    }

    [HttpPost("sessions/{sessionId}/submit")]
    public async Task<IActionResult> SubmitGameSession(int sessionId, [FromBody] SubmitAnswersRequest request)
    {
        var session = await _gameSessionRepository.GetByIdAsync(sessionId);
        
        if (session == null)
        {
            return ApiNotFound("game_session_not_found", "Game session not found");
        }

        var authResult = EnsureOwnerOrAdmin(session.UserId);
        if (authResult != null)
        {
            return authResult;
        }

        if (session.Status == GameStatus.Completed)
        {
            return ApiConflict("game_session_completed", "Completed game sessions cannot be submitted again.");
        }

        if (session.Status != GameStatus.InProgress)
        {
            return ApiBadRequest("game_session_not_in_progress", "Game session must be in progress before submit.");
        }

        if (request.Answers == null || request.Answers.Count == 0)
        {
            return ApiBadRequest("answers_required", "Game session submit must include at least one answer.");
        }

        var duplicateQuestionIds = request.Answers
            .GroupBy(answer => answer.QuestionId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateQuestionIds.Count > 0)
        {
            return ApiBadRequest("duplicate_question_ids", "Submitted answers must contain each question only once.");
        }

        var sessionQuestionIds = session.GetQuestionIds();
        var sessionQuestionIdSet = sessionQuestionIds.ToHashSet();
        if (sessionQuestionIds.Count == 0)
        {
            return ApiBadRequest("session_questions_missing", "Game session has no question set.");
        }

        var submittedQuestionIds = request.Answers.Select(answer => answer.QuestionId).ToList();
        var invalidQuestionIds = submittedQuestionIds
            .Where(questionId => !sessionQuestionIdSet.Contains(questionId))
            .Distinct()
            .ToList();
        if (invalidQuestionIds.Count > 0)
        {
            return ApiBadRequest("question_not_in_session", "Every submitted question must belong to this game session.");
        }

        if (submittedQuestionIds.Count != sessionQuestionIdSet.Count)
        {
            return ApiBadRequest("answers_incomplete", "Submitted answers must cover every question in this game session.");
        }

        var questionsById = (await _questionRepository.GetByIdsAsync(submittedQuestionIds))
            .ToDictionary(question => question.Id);
        if (questionsById.Count != submittedQuestionIds.Count)
        {
            return ApiNotFound("question_not_found", "One or more submitted questions were not found.");
        }

        if (questionsById.Values.Any(question => question.DocumentId != session.DocumentId))
        {
            return ApiBadRequest("question_document_mismatch", "Submitted questions must belong to the session document.");
        }

        // Calculate score
        int correctAnswers = 0;
        var results = new List<AnswerResult>();

        foreach (var answer in request.Answers)
        {
            var question = questionsById[answer.QuestionId];
            bool isCorrect = IsSelectedAnswerCorrect(question, answer.SelectedAnswer);
                
            if (isCorrect)
            {
                correctAnswers++;
            }

            results.Add(new AnswerResult
            {
                QuestionId = answer.QuestionId,
                IsCorrect = isCorrect,
                CorrectAnswer = question.CorrectAnswer,
                Explanation = question.Explanation
            });
        }

        // Update session
        session.CorrectAnswers = correctAnswers;
        session.Score = session.TotalQuestions > 0
            ? Math.Clamp((int)Math.Round((double)correctAnswers / session.TotalQuestions * 100), 0, 100)
            : 0;
        session.Status = GameStatus.Completed;
        session.CompletedAt = DateTime.UtcNow;
        await _gameSessionRepository.UpdateAsync(sessionId, session);

        return Ok(new
        {
            sessionId = sessionId,
            score = session.Score,
            correctAnswers = correctAnswers,
            totalQuestions = session.TotalQuestions,
            results = results
        });
    }

    [HttpGet("quiz/{documentId}")]
    public async Task<IActionResult> GetQuizGame(
        int documentId,
        [FromQuery] int count = 10,
        [FromQuery] bool includeAnswers = false)
    {
        var document = await _documentRepository.GetByIdAsync(documentId);
        if (document == null)
        {
            return ApiNotFound("document_not_found", "Document not found");
        }

        var authResult = EnsureOwnerOrAdmin(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var questions = await _questionRepository.GetByDocumentIdAndTypeAsync(documentId, QuestionType.MultipleChoice);
        var questionsList = questions.Take(count).Select(q => ToGameQuestionPayload(q, includeAnswers)).ToList();

        return Ok(new
        {
            documentId = documentId,
            gameType = "Quiz",
            questions = questionsList
        });
    }

    [HttpPost("quiz/{documentId}/answers")]
    public async Task<IActionResult> SubmitQuizAnswer(int documentId, [FromBody] SubmitQuizAnswerRequest request)
    {
        var document = await _documentRepository.GetByIdAsync(documentId);
        if (document == null)
        {
            return ApiNotFound("document_not_found", "Document not found");
        }

        var authResult = EnsureOwnerOrAdmin(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var question = await _questionRepository.GetByIdAsync(request.QuestionId);
        if (question == null || question.DocumentId != documentId)
        {
            return ApiNotFound("question_not_found", "Question not found for this document.");
        }

        if (string.IsNullOrWhiteSpace(request.SelectedAnswer))
        {
            return ApiBadRequest("selected_answer_required", "selectedAnswer is required.");
        }

        var isCorrect = IsSelectedAnswerCorrect(question, request.SelectedAnswer);
        return Ok(new AnswerResult
        {
            QuestionId = question.Id,
            IsCorrect = isCorrect,
            CorrectAnswer = question.CorrectAnswer,
            Explanation = NormalizeGameExplanation(question.Explanation)
        });
    }

    [HttpGet("flashcards/{documentId}")]
    public async Task<IActionResult> GetFlashcards(int documentId)
    {
        var document = await _documentRepository.GetByIdAsync(documentId);
        if (document == null)
        {
            return ApiNotFound("document_not_found", "Document not found");
        }

        var authResult = EnsureOwnerOrAdmin(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var questions = await _questionRepository.GetByDocumentIdAsync(documentId);
        
        var flashcards = questions.Select(q => new
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
            documentId = documentId,
            gameType = "Flashcard",
            flashcards = flashcards
        });
    }

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

    private static bool IsSelectedAnswerCorrect(Question question, string? selectedAnswer)
        => !string.IsNullOrWhiteSpace(question.CorrectAnswer)
            && !string.IsNullOrWhiteSpace(selectedAnswer)
            && string.Equals(
                question.CorrectAnswer.Trim(),
                selectedAnswer.Trim(),
                StringComparison.OrdinalIgnoreCase);

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

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserGameSessions(string userId)
    {
        var authResult = EnsureCurrentUserMatches(userId);
        if (authResult != null)
        {
            return authResult;
        }

        var sessions = await _gameSessionRepository.GetByUserIdAsync(CurrentUserIdAsString);
        return Ok(sessions);
    }
}

public class CreateGameSessionRequest
{
    public required int DocumentId { get; set; }
    public string? UserId { get; set; }
    public GameType GameType { get; set; } = GameType.Quiz;
    public int QuestionCount { get; set; } = 10;
}

public class SubmitAnswersRequest
{
    public required List<UserAnswer> Answers { get; set; }
}

public class UserAnswer
{
    public required int QuestionId { get; set; }
    public required string SelectedAnswer { get; set; }
}

public class SubmitQuizAnswerRequest
{
    public required int QuestionId { get; set; }
    public required string SelectedAnswer { get; set; }
}

public class AnswerResult
{
    public required int QuestionId { get; set; }
    public bool IsCorrect { get; set; }
    public string? CorrectAnswer { get; set; }
    public string? Explanation { get; set; }
}
