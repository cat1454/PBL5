using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.Json;
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
    private readonly ApplicationDbContext _dbContext;

    public LearningController(
        IDocumentRepository documentRepository,
        IQuestionRepository questionRepository,
        ILearningProgressService learningProgressService,
        ApplicationDbContext dbContext)
    {
        _documentRepository = documentRepository;
        _questionRepository = questionRepository;
        _learningProgressService = learningProgressService;
        _dbContext = dbContext;
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
            request.Confidence,
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

        var testSession = await _dbContext.LearningTestResults
            .AsNoTracking()
            .FirstOrDefaultAsync(
                result => result.UserId == CurrentUserIdAsString && result.TestSessionId == request.TestSessionId,
                cancellationToken);
        if (testSession == null)
        {
            return NotFound("Test session not found.");
        }

        var document = await _documentRepository.GetByIdAsync(testSession.DocumentId);
        if (document == null)
        {
            return NotFound("Document not found.");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
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

            if (question.DocumentId != testSession.DocumentId)
            {
                return BadRequest($"Question {answer.QuestionId} does not belong to this test session.");
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

        return Ok(result);
    }

    [HttpGet("export/attempts.csv")]
    public async Task<IActionResult> ExportAttemptsCsv(
        [FromQuery] LearningExportQuery query,
        CancellationToken cancellationToken)
    {
        var authResult = await EnsureExportAccessAsync(query.DocumentId);
        if (authResult != null)
        {
            return authResult;
        }

        var attemptsQuery = _dbContext.LearningAttempts
            .AsNoTracking()
            .Where(attempt => attempt.UserId == CurrentUserIdAsString);

        if (query.DocumentId.HasValue)
        {
            attemptsQuery = attemptsQuery.Where(attempt => attempt.DocumentId == query.DocumentId.Value);
        }

        var fromDate = NormalizeUtc(query.FromDate);
        var toDate = NormalizeUtc(query.ToDate);

        if (fromDate.HasValue)
        {
            attemptsQuery = attemptsQuery.Where(attempt => attempt.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            attemptsQuery = attemptsQuery.Where(attempt => attempt.CreatedAt <= toDate.Value);
        }

        if (query.Mode.HasValue)
        {
            attemptsQuery = attemptsQuery.Where(attempt => attempt.Mode == query.Mode.Value);
        }

        var attempts = await attemptsQuery
            .OrderBy(attempt => attempt.CreatedAt)
            .ThenBy(attempt => attempt.Id)
            .ToListAsync(cancellationToken);

        var csv = BuildCsv(
            new[]
            {
                "attemptId",
                "userId",
                "documentId",
                "questionId",
                "mode",
                "selectedAnswer",
                "isCorrect",
                "confidence",
                "responseTimeMs",
                "createdAt",
                "testResultId"
            },
            attempts.Select(attempt => new object?[]
            {
                attempt.Id,
                attempt.UserId,
                attempt.DocumentId,
                attempt.QuestionId,
                attempt.Mode.ToString(),
                attempt.SelectedAnswer,
                attempt.IsCorrect,
                attempt.Confidence,
                attempt.ResponseTimeMs,
                attempt.CreatedAt,
                attempt.TestResultId
            }));

        return CsvFile(csv, "learning-attempts.csv");
    }

    [HttpGet("export/progress.csv")]
    public async Task<IActionResult> ExportProgressCsv(
        [FromQuery] LearningExportQuery query,
        CancellationToken cancellationToken)
    {
        var authResult = await EnsureExportAccessAsync(query.DocumentId);
        if (authResult != null)
        {
            return authResult;
        }

        var progressQuery = _dbContext.LearningProgresses
            .AsNoTracking()
            .Where(progress => progress.UserId == CurrentUserIdAsString);

        if (query.DocumentId.HasValue)
        {
            progressQuery = progressQuery.Where(progress => progress.DocumentId == query.DocumentId.Value);
        }

        var fromDate = NormalizeUtc(query.FromDate);
        var toDate = NormalizeUtc(query.ToDate);

        if (fromDate.HasValue)
        {
            progressQuery = progressQuery.Where(progress => progress.UpdatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            progressQuery = progressQuery.Where(progress => progress.UpdatedAt <= toDate.Value);
        }

        var progresses = await progressQuery
            .OrderBy(progress => progress.DocumentId)
            .ThenBy(progress => progress.QuestionId)
            .ToListAsync(cancellationToken);

        var csv = BuildCsv(
            new[]
            {
                "progressId",
                "userId",
                "documentId",
                "questionId",
                "attemptCount",
                "correctCount",
                "wrongCount",
                "currentStreak",
                "bestStreak",
                "lastReviewedAt",
                "memoryScore",
                "masteryScore",
                "level",
                "updatedAt"
            },
            progresses.Select(progress => new object?[]
            {
                progress.Id,
                progress.UserId,
                progress.DocumentId,
                progress.QuestionId,
                progress.AttemptCount,
                progress.CorrectCount,
                progress.WrongCount,
                progress.CurrentStreak,
                progress.BestStreak,
                progress.LastReviewedAt,
                progress.MemoryScore,
                progress.MasteryScore,
                progress.Level.ToString(),
                progress.UpdatedAt
            }));

        return CsvFile(csv, "learning-progress.csv");
    }

    [HttpGet("export/test-results.csv")]
    public async Task<IActionResult> ExportTestResultsCsv(
        [FromQuery] LearningExportQuery query,
        CancellationToken cancellationToken)
    {
        var authResult = await EnsureExportAccessAsync(query.DocumentId);
        if (authResult != null)
        {
            return authResult;
        }

        var resultsQuery = _dbContext.LearningTestResults
            .AsNoTracking()
            .Where(result => result.UserId == CurrentUserIdAsString);

        if (query.DocumentId.HasValue)
        {
            resultsQuery = resultsQuery.Where(result => result.DocumentId == query.DocumentId.Value);
        }

        var fromDate = NormalizeUtc(query.FromDate);
        var toDate = NormalizeUtc(query.ToDate);

        if (fromDate.HasValue)
        {
            resultsQuery = resultsQuery.Where(result => result.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            resultsQuery = resultsQuery.Where(result => result.CreatedAt <= toDate.Value);
        }

        if (query.TestType.HasValue)
        {
            resultsQuery = resultsQuery.Where(result => result.TestType == query.TestType.Value);
        }

        var results = await resultsQuery
            .OrderBy(result => result.CreatedAt)
            .ThenBy(result => result.Id)
            .ToListAsync(cancellationToken);

        var csv = BuildCsv(
            new[]
            {
                "testResultId",
                "testSessionId",
                "userId",
                "documentId",
                "testType",
                "status",
                "totalQuestions",
                "correctCount",
                "wrongCount",
                "score",
                "startedAt",
                "submittedAt",
                "durationMs",
                "masteryScoreAfterTest",
                "memoryScoreAfterTest",
                "createdAt"
            },
            results.Select(result => new object?[]
            {
                result.Id,
                result.TestSessionId,
                result.UserId,
                result.DocumentId,
                result.TestType.ToString(),
                result.Status.ToString(),
                result.TotalQuestions,
                result.CorrectCount,
                result.WrongCount,
                result.Score,
                result.StartedAt,
                result.SubmittedAt,
                result.DurationMs,
                ExtractSnapshotNumber(result.ResultSnapshotJson, "masteryScoreAfterTest"),
                ExtractSnapshotNumber(result.ResultSnapshotJson, "memoryScoreAfterTest"),
                result.CreatedAt
            }));

        return CsvFile(csv, "learning-test-results.csv");
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

    [HttpGet("review-queue/{documentId:int}")]
    public async Task<IActionResult> GetReviewQueue(int documentId, CancellationToken cancellationToken)
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

        var queue = await _learningProgressService.GetReviewQueueAsync(
            CurrentUserIdAsString,
            documentId,
            cancellationToken);

        return Ok(queue);
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

    private async Task<IActionResult?> EnsureExportAccessAsync(int? documentId)
    {
        if (CurrentUserId == null || string.IsNullOrWhiteSpace(CurrentUserIdAsString))
        {
            return Unauthorized();
        }

        if (!documentId.HasValue)
        {
            return null;
        }

        var document = await _documentRepository.GetByIdAsync(documentId.Value);
        if (document == null)
        {
            return NotFound("Document not found.");
        }

        return EnsureOwnerAccess(document.UploadedBy);
    }

    private FileContentResult CsvFile(string csv, string fileName)
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv);
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private static string BuildCsv(IEnumerable<string> headers, IEnumerable<IEnumerable<object?>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(EscapeCsvValue)));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", row.Select(EscapeCsvValue)));
        }

        return builder.ToString();
    }

    private static string EscapeCsvValue(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        var text = value switch
        {
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            float number => number.ToString(CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
        {
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        return text;
    }

    private static double? ExtractSnapshotNumber(string? snapshotJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            if (document.RootElement.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetDouble(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
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
    public string? Confidence { get; set; }
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

public class LearningExportQuery
{
    public int? DocumentId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public LearningMode? Mode { get; set; }
    public LearningTestType? TestType { get; set; }
}
