using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.API.Services;
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
            resultHint = $"Khi status=completed, gọi /api/questions/document/{request.DocumentId} để lấy câu hỏi mới"
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

            List<Question> questions;
            
            if (request.QuestionType.HasValue)
            {
                questions = await _questionGenerator.GenerateQuestionsByTypeAsync(
                    request.DocumentId,
                    document.ExtractedText,
                    request.QuestionType.Value,
                    request.Count);
            }
            else
            {
                questions = await _questionGenerator.GenerateQuestionsAsync(
                    request.DocumentId,
                    document.ExtractedText,
                    request.Count);
            }

            await _questionRepository.ReplaceByDocumentIdAsync(request.DocumentId, questions);

            _logger.LogInformation("Generated and replaced {Count} questions for document {DocumentId}", questions.Count, request.DocumentId);

            return Ok(new
            {
                documentId = request.DocumentId,
                questionsGenerated = questions.Count,
                questions = questions
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
                state.Message = "Đang kiểm tra tài liệu";
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
                    state.Error = "Document not found";
                    state.Message = "Không tìm thấy tài liệu";
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
                    state.Error = "Document has not been processed yet";
                    state.Message = "Tài liệu chưa được xử lý nội dung";
                });
                return;
            }

            var progress = new Progress<QuestionGenerationProgressUpdate>(update =>
            {
                _jobStore.UpdateJob(jobId, state =>
                {
                    state.Status = update.Stage == "failed" ? "failed" : "running";
                    state.Percent = update.Percent;
                    state.Stage = update.Stage;
                    state.Message = update.Message;
                    state.Current = update.Current;
                    state.Total = update.Total;
                    state.TopicTag = update.TopicTag;
                });
            });

            List<Question> questions;
            if (request.QuestionType.HasValue)
            {
                questions = await questionGenerator.GenerateQuestionsByTypeAsync(
                    request.DocumentId,
                    document.ExtractedText,
                    request.QuestionType.Value,
                    request.Count,
                    progress);
            }
            else
            {
                questions = await questionGenerator.GenerateQuestionsAsync(
                    request.DocumentId,
                    document.ExtractedText,
                    request.Count,
                    progress);
            }

            _jobStore.UpdateJob(jobId, state =>
            {
                state.Percent = Math.Max(state.Percent, 96);
                state.Stage = "saving-questions";
                state.Message = "Đang lưu câu hỏi vào hệ thống";
            });

            await questionRepository.ReplaceByDocumentIdAsync(request.DocumentId, questions);

            _jobStore.UpdateJob(jobId, state =>
            {
                state.Status = "completed";
                state.Percent = 100;
                state.Stage = "completed";
                state.Message = "Hoàn tất sinh câu hỏi";
                state.QuestionsGenerated = questions.Count;
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
                state.Error = ex.Message;
                state.Message = "Sinh câu hỏi thất bại";
            });
        }
    }

    [HttpGet("document/{documentId}")]
    public async Task<IActionResult> GetQuestionsByDocument(int documentId)
    {
        var questions = await _questionRepository.GetByDocumentIdAsync(documentId);
        return Ok(questions.Select(q => new
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
            q.CreatedAt
        }));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetQuestion(int id)
    {
        var question = await _questionRepository.GetByIdAsync(id);
        
        if (question == null)
        {
            return NotFound();
        }

        return Ok(question);
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
        await _questionRepository.UpdateAsync(id, question);
        
        return Ok(question);
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
}

public class GenerateQuestionsRequest
{
    public required int DocumentId { get; set; }
    public int Count { get; set; } = 10;
    public QuestionType? QuestionType { get; set; }
}
