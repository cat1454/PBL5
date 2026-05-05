using ELearnGamePlatform.API.Contracts;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ELearnGamePlatform.API.Services;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentsController : AuthenticatedControllerBase
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IQuestionRepository _questionRepository;
    private readonly ILogger<DocumentsController> _logger;
    private readonly IDocumentProcessingJobStore _documentJobStore;
    private readonly IDocumentIngestionService _documentIngestionService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IContentAnalyzer _contentAnalyzer;

    public DocumentsController(
        IDocumentRepository documentRepository,
        IQuestionRepository questionRepository,
        ILogger<DocumentsController> logger,
        IDocumentProcessingJobStore documentJobStore,
        IDocumentIngestionService documentIngestionService,
        IWorkspaceService workspaceService,
        IContentAnalyzer contentAnalyzer)
    {
        _documentRepository = documentRepository;
        _questionRepository = questionRepository;
        _logger = logger;
        _documentJobStore = documentJobStore;
        _documentIngestionService = documentIngestionService;
        _workspaceService = workspaceService;
        _contentAnalyzer = contentAnalyzer;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadDocument([FromForm] IFormFile file, [FromForm] string userId)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file provided");
        }

        if (CurrentUserId == null)
        {
            return Unauthorized("User context is required");
        }

        try
        {
            var currentUserId = CurrentUserIdAsString;
            var defaultWorkspace = await _workspaceService.EnsureDefaultWorkspaceAsync(currentUserId);
            await _workspaceService.AttachOrphanDocumentsAsync(currentUserId, defaultWorkspace.Id);

            var createdDocument = await _documentIngestionService.UploadDocumentAsync(file, currentUserId, defaultWorkspace.Id);
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

        var authResult = EnsureOwnerOrAdmin(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
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

        var authResult = EnsureOwnerOrAdmin(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        _documentJobStore.TryGetJob(id, out var progressState);
        return Ok(JobProgressPayloadFactory.BuildDocument(progressState, document));
    }

    [HttpGet("{id}/structure")]
    public async Task<IActionResult> GetDocumentStructure(int id)
    {
        var document = await _documentRepository.GetByIdAsync(id);
        if (document == null)
        {
            return NotFound("Document not found");
        }

        var authResult = EnsureOwnerOrAdmin(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        return Ok(BuildDocumentStructurePayload(document));
    }

    [HttpPost("{id}/analyze-structure")]
    public async Task<IActionResult> AnalyzeDocumentStructure(int id)
    {
        var document = await _documentRepository.GetByIdAsync(id);
        if (document == null)
        {
            return NotFound("Document not found");
        }

        var authResult = EnsureOwnerOrAdmin(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        if (string.IsNullOrWhiteSpace(document.ExtractedText))
        {
            return BadRequest("Document has not been processed yet");
        }

        try
        {
            var processedContent = await _contentAnalyzer.AnalyzeContentAsync(document.ExtractedText);
            document.SetMainTopics(processedContent.MainTopics);
            document.SetKeyPoints(processedContent.KeyPoints);
            document.SetCoverageMap(processedContent.CoverageMap);
            document.SetProcessingMetadata(new DocumentProcessingMetadata
            {
                DocumentType = processedContent.DocumentType,
                Language = processedContent.Language,
                Title = processedContent.Title,
                MainContentStartPage = processedContent.MainContentStartPage,
                Structure = processedContent.Structure,
                ExcludedContent = processedContent.ExcludedContent
            });
            document.Summary = processedContent.Summary;
            document.Language = processedContent.Language;
            document.UpdatedAt = DateTime.UtcNow;

            await _documentRepository.UpdateAsync(document.Id, document);
            return Ok(BuildDocumentStructurePayload(document));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error re-analyzing document structure for {DocumentId}", id);
            return StatusCode(500, "Error analyzing document structure");
        }
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserDocuments(string userId)
    {
        var authResult = EnsureCurrentUserMatches(userId);
        if (authResult != null)
        {
            return authResult;
        }

        var documents = await _documentRepository.GetByUserAsync(CurrentUserIdAsString);
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

        var authResult = EnsureOwnerOrAdmin(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
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
        var processingMetadata = doc.GetProcessingMetadata();

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
            documentType = processingMetadata.DocumentType,
            title = processingMetadata.Title,
            mainContentStartPage = processingMetadata.MainContentStartPage,
            structure = processingMetadata.Structure,
            excludedContent = processingMetadata.ExcludedContent,
            status = doc.Status,
            uploadedBy = doc.UploadedBy,
            createdAt = doc.CreatedAt,
            updatedAt = doc.UpdatedAt,
            questionsCount,
            processingProgress = JobProgressPayloadFactory.BuildDocument(progressState, doc)
        };
    }

    private object BuildDocumentStructurePayload(Document doc)
    {
        _documentJobStore.TryGetJob(doc.Id, out var progressState);
        var processingMetadata = doc.GetProcessingMetadata();
        var structure = processingMetadata.Structure ?? new List<DocumentSectionDescriptor>();

        return new
        {
            id = doc.Id,
            fileName = doc.FileName,
            status = doc.Status,
            documentType = processingMetadata.DocumentType,
            title = processingMetadata.Title,
            language = processingMetadata.Language ?? doc.Language,
            mainContentStartPage = processingMetadata.MainContentStartPage,
            analysisStatus = doc.Status == DocumentStatus.Completed
                ? "ready"
                : doc.Status == DocumentStatus.Failed
                    ? "failed"
                    : "processing",
            isStructureReady = structure.Count > 0,
            sectionCount = structure.Count,
            structure = structure.Select(section => new
            {
                sectionKey = section.SectionKey,
                heading = section.Heading,
                classification = section.Classification,
                startPage = section.StartPage,
                endPage = section.EndPage,
                chunkCount = section.ChunkIds?.Count ?? 0,
                chunkIds = section.ChunkIds
            }),
            excludedContent = processingMetadata.ExcludedContent,
            processingProgress = JobProgressPayloadFactory.BuildDocument(progressState, doc)
        };
    }
}
