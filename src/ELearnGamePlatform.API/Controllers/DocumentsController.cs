using ELearnGamePlatform.API.Contracts;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using ELearnGamePlatform.API.Services;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IQuestionRepository _questionRepository;
    private readonly ILogger<DocumentsController> _logger;
    private readonly IDocumentProcessingJobStore _documentJobStore;
    private readonly IDocumentIngestionService _documentIngestionService;
    private readonly IWorkspaceService _workspaceService;

    public DocumentsController(
        IDocumentRepository documentRepository,
        IQuestionRepository questionRepository,
        ILogger<DocumentsController> logger,
        IDocumentProcessingJobStore documentJobStore,
        IDocumentIngestionService documentIngestionService,
        IWorkspaceService workspaceService)
    {
        _documentRepository = documentRepository;
        _questionRepository = questionRepository;
        _logger = logger;
        _documentJobStore = documentJobStore;
        _documentIngestionService = documentIngestionService;
        _workspaceService = workspaceService;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadDocument([FromForm] IFormFile file, [FromForm] string userId)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file provided");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest("UserId is required");
        }

        try
        {
            var defaultWorkspace = await _workspaceService.EnsureDefaultWorkspaceAsync(userId);
            await _workspaceService.AttachOrphanDocumentsAsync(userId, defaultWorkspace.Id);

            var createdDocument = await _documentIngestionService.UploadDocumentAsync(file, userId, defaultWorkspace.Id);
            _documentJobStore.TryGetJob(createdDocument.Id, out var progressState);
            _documentIngestionService.StartBackgroundProcessing(createdDocument.Id);

            return Ok(new
            {
                id = createdDocument.Id,
                workspaceId = defaultWorkspace.Id,
                fileName = createdDocument.FileName,
                status = createdDocument.Status.ToString(),
                message = "File uploaded successfully. Processing started.",
                progressUrl = $"/api/documents/{createdDocument.Id}/progress",
                progress = JobProgressPayloadFactory.BuildDocument(progressState, createdDocument)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading document");
            return ex is InvalidOperationException
                ? BadRequest(ex.Message)
                : StatusCode(500, "Error uploading file");
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDocument(int id)
    {
        var document = await _documentRepository.GetByIdAsync(id);
        
        if (document == null)
        {
            return NotFound();
        }

        return Ok(BuildDocumentPayload(document, questionsCount: document.Questions.Count));
    }

    [HttpGet("{id}/progress")]
    public async Task<IActionResult> GetDocumentProgress(int id)
    {
        var document = await _documentRepository.GetByIdAsync(id);

        if (document == null)
        {
            return NotFound("Document not found");
        }

        _documentJobStore.TryGetJob(id, out var progressState);
        return Ok(JobProgressPayloadFactory.BuildDocument(progressState, document));
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserDocuments(string userId)
    {
        var documents = await _documentRepository.GetByUserAsync(userId);
        var questionsCountMap = new Dictionary<int, int>();

        foreach (var document in documents)
        {
            var questions = await _questionRepository.GetByDocumentIdAsync(document.Id);
            questionsCountMap[document.Id] = questions.Count();
        }
        
        // Add questions count to each document
        var documentsWithMeta = documents.Select(doc => BuildDocumentPayload(
            doc,
            questionsCountMap.TryGetValue(doc.Id, out var count) ? count : 0));
        
        return Ok(documentsWithMeta);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDocument(int id)
    {
        var document = await _documentRepository.GetByIdAsync(id);
        
        if (document == null)
        {
            return NotFound();
        }

        // Delete file
        if (System.IO.File.Exists(document.FilePath))
        {
            System.IO.File.Delete(document.FilePath);
        }

        await _documentRepository.DeleteAsync(id);
        return NoContent();
    }

    private object BuildDocumentPayload(Document doc, int questionsCount)
    {
        _documentJobStore.TryGetJob(doc.Id, out var progressState);

        return new
        {
            id = doc.Id,
            fileName = doc.FileName,
            fileType = doc.FileType,
            filePath = doc.FilePath,
            fileSize = doc.FileSize,
            extractedText = doc.ExtractedText,
            mainTopics = doc.GetMainTopics(),
            keyPoints = doc.GetKeyPoints(),
            coverageChunkCount = doc.GetCoverageMap().Count,
            summary = doc.Summary,
            language = doc.Language,
            status = doc.Status,
            uploadedBy = doc.UploadedBy,
            createdAt = doc.CreatedAt,
            updatedAt = doc.UpdatedAt,
            questionsCount,
            processingProgress = JobProgressPayloadFactory.BuildDocument(progressState, doc)
        };
    }
}
