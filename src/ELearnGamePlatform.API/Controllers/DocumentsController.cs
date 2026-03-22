using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ELearnGamePlatform.API.Configuration;
using ELearnGamePlatform.API.Services;

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
    private readonly IDocumentProcessingJobStore _documentJobStore;

    public DocumentsController(
        IDocumentRepository documentRepository,
        IQuestionRepository questionRepository,
        IContentAnalyzer contentAnalyzer,
        ILogger<DocumentsController> logger,
        IEnumerable<IDocumentProcessor> documentProcessors,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<FileUploadSettings> fileUploadOptions,
        IDocumentProcessingJobStore documentJobStore)
    {
        _documentRepository = documentRepository;
        _questionRepository = questionRepository;
        _contentAnalyzer = contentAnalyzer;
        _logger = logger;
        _documentProcessors = documentProcessors;
        _serviceScopeFactory = serviceScopeFactory;
        _fileUploadSettings = fileUploadOptions.Value;
        _documentJobStore = documentJobStore;
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
            _documentJobStore.StartJob(createdDocument.Id, createdDocument.FileName);

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

        return Ok(BuildDocumentPayload(document, questionsCount: document.Questions.Count));
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

    private async Task ProcessDocumentAsync(int documentId)
    {
        // Create a new scope for background processing
        using var scope = _serviceScopeFactory.CreateScope();
        var documentRepository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var contentAnalyzer = scope.ServiceProvider.GetRequiredService<IContentAnalyzer>();
        var documentProcessors = scope.ServiceProvider.GetRequiredService<IEnumerable<IDocumentProcessor>>();
        var documentJobStore = scope.ServiceProvider.GetRequiredService<IDocumentProcessingJobStore>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<DocumentsController>>();

        try
        {
            var document = await documentRepository.GetByIdAsync(documentId);
            if (document == null) return;

            documentJobStore.UpdateJob(documentId, state =>
            {
                state.Status = "running";
                state.Percent = 3;
                state.Stage = "preparing";
                state.StageLabel = "Chuan bi xu ly";
                state.Message = "Dang xac dinh cach xu ly tai lieu";
                state.Detail = $"Dinh dang file: {document.FileType}";
                state.StageIndex = 1;
                state.StageCount = 6;
                state.Error = null;
                UpdateEta(state);
            });

            document.Status = DocumentStatus.Extracting;
            await documentRepository.UpdateAsync(documentId, document);

            var processor = documentProcessors.FirstOrDefault(p => p.SupportedFileType(document.FileType));
            if (processor == null)
            {
                logger.LogError("No processor found for file type: {FileType}", document.FileType);
                document.Status = DocumentStatus.Failed;
                await documentRepository.UpdateAsync(documentId, document);
                documentJobStore.UpdateJob(documentId, state =>
                {
                    state.Status = "failed";
                    state.Percent = 100;
                    state.Stage = "failed";
                    state.StageLabel = "That bai";
                    state.Message = "Khong tim thay processor phu hop";
                    state.Detail = $"Khong ho tro file type {document.FileType}";
                    state.Error = "No processor found for file type";
                    state.StageIndex = 6;
                    state.StageCount = 6;
                    state.EstimatedRemainingSeconds = 0;
                    UpdateEta(state);
                });
                return;
            }

            var extractionProgress = new Progress<DocumentProcessingProgressUpdate>(update =>
            {
                documentJobStore.UpdateJob(documentId, state =>
                {
                    ApplyProgressUpdate(state, MapProgressUpdate(update, 8, 58, "extracting", "Trich xuat van ban", 2, 6));
                });
            });

            var extractedText = await processor.ExtractTextAsync(document.FilePath, document.FileType, extractionProgress);
            document.ExtractedText = extractedText;

            documentJobStore.UpdateJob(documentId, state =>
            {
                state.Status = "running";
                state.Percent = Math.Max(state.Percent, 60);
                state.Stage = "extracting";
                state.StageLabel = "Trich xuat van ban";
                state.Message = "Da xong buoc trich xuat van ban";
                state.Detail = $"Da lay duoc {extractedText.Length} ky tu, chuan bi phan tich noi dung";
                state.StageIndex = 2;
                state.StageCount = 6;
                UpdateEta(state);
            });

            document.Status = DocumentStatus.Analyzing;
            await documentRepository.UpdateAsync(documentId, document);

            var analysisProgress = new Progress<DocumentProcessingProgressUpdate>(update =>
            {
                documentJobStore.UpdateJob(documentId, state =>
                {
                    ApplyProgressUpdate(state, MapProgressUpdate(update, 62, 96, "analyzing", "Phan tich noi dung", update.Stage == "analyzing-chunks" ? 4 : 5, 6));
                });
            });

            var processedContent = await contentAnalyzer.AnalyzeContentAsync(extractedText, analysisProgress);
            document.SetMainTopics(processedContent.MainTopics);
            document.SetKeyPoints(processedContent.KeyPoints);
            document.SetCoverageMap(processedContent.CoverageMap);
            document.Summary = processedContent.Summary;
            document.Language = processedContent.Language;

            documentJobStore.UpdateJob(documentId, state =>
            {
                state.Status = "running";
                state.Percent = 98;
                state.Stage = "saving";
                state.StageLabel = "Luu ket qua";
                state.Message = "Dang luu ket qua phan tich vao he thong";
                state.Detail = $"Co {processedContent.MainTopics.Count} topic, {processedContent.KeyPoints.Count} key point, {processedContent.CoverageMap.Count} coverage chunk";
                state.StageIndex = 6;
                state.StageCount = 6;
                state.EstimatedRemainingSeconds = 1;
                UpdateEta(state);
            });

            document.Status = DocumentStatus.Completed;
            document.UpdatedAt = DateTime.UtcNow;
            await documentRepository.UpdateAsync(documentId, document);

            documentJobStore.UpdateJob(documentId, state =>
            {
                state.Status = "completed";
                state.Percent = 100;
                state.Stage = "completed";
                state.StageLabel = "Hoan tat";
                state.Message = "Da xu ly xong tai lieu";
                state.Detail = "San sang tao cau hoi va hoc bang game";
                state.StageIndex = 6;
                state.StageCount = 6;
                state.EstimatedRemainingSeconds = 0;
                UpdateEta(state);
            });

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

            documentJobStore.UpdateJob(documentId, state =>
            {
                state.Status = "failed";
                state.Percent = 100;
                state.Stage = "failed";
                state.StageLabel = "That bai";
                state.Message = "Xu ly tai lieu that bai";
                state.Detail = ex.Message;
                state.Error = ex.Message;
                state.StageIndex = 6;
                state.StageCount = 6;
                state.EstimatedRemainingSeconds = 0;
                UpdateEta(state);
            });
        }
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
            processingProgress = progressState == null ? null : new
            {
                status = progressState.Status,
                percent = progressState.Percent,
                stage = progressState.Stage,
                stageLabel = progressState.StageLabel,
                message = progressState.Message,
                detail = progressState.Detail,
                current = progressState.Current,
                total = progressState.Total,
                unitLabel = progressState.UnitLabel,
                stageIndex = progressState.StageIndex,
                stageCount = progressState.StageCount,
                elapsedSeconds = progressState.ElapsedSeconds,
                estimatedRemainingSeconds = progressState.EstimatedRemainingSeconds,
                error = progressState.Error
            }
        };
    }

    private static DocumentProcessingProgressUpdate MapProgressUpdate(
        DocumentProcessingProgressUpdate update,
        int startPercent,
        int endPercent,
        string defaultStage,
        string defaultStageLabel,
        int defaultStageIndex,
        int stageCount)
    {
        return new DocumentProcessingProgressUpdate
        {
            Percent = startPercent + (int)Math.Round((endPercent - startPercent) * (Math.Clamp(update.Percent, 0, 100) / 100d)),
            Stage = string.IsNullOrWhiteSpace(update.Stage) ? defaultStage : update.Stage,
            StageLabel = string.IsNullOrWhiteSpace(update.StageLabel) ? defaultStageLabel : update.StageLabel,
            Message = update.Message,
            Detail = update.Detail,
            Current = update.Current,
            Total = update.Total,
            UnitLabel = update.UnitLabel,
            StageIndex = update.StageIndex ?? defaultStageIndex,
            StageCount = update.StageCount ?? stageCount
        };
    }

    private static void ApplyProgressUpdate(DocumentProcessingJobState state, DocumentProcessingProgressUpdate update)
    {
        state.Status = update.Stage == "failed" ? "failed" : "running";
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
        UpdateEta(state);
    }

    private static void UpdateEta(DocumentProcessingJobState state)
    {
        var now = DateTime.UtcNow;
        var elapsedSeconds = Math.Max(0, (int)Math.Round((DateTime.UtcNow - state.CreatedAt).TotalSeconds));
        state.ElapsedSeconds = elapsedSeconds;

        if (state.Status is "completed" or "failed")
        {
            state.EstimatedRemainingSeconds = 0;
            return;
        }

        if (state.Current.HasValue && state.Total.HasValue && state.Total.Value > 0)
        {
            var current = Math.Clamp(state.Current.Value, 0, state.Total.Value);
            var anchorNeedsReset =
                state.EtaAnchorAt == null ||
                !string.Equals(state.EtaAnchorStage, state.Stage, StringComparison.OrdinalIgnoreCase) ||
                state.EtaAnchorTotal != state.Total ||
                current < (state.EtaAnchorCurrent ?? 0);

            if (anchorNeedsReset)
            {
                state.EtaAnchorStage = state.Stage;
                state.EtaAnchorTotal = state.Total;
                state.EtaAnchorCurrent = Math.Max(0, current - 1);
                state.EtaAnchorAt = now;
            }

            if (current >= state.Total.Value)
            {
                state.EstimatedRemainingSeconds = 0;
                return;
            }

            var stageElapsedSeconds = Math.Max(0.1d, (now - state.EtaAnchorAt!.Value).TotalSeconds);
            var processedUnits = Math.Max(0, current - (state.EtaAnchorCurrent ?? 0));
            var remainingUnits = Math.Max(0, state.Total.Value - current);

            if (processedUnits > 0 && remainingUnits > 0)
            {
                var secondsPerUnit = stageElapsedSeconds / processedUnits;
                state.EstimatedRemainingSeconds = Math.Max(1, (int)Math.Round(secondsPerUnit * remainingUnits));
                return;
            }
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
}
