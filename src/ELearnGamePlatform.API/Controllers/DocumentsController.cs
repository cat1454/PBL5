using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ELearnGamePlatform.API.Configuration;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IQuestionRepository _questionRepository;
    private readonly IContentAnalyzer _contentAnalyzer;
    private readonly ILogger<DocumentsController> _logger;
    private readonly IEnumerable<IDocumentProcessor> _documentProcessors;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly FileUploadSettings _fileUploadSettings;

    public DocumentsController(
        IDocumentRepository documentRepository,
        IQuestionRepository questionRepository,
        IContentAnalyzer contentAnalyzer,
        ILogger<DocumentsController> logger,
        IEnumerable<IDocumentProcessor> documentProcessors,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<FileUploadSettings> fileUploadOptions)
    {
        _documentRepository = documentRepository;
        _questionRepository = questionRepository;
        _contentAnalyzer = contentAnalyzer;
        _logger = logger;
        _documentProcessors = documentProcessors;
        _serviceScopeFactory = serviceScopeFactory;
        _fileUploadSettings = fileUploadOptions.Value;
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
            if (file.Length > _fileUploadSettings.MaxFileSizeInBytes)
            {
                return BadRequest($"File size exceeds {_fileUploadSettings.MaxFileSizeInMB} MB limit");
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_fileUploadSettings.IsExtensionAllowed(extension))
            {
                return BadRequest($"File type {extension} is not supported. Allowed: {string.Join(", ", _fileUploadSettings.AllowedExtensions)}");
            }

            var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
            Directory.CreateDirectory(uploadsPath);

            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadsPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Create document record
            var document = new Document
            {
                FileName = file.FileName,
                FileType = extension.TrimStart('.'),
                FilePath = filePath,
                FileSize = file.Length,
                UploadedBy = userId,
                Status = DocumentStatus.Uploaded
            };

            var createdDocument = await _documentRepository.CreateAsync(document);

            // Start background processing with new scope
            _ = Task.Run(() => ProcessDocumentAsync(createdDocument.Id));

            return Ok(new
            {
                id = createdDocument.Id,
                fileName = createdDocument.FileName,
                status = createdDocument.Status.ToString(),
                message = "File uploaded successfully. Processing started."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading document");
            return StatusCode(500, "Error uploading file");
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

        return Ok(document);
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
        var documentsWithMeta = documents.Select(doc => new
        {
            id = doc.Id,
            fileName = doc.FileName,
            fileType = doc.FileType,
            filePath = doc.FilePath,
            fileSize = doc.FileSize,
            extractedText = doc.ExtractedText,
            mainTopics = doc.GetMainTopics(),
            keyPoints = doc.GetKeyPoints(),
            summary = doc.Summary,
            language = doc.Language,
            status = doc.Status,
            uploadedBy = doc.UploadedBy,
            createdAt = doc.CreatedAt,
            updatedAt = doc.UpdatedAt,
            questionsCount = questionsCountMap.TryGetValue(doc.Id, out var count) ? count : 0
        });
        
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

    private async Task ProcessDocumentAsync(int documentId)
    {
        // Create a new scope for background processing
        using var scope = _serviceScopeFactory.CreateScope();
        var documentRepository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var contentAnalyzer = scope.ServiceProvider.GetRequiredService<IContentAnalyzer>();
        var documentProcessors = scope.ServiceProvider.GetRequiredService<IEnumerable<IDocumentProcessor>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<DocumentsController>>();

        try
        {
            var document = await documentRepository.GetByIdAsync(documentId);
            if (document == null) return;

            // Update status to Extracting
            document.Status = DocumentStatus.Extracting;
            await documentRepository.UpdateAsync(documentId, document);

            // Extract text from document
            var processor = documentProcessors.FirstOrDefault(p => p.SupportedFileType(document.FileType));
            if (processor == null)
            {
                logger.LogError("No processor found for file type: {FileType}", document.FileType);
                document.Status = DocumentStatus.Failed;
                await documentRepository.UpdateAsync(documentId, document);
                return;
            }

            var extractedText = await processor.ExtractTextAsync(document.FilePath, document.FileType);
            document.ExtractedText = extractedText;

            // Update status to Analyzing
            document.Status = DocumentStatus.Analyzing;
            await documentRepository.UpdateAsync(documentId, document);

            // Analyze content
            var processedContent = await contentAnalyzer.AnalyzeContentAsync(extractedText);
            document.SetMainTopics(processedContent.MainTopics);
            document.SetKeyPoints(processedContent.KeyPoints);
            document.Summary = processedContent.Summary;
            document.Language = processedContent.Language;

            // Update status to Completed
            document.Status = DocumentStatus.Completed;
            document.UpdatedAt = DateTime.UtcNow;
            await documentRepository.UpdateAsync(documentId, document);

            logger.LogInformation("Document processed successfully: {DocumentId}", documentId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing document: {DocumentId}", documentId);
            
            var document = await documentRepository.GetByIdAsync(documentId);
            if (document != null)
            {
                document.Status = DocumentStatus.Failed;
                await documentRepository.UpdateAsync(documentId, document);
            }
        }
    }
}
