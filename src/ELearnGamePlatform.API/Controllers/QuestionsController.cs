using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QuestionsController : ControllerBase
{
    private readonly IQuestionRepository _questionRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IQuestionGenerator _questionGenerator;
    private readonly IQuestionGenerationJobStore _jobStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QuestionsController> _logger;

    public QuestionsController(
        IQuestionRepository questionRepository,
        IDocumentRepository documentRepository,
        IQuestionGenerator questionGenerator,
        IQuestionGenerationJobStore jobStore,
        IServiceScopeFactory scopeFactory,
        ILogger<QuestionsController> logger)
    {
        _questionRepository = questionRepository;
        _documentRepository = documentRepository;
        _questionGenerator = questionGenerator;
        _jobStore = jobStore;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [HttpPost("generate/start")]
    public IActionResult StartGenerateQuestions([FromBody] GenerateQuestionsRequest request)
    {
        if (request.Count < 1 || request.Count > 50)
        {
            return BadRequest("Count must be between 1 and 50");
        }

        var jobId = _jobStore.CreateJob(request.DocumentId, request.Count, request.QuestionType?.ToString());

        _ = Task.Run(() => RunGenerateQuestionsJobAsync(jobId, request));

        return Accepted(new
        {
            jobId,
            status = "queued",
            progressUrl = $"/api/questions/generate/progress/{jobId}",
            resultHint = $"Khi status=completed, goi /api/questions/document/{request.DocumentId} de lay cau hoi moi"
        });
    }

    [HttpGet("generate/progress/{jobId}")]
    public IActionResult GetGenerateProgress(string jobId)
    {
        if (!_jobStore.TryGetJob(jobId, out var state) || state == null)
        {
            return NotFound("Job not found");
        }

        return Ok(state);
    }

    [HttpPost("generate")]
    public async Task<IActionResult> GenerateQuestions([FromBody] GenerateQuestionsRequest request)
    {
        try
        {
            if (request.Count < 1 || request.Count > 50)
            {
                return BadRequest("Count must be between 1 and 50");
            }

            var document = await _documentRepository.GetByIdAsync(request.DocumentId);
            if (document == null)
            {
                return NotFound("Document not found");
            }

            if (string.IsNullOrEmpty(document.ExtractedText))
            {
                return BadRequest("Document has not been processed yet");
            }

            var processedContent = BuildProcessedContentFromDocument(document);
            List<Question> questions;
            if (request.QuestionType.HasValue)
            {
                questions = await _questionGenerator.GenerateQuestionsByTypeAsync(
                    request.DocumentId,
                    document.ExtractedText,
                    request.QuestionType.Value,
                    request.Count,
                    processedContent);
            }
            else
            {
                questions = await _questionGenerator.GenerateQuestionsAsync(
                    request.DocumentId,
                    document.ExtractedText,
                    request.Count,
                    processedContent);
            }

            await _questionRepository.ReplaceByDocumentIdAsync(request.DocumentId, questions);

            _logger.LogInformation("Generated and replaced {Count} questions for document {DocumentId}", questions.Count, request.DocumentId);

            return Ok(new
            {
                documentId = request.DocumentId,
                questionsGenerated = questions.Count,
                questions = questions.Select(BuildQuestionPayload)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating questions for document: {DocumentId}", request.DocumentId);
            return StatusCode(500, "Error generating questions");
        }
    }

    private async Task RunGenerateQuestionsJobAsync(string jobId, GenerateQuestionsRequest request)
    {
        try
        {
            _jobStore.UpdateJob(jobId, state =>
            {
                state.Status = "running";
                state.Percent = 2;
                state.Stage = "validating-document";
                state.StageLabel = "Kiem tra dau vao";
                state.StageIndex = 1;
                state.StageCount = 7;
                state.Message = "Dang kiem tra tai lieu";
                state.Detail = "Xac minh document va noi dung dau vao";
                state.UnitLabel = null;
                state.Current = null;
                state.Total = null;
                state.TopicTag = null;
                state.Error = null;
                UpdateEta(state);
            });

            using var scope = _scopeFactory.CreateScope();
            var questionRepository = scope.ServiceProvider.GetRequiredService<IQuestionRepository>();
            var documentRepository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var questionGenerator = scope.ServiceProvider.GetRequiredService<IQuestionGenerator>();

            var document = await documentRepository.GetByIdAsync(request.DocumentId);
            if (document == null)
            {
                _jobStore.UpdateJob(jobId, state =>
                {
                    state.Status = "failed";
                    state.Percent = 100;
                    state.Stage = "failed";
                    state.StageLabel = "That bai";
                    state.Message = "Khong tim thay tai lieu";
                    state.Detail = "DocumentId khong ton tai trong he thong";
                    state.Error = "Document not found";
                    state.StageIndex = 7;
                    state.StageCount = 7;
                    state.EstimatedRemainingSeconds = 0;
                    UpdateEta(state);
                });
                return;
            }

            if (string.IsNullOrWhiteSpace(document.ExtractedText))
            {
                _jobStore.UpdateJob(jobId, state =>
                {
                    state.Status = "failed";
                    state.Percent = 100;
                    state.Stage = "failed";
                    state.StageLabel = "That bai";
                    state.Message = "Tai lieu chua du noi dung de sinh cau hoi";
                    state.Detail = "Can xu ly xong ExtractedText truoc khi tao cau hoi";
                    state.Error = "Document has not been processed yet";
                    state.StageIndex = 7;
                    state.StageCount = 7;
                    state.EstimatedRemainingSeconds = 0;
                    UpdateEta(state);
                });
                return;
            }

            var progress = new Progress<QuestionGenerationProgressUpdate>(update =>
            {
                _jobStore.UpdateJob(jobId, state =>
                {
                    ApplyProgressUpdate(state, update);
                });
            });

            var processedContent = BuildProcessedContentFromDocument(document);
            List<Question> questions;
            if (request.QuestionType.HasValue)
            {
                questions = await questionGenerator.GenerateQuestionsByTypeAsync(
                    request.DocumentId,
                    document.ExtractedText,
                    request.QuestionType.Value,
                    request.Count,
                    processedContent,
                    progress);
            }
            else
            {
                questions = await questionGenerator.GenerateQuestionsAsync(
                    request.DocumentId,
                    document.ExtractedText,
                    request.Count,
                    processedContent,
                    progress);
            }

            _jobStore.UpdateJob(jobId, state =>
            {
                state.Status = "running";
                state.Percent = Math.Max(state.Percent, 96);
                state.Stage = "saving-questions";
                state.StageLabel = "Luu ket qua";
                state.StageIndex = 7;
                state.StageCount = 7;
                state.Message = "Dang luu cau hoi vao he thong";
                state.Detail = $"Ghi {questions.Count} cau hoi vao database";
                state.Current = questions.Count;
                state.Total = questions.Count;
                state.UnitLabel = "cau hoi";
                state.EstimatedRemainingSeconds = 1;
                UpdateEta(state);
            });

            await questionRepository.ReplaceByDocumentIdAsync(request.DocumentId, questions);

            _jobStore.UpdateJob(jobId, state =>
            {
                state.Status = "completed";
                state.Percent = 100;
                state.Stage = "completed";
                state.StageLabel = "Hoan tat";
                state.StageIndex = 7;
                state.StageCount = 7;
                state.Message = "Hoan tat sinh cau hoi";
                state.Detail = $"Da tao va luu {questions.Count} cau hoi";
                state.QuestionsGenerated = questions.Count;
                state.EstimatedRemainingSeconds = 0;
                UpdateEta(state);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running question generation job {JobId} for document {DocumentId}", jobId, request.DocumentId);
            _jobStore.UpdateJob(jobId, state =>
            {
                state.Status = "failed";
                state.Percent = 100;
                state.Stage = "failed";
                state.StageLabel = "That bai";
                state.StageIndex = 7;
                state.StageCount = 7;
                state.Error = ex.Message;
                state.Message = "Sinh cau hoi that bai";
                state.Detail = ex.Message;
                state.EstimatedRemainingSeconds = 0;
                UpdateEta(state);
            });
        }
    }

    private static void ApplyProgressUpdate(QuestionGenerationJobState state, QuestionGenerationProgressUpdate update)
    {
        state.Status = update.Stage switch
        {
            "failed" => "failed",
            "completed" => "running",
            _ => "running"
        };
        state.Percent = Math.Clamp(update.Percent, 0, 100);
        state.Stage = string.IsNullOrWhiteSpace(update.Stage) ? state.Stage : update.Stage;
        state.StageLabel = string.IsNullOrWhiteSpace(update.StageLabel) ? state.StageLabel : update.StageLabel;
        state.Message = update.Message;
        state.Detail = update.Detail;
        state.Current = update.Current;
        state.Total = update.Total;
        state.UnitLabel = update.UnitLabel;
        state.StageIndex = update.StageIndex ?? state.StageIndex;
        state.StageCount = update.StageCount ?? state.StageCount;
        state.TopicTag = update.TopicTag;

        if (state.Status == "failed")
        {
            state.Error = update.Message;
            state.EstimatedRemainingSeconds = 0;
        }

        UpdateEta(state);
    }

    private static void UpdateEta(QuestionGenerationJobState state)
    {
        var elapsedSeconds = Math.Max(0, (int)Math.Round((DateTime.UtcNow - state.CreatedAt).TotalSeconds));
        state.ElapsedSeconds = elapsedSeconds;

        if (state.Status is "completed" or "failed")
        {
            state.EstimatedRemainingSeconds = 0;
            return;
        }

        if (state.Percent <= 3)
        {
            state.EstimatedRemainingSeconds = null;
            return;
        }

        var estimatedTotalSeconds = elapsedSeconds / Math.Max(0.03d, state.Percent / 100d);
        var estimatedRemaining = Math.Max(1, (int)Math.Round(estimatedTotalSeconds - elapsedSeconds));
        state.EstimatedRemainingSeconds = estimatedRemaining;
    }

    private static ProcessedContent BuildProcessedContentFromDocument(Document document)
    {
        return new ProcessedContent
        {
            MainTopics = document.GetMainTopics(),
            KeyPoints = document.GetKeyPoints(),
            Summary = document.Summary,
            Language = document.Language,
            CoverageMap = document.GetCoverageMap()
        };
    }

    [HttpGet("document/{documentId}")]
    public async Task<IActionResult> GetQuestionsByDocument(int documentId)
    {
        var questions = await _questionRepository.GetByDocumentIdAsync(documentId);
        return Ok(questions.Select(BuildQuestionPayload));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetQuestion(int id)
    {
        var question = await _questionRepository.GetByIdAsync(id);
        if (question == null)
        {
            return NotFound();
        }

        return Ok(BuildQuestionPayload(question));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateQuestion(int id, [FromBody] Question question)
    {
        var existing = await _questionRepository.GetByIdAsync(id);
        if (existing == null)
        {
            return NotFound();
        }

        question.Id = id;
        question.VerifierScore = null;
        question.SetVerifierIssues(new List<string>
        {
            "Cau hoi da duoc chinh sua thu cong sau khi verifier chay.",
            "Can sinh lai neu muon co verifier score moi."
        });
        await _questionRepository.UpdateAsync(id, question);

        return Ok(BuildQuestionPayload(question));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteQuestion(int id)
    {
        var question = await _questionRepository.GetByIdAsync(id);
        if (question == null)
        {
            return NotFound();
        }

        await _questionRepository.DeleteAsync(id);
        return NoContent();
    }

    private static object BuildQuestionPayload(Question q)
    {
        var issues = q.GetVerifierIssues();
        return new
        {
            q.Id,
            q.DocumentId,
            q.QuestionText,
            q.QuestionType,
            options = q.GetOptions(),
            q.CorrectAnswer,
            q.Explanation,
            q.Difficulty,
            q.Topic,
            quality = new
            {
                score = q.VerifierScore,
                issues,
                isLowConfidence = q.VerifierScore.HasValue && q.VerifierScore.Value < 70,
                isUnknown = !q.VerifierScore.HasValue
            },
            q.CreatedAt
        };
    }
}

public class GenerateQuestionsRequest
{
    public required int DocumentId { get; set; }
    public int Count { get; set; } = 10;
    public QuestionType? QuestionType { get; set; }
}
