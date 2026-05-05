using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

        var authResult = EnsureOwnerOrAdmin(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var question = await _questionRepository.GetByIdAsync(request.QuestionId);
        if (question == null || question.DocumentId != request.DocumentId)
        {
            return NotFound("Question not found for this document.");
        }

        var isCorrect = ResolveCorrectness(question, request);
        if (!isCorrect.HasValue)
        {
            return BadRequest("Cannot determine correctness. Provide selectedAnswer or isCorrect.");
        }

        var progress = await _learningProgressService.RecordAttemptAsync(
            CurrentUserIdAsString,
            request.DocumentId,
            request.QuestionId,
            request.Mode,
            request.SelectedAnswer,
            isCorrect.Value,
            request.ResponseTimeMs,
            cancellationToken);

        return Ok(progress);
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

        var authResult = EnsureOwnerOrAdmin(document.UploadedBy);
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

        var authResult = EnsureOwnerOrAdmin(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var totalQuestions = (await _questionRepository.GetByDocumentIdAsync(documentId)).Count();
        var summary = await _learningProgressService.GetDocumentSummaryAsync(
            CurrentUserIdAsString,
            documentId,
            totalQuestions,
            cancellationToken);

        return Ok(summary);
    }

    private static bool? ResolveCorrectness(Question question, RecordLearningAttemptRequest request)
    {
        if (!string.IsNullOrWhiteSpace(question.CorrectAnswer) && !string.IsNullOrWhiteSpace(request.SelectedAnswer))
        {
            return string.Equals(
                request.SelectedAnswer.Trim(),
                question.CorrectAnswer.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        return request.IsCorrect;
    }
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
