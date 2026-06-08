using System.Text.Json;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Infrastructure.Data;
using ELearnGamePlatform.API.Services.QuestionStudio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ELearnGamePlatform.API.Filters;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/question-studio")]
[Authorize]
[AuthoringRouteGuard]
public class QuestionStudioController : AuthenticatedControllerBase
{
    private static readonly HashSet<string> AllowedModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "fast",
        "balanced",
        "quality",
        "max_draft"
    };

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Draft",
        "Verified",
        "Borderline",
        "Rejected",
        "Quarantined",
        "Imported"
    };

    private readonly ApplicationDbContext _context;
    private readonly IQuestionDraftImportService _importService;
    private readonly IQuestionStudioRunControlStore _runControlStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QuestionStudioController> _logger;

    public QuestionStudioController(
        ApplicationDbContext context,
        IQuestionDraftImportService importService,
        IQuestionStudioRunControlStore runControlStore,
        IServiceScopeFactory scopeFactory,
        ILogger<QuestionStudioController> logger)
    {
        _context = context;
        _importService = importService;
        _runControlStore = runControlStore;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [HttpPost("runs/start")]
    public async Task<IActionResult> StartRun([FromBody] StartQuestionStudioRunRequest request, CancellationToken cancellationToken)
    {
        var normalizedMode = request.Mode?.Trim().ToLowerInvariant();
        if (request.TargetDraftCount < 1 || request.TargetDraftCount > 300)
        {
            return ApiBadRequest("invalid_target_draft_count", "targetDraftCount must be between 1 and 300.");
        }

        if (string.IsNullOrWhiteSpace(normalizedMode) || !AllowedModes.Contains(normalizedMode))
        {
            return ApiBadRequest("invalid_mode", "mode must be fast, balanced, quality, or max_draft.");
        }

        var questionTypes = NormalizeQuestionTypes(request.QuestionTypes);
        if (questionTypes.Count == 0)
        {
            return ApiBadRequest("question_types_required", "questionTypes must include at least one supported type.");
        }

        var difficulties = NormalizeDifficulties(request.Difficulties);
        if (difficulties.Count == 0)
        {
            return ApiBadRequest("difficulties_required", "difficulties must include at least one supported difficulty.");
        }

        var document = await _context.Documents.FirstOrDefaultAsync(x => x.Id == request.DocumentId, cancellationToken);
        if (document == null)
        {
            return ApiNotFound("document_not_found", "Document not found.");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        if (document.Status != DocumentStatus.Completed || string.IsNullOrWhiteSpace(document.ExtractedText))
        {
            return ApiBadRequest("document_not_ready", "Document must be completed and have extracted text before starting Question Studio.");
        }

        var activeRun = await _context.QuestionGenerationRuns
            .Where(x => x.DocumentId == document.Id && (x.Status == "Pending" || x.Status == "Running" || x.Status == "Paused"))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeRun != null)
        {
            return Conflict(new
            {
                success = false,
                code = "generation_active",
                message = "A Question Studio run is already active for this document.",
                run = BuildRunPayload(activeRun)
            });
        }

        var run = new QuestionGenerationRun
        {
            DocumentId = document.Id,
            UserId = CurrentUserIdAsString,
            Mode = normalizedMode,
            Status = "Pending",
            Stage = "Created",
            TargetDraftCount = request.TargetDraftCount,
            RequestedQuestionTypesJson = JsonSerializer.Serialize(questionTypes),
            RequestedDifficultiesJson = JsonSerializer.Serialize(difficulties),
            ModelProfileJson = JsonSerializer.Serialize(new { mode = normalizedMode })
        };

        _context.QuestionGenerationRuns.Add(run);
        await _context.SaveChangesAsync(cancellationToken);

        _runControlStore.RegisterRun(run.Id);
        _ = Task.Run(() => RunQuestionStudioJobAsync(run.Id));

        return Accepted(new
        {
            runId = run.Id,
            status = run.Status,
            message = "Question Studio run created.",
            progressUrl = $"/api/question-studio/runs/{run.Id}"
        });
    }

    [HttpGet("runs/{runId:int}")]
    public async Task<IActionResult> GetRun(int runId, CancellationToken cancellationToken)
    {
        var run = await _context.QuestionGenerationRuns
            .Include(x => x.Document)
            .FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run?.Document == null)
        {
            return ApiNotFound("run_not_found", "Question Studio run not found.");
        }

        var authResult = EnsureOwnerAccess(run.Document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        return Ok(BuildRunPayload(run));
    }

    [HttpGet("documents/{documentId:int}/runs/active")]
    public async Task<IActionResult> GetActiveRun(int documentId, CancellationToken cancellationToken)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);
        if (document == null)
        {
            return ApiNotFound("document_not_found", "Document not found.");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var run = await _context.QuestionGenerationRuns
            .Where(x => x.DocumentId == documentId && (x.Status == "Pending" || x.Status == "Running" || x.Status == "Paused"))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return run == null ? NoContent() : Ok(BuildRunPayload(run));
    }

    [HttpPost("runs/{runId:int}/pause")]
    public Task<IActionResult> PauseRun(int runId, CancellationToken cancellationToken)
        => ChangeRunState(runId, "Paused", "Paused", _runControlStore.PauseRun, "run_not_pausable", cancellationToken);

    [HttpPost("runs/{runId:int}/resume")]
    public Task<IActionResult> ResumeRun(int runId, CancellationToken cancellationToken)
        => ChangeRunState(runId, "Running", "Resuming", _runControlStore.ResumeRun, "run_not_resumable", cancellationToken);

    [HttpPost("runs/{runId:int}/cancel")]
    public Task<IActionResult> CancelRun(int runId, CancellationToken cancellationToken)
        => ChangeRunState(runId, "Cancelled", "Cancelled", _runControlStore.CancelRun, "run_not_cancellable", cancellationToken);

    [HttpGet("drafts")]
    public async Task<IActionResult> ListDrafts([FromQuery] QuestionDraftListQuery query, CancellationToken cancellationToken)
    {
        if (query.DocumentId <= 0)
        {
            return ApiBadRequest("document_id_required", "documentId is required.");
        }

        var document = await _context.Documents.FirstOrDefaultAsync(x => x.Id == query.DocumentId, cancellationToken);
        if (document == null)
        {
            return ApiNotFound("document_not_found", "Document not found.");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var draftsQuery = _context.QuestionDrafts
            .Where(x => x.DocumentId == query.DocumentId)
            .AsQueryable();

        if (query.RunId.HasValue)
        {
            draftsQuery = draftsQuery.Where(x => x.GenerationRunId == query.RunId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            draftsQuery = draftsQuery.Where(x => x.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            draftsQuery = draftsQuery.Where(x => x.QuestionType == query.Type);
        }

        if (!string.IsNullOrWhiteSpace(query.Difficulty))
        {
            draftsQuery = draftsQuery.Where(x => x.Difficulty == query.Difficulty);
        }

        if (!string.IsNullOrWhiteSpace(query.Topic))
        {
            draftsQuery = draftsQuery.Where(x => x.TopicTag.Contains(query.Topic));
        }

        if (query.MinScore.HasValue)
        {
            draftsQuery = draftsQuery.Where(x => x.OverallScore >= query.MinScore.Value);
        }

        var total = await draftsQuery.CountAsync(cancellationToken);
        var drafts = await draftsQuery
            .OrderByDescending(x => x.OverallScore)
            .ThenByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            data = drafts.Select(BuildDraftPayload),
            pagination = new
            {
                page,
                pageSize,
                totalItems = total,
                totalPages = (int)Math.Ceiling(total / (double)pageSize)
            }
        });
    }

    [HttpPut("drafts/{draftId:int}")]
    public async Task<IActionResult> UpdateDraft(int draftId, [FromBody] UpdateQuestionDraftRequest request, CancellationToken cancellationToken)
    {
        var draft = await _context.QuestionDrafts
            .Include(x => x.Document)
            .FirstOrDefaultAsync(x => x.Id == draftId, cancellationToken);
        if (draft?.Document == null)
        {
            return ApiNotFound("draft_not_found", "Draft not found.");
        }

        var authResult = EnsureOwnerAccess(draft.Document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        if (draft.Status == "Imported")
        {
            return ApiConflict("draft_already_imported", "Imported drafts cannot be edited.");
        }

        var before = JsonSerializer.Serialize(BuildDraftPayload(draft));
        draft.QuestionText = request.QuestionText?.Trim() ?? draft.QuestionText;
        if (request.Options != null)
        {
            if (request.Options.Count > 6)
            {
                return ApiBadRequest("invalid_options", "options must include 6 items or fewer.");
            }

            draft.OptionsJson = JsonSerializer.Serialize(request.Options.Select((option, index) => new QuestionOption
            {
                Key = ResolveOptionKey(option, index),
                Text = ResolveOptionText(option),
                IsCorrect = IsCorrectOption(option, index, request.CorrectAnswer)
            }).ToList());
        }

        draft.CorrectAnswer = request.CorrectAnswer?.Trim() ?? draft.CorrectAnswer;
        draft.Explanation = request.Explanation?.Trim() ?? draft.Explanation;
        draft.Difficulty = NormalizeDifficulties(new[] { request.Difficulty ?? draft.Difficulty }).FirstOrDefault() ?? draft.Difficulty;
        if (request.TopicTag != null)
        {
            var topicTag = request.TopicTag.Trim();
            if (topicTag.Length > 200)
            {
                return ApiBadRequest("invalid_topic_tag", "topicTag must be 200 characters or fewer.");
            }

            draft.TopicTag = topicTag;
        }

        draft.OverallScore = 0;
        draft.GroundingScore = 0;
        draft.AnswerScore = 0;
        draft.ClarityScore = 0;
        draft.Status = "Draft";
        draft.VerifiedAt = null;
        draft.FailureReason = "Edited manually; reverify recommended.";

        _context.QuestionReviewEvents.Add(new QuestionReviewEvent
        {
            QuestionDraftId = draft.Id,
            UserId = CurrentUserIdAsString,
            Action = "Edit",
            BeforeJson = before,
            AfterJson = JsonSerializer.Serialize(BuildDraftPayload(draft)),
            Note = "Draft edited from Question Studio."
        });

        await _context.SaveChangesAsync(cancellationToken);
        return Ok(BuildDraftPayload(draft));
    }

    [HttpPost("drafts/{draftId:int}/accept")]
    public Task<IActionResult> AcceptDraft(int draftId, CancellationToken cancellationToken)
        => UpdateDraftStatus(draftId, "Accept", "Verified", cancellationToken);

    [HttpPost("drafts/{draftId:int}/reject")]
    public Task<IActionResult> RejectDraft(int draftId, CancellationToken cancellationToken)
        => UpdateDraftStatus(draftId, "Reject", "Rejected", cancellationToken);

    [HttpPost("drafts/{draftId:int}/quarantine")]
    public Task<IActionResult> QuarantineDraft(int draftId, CancellationToken cancellationToken)
        => UpdateDraftStatus(draftId, "Quarantine", "Quarantined", cancellationToken);

    [HttpPost("drafts/{draftId:int}/restore")]
    public async Task<IActionResult> RestoreDraft(int draftId, CancellationToken cancellationToken)
    {
        var draft = await _context.QuestionDrafts.FirstOrDefaultAsync(x => x.Id == draftId, cancellationToken);
        var restoreStatus = draft?.OverallScore >= 0.70 ? "Borderline" : "Draft";
        return await UpdateDraftStatus(draftId, "Restore", restoreStatus, cancellationToken);
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportDrafts([FromBody] ImportQuestionDraftsRequest request, CancellationToken cancellationToken)
    {
        if (request.DocumentId <= 0 || request.DraftIds.Count == 0)
        {
            return ApiBadRequest("invalid_import_request", "documentId and draftIds are required.");
        }

        var document = await _context.Documents.FirstOrDefaultAsync(x => x.Id == request.DocumentId, cancellationToken);
        if (document == null)
        {
            return ApiNotFound("document_not_found", "Document not found.");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var result = await _importService.ImportAsync(request.DocumentId, request.DraftIds, CurrentUserIdAsString, cancellationToken);
        return Ok(new
        {
            result.ImportedCount,
            result.SkippedCount,
            skippedDraftIds = result.SkippedDraftIds
        });
    }

    private async Task<IActionResult> UpdateDraftStatus(int draftId, string action, string status, CancellationToken cancellationToken)
    {
        if (!AllowedStatuses.Contains(status))
        {
            return ApiBadRequest("invalid_status", "Invalid draft status.");
        }

        var draft = await _context.QuestionDrafts
            .Include(x => x.Document)
            .FirstOrDefaultAsync(x => x.Id == draftId, cancellationToken);
        if (draft?.Document == null)
        {
            return ApiNotFound("draft_not_found", "Draft not found.");
        }

        var authResult = EnsureOwnerAccess(draft.Document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        if (draft.Status == "Imported")
        {
            return ApiConflict("draft_already_imported", "Imported drafts cannot be changed.");
        }

        var before = JsonSerializer.Serialize(new { draft.Status });
        draft.Status = status;
        _context.QuestionReviewEvents.Add(new QuestionReviewEvent
        {
            QuestionDraftId = draft.Id,
            UserId = CurrentUserIdAsString,
            Action = action,
            BeforeJson = before,
            AfterJson = JsonSerializer.Serialize(new { draft.Status }),
            Note = $"Draft status changed to {status}."
        });

        await _context.SaveChangesAsync(cancellationToken);
        return Ok(BuildDraftPayload(draft));
    }

    private async Task<IActionResult> ChangeRunState(
        int runId,
        string status,
        string stage,
        Func<int, bool> transition,
        string conflictCode,
        CancellationToken cancellationToken)
    {
        var run = await _context.QuestionGenerationRuns
            .Include(x => x.Document)
            .FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run?.Document == null)
        {
            return ApiNotFound("run_not_found", "Question Studio run not found.");
        }

        var authResult = EnsureOwnerAccess(run.Document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var transitionAllowed = status switch
        {
            "Paused" => run.Status is "Pending" or "Running",
            "Running" => run.Status == "Paused",
            "Cancelled" => run.Status is "Pending" or "Running" or "Paused",
            _ => false
        };
        if (!transitionAllowed)
        {
            return ApiConflict(conflictCode, "Question Studio run cannot change to the requested state.");
        }

        if (!transition(runId))
        {
            return ApiConflict(conflictCode, "Question Studio run cannot change to the requested state.");
        }

        run.Status = status;
        run.Stage = stage;
        if (status == "Cancelled")
        {
            run.CompletedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Ok(BuildRunPayload(run));
    }

    private async Task RunQuestionStudioJobAsync(int runId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<QuestionStudioOrchestrator>();
            await orchestrator.RunAsync(runId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Question Studio background job {RunId} failed", runId);
        }
    }

    private static object BuildRunPayload(QuestionGenerationRun run)
        => new
        {
            runId = run.Id,
            run.DocumentId,
            run.Status,
            run.Stage,
            progressPercent = CalculateRunProgressPercent(run),
            run.TargetDraftCount,
            run.GeneratedDraftCount,
            run.VerifiedDraftCount,
            run.DuplicateCount,
            run.RejectedCount,
            run.BorderlineCount,
            run.QuarantinedCount,
            run.ImportedCount,
            run.ErrorMessage,
            metrics = ParseJsonObject(run.MetricsJson),
            run.CreatedAt,
            run.StartedAt,
            run.CompletedAt
        };

    private static int CalculateRunProgressPercent(QuestionGenerationRun run)
    {
        if (string.Equals(run.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (string.Equals(run.Status, "Failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(run.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Clamp(EstimateDraftProgress(run, 0, 100), 0, 99);
        }

        return run.Stage switch
        {
            "Created" => 0,
            "ExtractingSourceUnits" => 5,
            "GeneratingCanonical" => Math.Max(10, EstimateDraftProgress(run, 10, 55)),
            "VerifyingCanonical" => Math.Max(55, EstimateDraftProgress(run, 55, 65)),
            "DeduplicatingCanonical" => Math.Max(65, EstimateDraftProgress(run, 65, 75)),
            "GeneratingVariants" => Math.Max(75, EstimateDraftProgress(run, 75, 90)),
            "VerifyingVariants" => Math.Max(90, EstimateDraftProgress(run, 90, 96)),
            "DeduplicatingVariants" => Math.Max(96, EstimateDraftProgress(run, 96, 99)),
            _ => Math.Clamp(EstimateDraftProgress(run, 0, 95), 0, 95)
        };
    }

    private static int EstimateDraftProgress(QuestionGenerationRun run, int startPercent, int endPercent)
    {
        var target = Math.Max(1, run.TargetDraftCount);
        var ratio = Math.Clamp(run.GeneratedDraftCount / (double)target, 0, 1);
        return startPercent + (int)Math.Round((endPercent - startPercent) * ratio);
    }

    private static object BuildDraftPayload(QuestionDraft draft)
        => new
        {
            draft.Id,
            draft.DocumentId,
            draft.GenerationRunId,
            draft.Status,
            draft.DraftKind,
            draft.ParentDraftId,
            draft.QuestionText,
            draft.QuestionType,
            options = BuildOptionPayloads(draft.OptionsJson),
            draft.CorrectAnswer,
            draft.Explanation,
            draft.Difficulty,
            draft.LearningObjective,
            draft.TopicTag,
            draft.GroundingScore,
            draft.AnswerScore,
            draft.ClarityScore,
            draft.DuplicateScore,
            draft.OverallScore,
            duplicateWarning = draft.DuplicateScore < 1.0,
            draft.FailureReason,
            draft.SourceEvidence,
            draft.CreatedAt,
            draft.VerifiedAt,
            draft.ImportedAt
        };

    private static object BuildOptionPayloads(string? optionsJson)
        => QuestionStudioDraftFactory.ParseOptions(optionsJson)
            .Select(option => new
            {
                key = option.Key,
                text = option.Text,
                isCorrect = option.IsCorrect
            })
            .ToList();

    private static List<string> NormalizeQuestionTypes(IEnumerable<string>? values)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MultipleChoice",
            "Flashcard",
            "ShortAnswer",
            "TrueFalse",
            "FillBlank",
            "FillInTheBlank",
            "MatchPair"
        };

        return (values ?? Array.Empty<string>())
            .Select(x => x?.Trim() ?? string.Empty)
            .Where(x => allowed.Contains(x))
            .Select(x => x == "FillBlank" ? "FillInTheBlank" : x)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> NormalizeDifficulties(IEnumerable<string?>? values)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Easy", "Medium", "Hard" };
        return (values ?? Array.Empty<string>())
            .Select(x => x?.Trim() ?? string.Empty)
            .Where(x => allowed.Contains(x))
            .Select(x => char.ToUpperInvariant(x[0]) + x[1..].ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static object? ParseJsonObject(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(json ?? "{}");
        }
        catch
        {
            return new { };
        }
    }

    private static object ParseJsonArray(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(json ?? "[]") ?? Array.Empty<object>();
        }
        catch
        {
            return Array.Empty<object>();
        }
    }

    private static string ResolveOptionKey(string option, int index)
    {
        var match = System.Text.RegularExpressions.Regex.Match((option ?? string.Empty).Trim(), @"^([A-Fa-f])[\).\:\-]\s*(.+)$");
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : ((char)('A' + index)).ToString();
    }

    private static string ResolveOptionText(string option)
    {
        var value = (option ?? string.Empty).Trim();
        var match = System.Text.RegularExpressions.Regex.Match(value, @"^([A-Fa-f])[\).\:\-]\s*(.+)$");
        return match.Success ? match.Groups[2].Value.Trim() : value;
    }

    private static bool IsCorrectOption(string option, int index, string? correctAnswer)
    {
        if (string.IsNullOrWhiteSpace(correctAnswer))
        {
            return false;
        }

        var key = ResolveOptionKey(option, index);
        var text = ResolveOptionText(option);
        return string.Equals(key, correctAnswer.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, correctAnswer.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class StartQuestionStudioRunRequest
{
    public int DocumentId { get; set; }
    public int TargetDraftCount { get; set; } = 30;
    public string Mode { get; set; } = "balanced";
    public List<string> QuestionTypes { get; set; } = new() { "MultipleChoice", "Flashcard", "ShortAnswer" };
    public List<string> Difficulties { get; set; } = new() { "Easy", "Medium", "Hard" };
}

public sealed class QuestionDraftListQuery
{
    public int DocumentId { get; set; }
    public int? RunId { get; set; }
    public string? Status { get; set; }
    public string? Type { get; set; }
    public string? Difficulty { get; set; }
    public string? Topic { get; set; }
    public double? MinScore { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class UpdateQuestionDraftRequest
{
    public string? QuestionText { get; set; }
    public List<string>? Options { get; set; }
    public string? CorrectAnswer { get; set; }
    public string? Explanation { get; set; }
    public string? Difficulty { get; set; }
    public string? TopicTag { get; set; }
}

public sealed class ImportQuestionDraftsRequest
{
    public int DocumentId { get; set; }
    public List<int> DraftIds { get; set; } = new();
}
