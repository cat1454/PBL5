using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController : ControllerBase
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
                return NotFound("Document not found");
            }

            // Get questions for the document
            var questions = await _questionRepository.GetByDocumentIdAsync(request.DocumentId);
            var questionsList = questions.ToList();

            if (!questionsList.Any())
            {
                return BadRequest("No questions available for this document. Please generate questions first.");
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
                UserId = request.UserId,
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
            return StatusCode(500, "Error creating game session");
        }
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetGameSession(int sessionId)
    {
        var session = await _gameSessionRepository.GetByIdAsync(sessionId);
        
        if (session == null)
        {
            return NotFound();
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

        return Ok(new
        {
            session = session,
            questions = questions
        });
    }

    [HttpPost("sessions/{sessionId}/start")]
    public async Task<IActionResult> StartGameSession(int sessionId)
    {
        var session = await _gameSessionRepository.GetByIdAsync(sessionId);
        
        if (session == null)
        {
            return NotFound();
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
            return NotFound();
        }

        // Calculate score
        int correctAnswers = 0;
        var results = new List<AnswerResult>();

        foreach (var answer in request.Answers)
        {
            var question = await _questionRepository.GetByIdAsync(answer.QuestionId);
            if (question != null)
            {
                bool isCorrect = question.CorrectAnswer?.Equals(answer.SelectedAnswer, StringComparison.OrdinalIgnoreCase) ?? false;
                
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
        }

        // Update session
        session.CorrectAnswers = correctAnswers;
        session.Score = (int)((double)correctAnswers / session.TotalQuestions * 100);
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
    public async Task<IActionResult> GetQuizGame(int documentId, [FromQuery] int count = 10)
    {
        var questions = await _questionRepository.GetByDocumentIdAndTypeAsync(documentId, QuestionType.MultipleChoice);
        var questionsList = questions.Take(count).Select(q => new
        {
            id = q.Id,
            questionText = q.QuestionText,
            questionType = q.QuestionType.ToString(),
            options = q.GetOptions(),
            correctAnswer = q.CorrectAnswer,
            explanation = q.Explanation,
            difficulty = q.Difficulty.ToString(),
            topic = q.Topic
        }).ToList();

        return Ok(new
        {
            documentId = documentId,
            gameType = "Quiz",
            questions = questionsList
        });
    }

    [HttpGet("flashcards/{documentId}")]
    public async Task<IActionResult> GetFlashcards(int documentId)
    {
        var questions = await _questionRepository.GetByDocumentIdAsync(documentId);
        
        var flashcards = questions.Select(q => new
        {
            id = q.Id,
            front = q.QuestionText,
            back = ResolveFlashcardAnswer(q),
            explanation = q.Explanation,
            topic = q.Topic
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

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserGameSessions(string userId)
    {
        var sessions = await _gameSessionRepository.GetByUserIdAsync(userId);
        return Ok(sessions);
    }
}

public class CreateGameSessionRequest
{
    public required int DocumentId { get; set; }
    public required string UserId { get; set; }
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

public class AnswerResult
{
    public required int QuestionId { get; set; }
    public bool IsCorrect { get; set; }
    public string? CorrectAnswer { get; set; }
    public string? Explanation { get; set; }
}
