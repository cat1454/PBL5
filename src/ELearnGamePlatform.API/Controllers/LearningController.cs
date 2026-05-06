using ELearnGamePlatform.API.Services;
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
public class LearningController : AuthenticatedControllerBase
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IQuestionRepository _questionRepository;
    private readonly ILearningProgressService _learningProgressService;

    public LearningController(
        IDocumentRepository documentRepository,
        IQuestionRepository questionRepository,
        ILearningProgressService learningProgressService)
    {
        _documentRepository = documentRepository;
        _questionRepository = questionRepository;
        _learningProgressService = learningProgressService;
    }

    [HttpPost("attempts")]
    public async Task<IActionResult> RecordAttempt(
        [FromBody] RecordLearningAttemptRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId == null || string.IsNullOrWhiteSpace(CurrentUserIdAsString))
        {
            return Unauthorized();
        }

        var document = await _documentRepository.GetByIdAsync(request.DocumentId);
        if (document == null)
        {
            return NotFound("Document not found.");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var question = await _questionRepository.GetByIdAsync(request.QuestionId);
        if (question == null || question.DocumentId != request.DocumentId)
        {
            return NotFound("Question not found for this document.");
        }

        var isCorrect = ResolveCorrectness(question, request.Mode, request.SelectedAnswer, request.IsCorrect);
        if (!isCorrect.HasValue)
        {
            return BadRequest("Cannot determine correctness for this learning mode.");
        }

        var progress = await _learningProgressService.RecordAttemptAsync(
            CurrentUserIdAsString,
            request.DocumentId,
            request.QuestionId,
            request.Mode,
            request.SelectedAnswer,
            isCorrect.Value,
            request.ResponseTimeMs,
            cancellationToken: cancellationToken);

        return Ok(progress);
    }

    [HttpPost("tests/start")]
    public async Task<IActionResult> StartTest(
        [FromBody] StartLearningTestRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId == null || string.IsNullOrWhiteSpace(CurrentUserIdAsString))
        {
            return Unauthorized();
        }

        var document = await _documentRepository.GetByIdAsync(request.DocumentId);
        if (document == null)
        {
            return NotFound("Document not found.");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var requestedCount = Math.Clamp(request.Count <= 0 ? 10 : request.Count, 1, 50);
        var questions = (await _questionRepository.GetByDocumentIdAndTypeAsync(request.DocumentId, QuestionType.MultipleChoice))
            .Take(requestedCount)
            .Select(ToSanitizedTestQuestion)
            .ToList();

        if (questions.Count == 0)
        {
            return BadRequest("No test questions available for this document. Please generate questions first.");
        }

        var started = await _learningProgressService.StartTestAsync(
            CurrentUserIdAsString,
            request.DocumentId,
            request.TestType,
            questions,
            cancellationToken);

        return Ok(started);
    }

    [HttpPost("tests/submit")]
    public async Task<IActionResult> SubmitTest(
        [FromBody] SubmitLearningTestRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId == null || string.IsNullOrWhiteSpace(CurrentUserIdAsString))
        {
            return Unauthorized();
        }

        if (request.Answers.Count == 0)
        {
            return BadRequest("Test must include at least one answer.");
        }

        var duplicateQuestionIds = request.Answers
            .GroupBy(answer => answer.QuestionId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateQuestionIds.Count > 0)
        {
            return BadRequest("Test answers must contain each question only once.");
        }

        if (request.TestSessionId == Guid.Empty)
        {
            return BadRequest("testSessionId is required.");
        }

        var testSessionQuestions = request.Answers.Select(answer => answer.QuestionId).ToList();
        var documentQuestions = (await _questionRepository.GetByIdsAsync(testSessionQuestions))
            .ToDictionary(question => question.Id);
        if (documentQuestions.Count != testSessionQuestions.Distinct().Count())
        {
            return NotFound("One or more test questions were not found.");
        }

        var submissions = new List<LearningTestAnswerSubmission>();

        foreach (var answer in request.Answers)
        {
            if (!documentQuestions.TryGetValue(answer.QuestionId, out var question))
            {
                return NotFound($"Question {answer.QuestionId} not found for this document.");
            }

            var isCorrect = ResolveCorrectness(question, LearningMode.Test, answer.SelectedAnswer, null);
            if (!isCorrect.HasValue)
            {
                return BadRequest($"Cannot determine correctness for question {answer.QuestionId}. Provide selectedAnswer.");
            }

            submissions.Add(new LearningTestAnswerSubmission
            {
                QuestionId = question.Id,
                SelectedAnswer = answer.SelectedAnswer,
                IsCorrect = isCorrect.Value,
                ResponseTimeMs = answer.ResponseTimeMs,
                QuestionText = question.QuestionText,
                CorrectAnswer = question.CorrectAnswer,
                Topic = question.Topic
            });
        }

        LearningTestResultSnapshot result;
        try
        {
            result = await _learningProgressService.SubmitTestAsync(
                CurrentUserIdAsString,
                request.TestSessionId,
                request.DurationMs,
                submissions,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var document = await _documentRepository.GetByIdAsync(result.DocumentId);
        if (document == null)
        {
            return NotFound("Document not found.");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        return Ok(result);
    }

    [HttpGet("tests/document/{documentId:int}")]
    public async Task<IActionResult> GetDocumentTestResults(int documentId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null || string.IsNullOrWhiteSpace(CurrentUserIdAsString))
        {
            return Unauthorized();
        }

        var document = await _documentRepository.GetByIdAsync(documentId);
        if (document == null)
        {
            return NotFound("Document not found.");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var results = await _learningProgressService.GetDocumentTestResultsAsync(
            CurrentUserIdAsString,
            documentId,
            cancellationToken);

        return Ok(results);
    }

    [HttpGet("tests/summary/{documentId:int}")]
    public async Task<IActionResult> GetDocumentTestSummary(int documentId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null || string.IsNullOrWhiteSpace(CurrentUserIdAsString))
        {
            return Unauthorized();
        }

        var document = await _documentRepository.GetByIdAsync(documentId);
        if (document == null)
        {
            return NotFound("Document not found.");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var summary = await _learningProgressService.GetDocumentTestSummaryAsync(
            CurrentUserIdAsString,
            documentId,
            cancellationToken);

        return Ok(summary);
    }

    [HttpGet("progress/document/{documentId:int}")]
    public async Task<IActionResult> GetDocumentProgress(int documentId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null || string.IsNullOrWhiteSpace(CurrentUserIdAsString))
        {
            return Unauthorized();
        }

        var document = await _documentRepository.GetByIdAsync(documentId);
        if (document == null)
        {
            return NotFound("Document not found.");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var progress = await _learningProgressService.GetDocumentProgressAsync(
            CurrentUserIdAsString,
            documentId,
            cancellationToken);

        return Ok(progress);
    }

    [HttpGet("progress/summary/{documentId:int}")]
    public async Task<IActionResult> GetDocumentSummary(int documentId, CancellationToken cancellationToken)
    {
        if (CurrentUserId == null || string.IsNullOrWhiteSpace(CurrentUserIdAsString))
        {
            return Unauthorized();
        }

        var document = await _documentRepository.GetByIdAsync(documentId);
        if (document == null)
        {
            return NotFound("Document not found.");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var totalQuestions = await _questionRepository.CountByDocumentIdAsync(documentId, cancellationToken);
        var summary = await _learningProgressService.GetDocumentSummaryAsync(
            CurrentUserIdAsString,
            documentId,
            totalQuestions,
            cancellationToken);

        return Ok(summary);
    }

    private static bool? ResolveCorrectness(Question question, LearningMode mode, string? selectedAnswer, bool? selfAssessment)
    {
        if (mode == LearningMode.Flashcard)
        {
            return selfAssessment;
        }

        if (!string.IsNullOrWhiteSpace(question.CorrectAnswer) && !string.IsNullOrWhiteSpace(selectedAnswer))
        {
            return string.Equals(
                selectedAnswer.Trim(),
                question.CorrectAnswer.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    private static LearningTestQuestionStartSnapshot ToSanitizedTestQuestion(Question question)
    {
        return new LearningTestQuestionStartSnapshot
        {
            Id = question.Id,
            QuestionText = NormalizeLearningText(question.QuestionText),
            QuestionType = question.QuestionType.ToString(),
            Options = question.GetOptions()
                .Select(option => new LearningTestOptionStartSnapshot
                {
                    Key = option.Key,
                    Text = NormalizeLearningText(option.Text)
                })
                .ToList(),
            Difficulty = question.Difficulty.ToString(),
            Topic = question.Topic,
            Quality = BuildQuestionQualityPayload(question)
        };
    }

    private static string NormalizeLearningText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace('\u00A0', ' ');
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"\s+([,.;:?!])", "$1");
        normalized = Regex.Replace(normalized, @"([,.;:?!])(?=[\p{L}\p{N}])", "$1 ");
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
}

public class StartLearningTestRequest
{
    public int DocumentId { get; set; }
    public int Count { get; set; } = 10;
    public LearningTestType TestType { get; set; } = LearningTestType.PracticeTest;
}

public class RecordLearningAttemptRequest
{
    public int DocumentId { get; set; }
    public int QuestionId { get; set; }
    public LearningMode Mode { get; set; }
    public string? SelectedAnswer { get; set; }
    public bool? IsCorrect { get; set; }
    public int? ResponseTimeMs { get; set; }
}

public class SubmitLearningTestRequest
{
    public Guid TestSessionId { get; set; }
    public long? DurationMs { get; set; }
    public List<SubmitLearningTestAnswerRequest> Answers { get; set; } = new();
}

public class SubmitLearningTestAnswerRequest
{
    public int QuestionId { get; set; }
    public string? SelectedAnswer { get; set; }
    public int? ResponseTimeMs { get; set; }
}
