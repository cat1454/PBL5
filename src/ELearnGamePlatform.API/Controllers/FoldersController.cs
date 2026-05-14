using ELearnGamePlatform.API.Contracts;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FoldersController : AuthenticatedControllerBase
{
    private readonly IFolderProjectRepository _folderProjectRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentProcessingJobStore _documentJobStore;
    private readonly IDocumentIngestionService _documentIngestionService;
    private readonly IWorkspaceService _workspaceService;
    private readonly ILogger<FoldersController> _logger;

    public FoldersController(
        IFolderProjectRepository folderProjectRepository,
        IDocumentRepository documentRepository,
        IDocumentProcessingJobStore documentJobStore,
        IDocumentIngestionService documentIngestionService,
        IWorkspaceService workspaceService,
        ILogger<FoldersController> logger)
    {
        _folderProjectRepository = folderProjectRepository;
        _documentRepository = documentRepository;
        _documentJobStore = documentJobStore;
        _documentIngestionService = documentIngestionService;
        _workspaceService = workspaceService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required");
        }

        if (CurrentUserId == null)
        {
            return Unauthorized("User context is required");
        }

        var folder = new FolderProject
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            UploadedBy = CurrentUserIdAsString
        };

        var created = await _folderProjectRepository.CreateAsync(folder);
        return Ok(BuildFolderPayload(created, Array.Empty<Document>(), null));
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserFolders(string userId)
    {
        var authResult = EnsureCurrentUserMatches(userId);
        if (authResult != null)
        {
            return authResult;
        }

        var currentUserId = CurrentUserIdAsString;
        var defaultWorkspace = await _workspaceService.EnsureDefaultWorkspaceAsync(currentUserId);
        await _workspaceService.AttachOrphanDocumentsAsync(currentUserId, defaultWorkspace.Id);

        var folders = await _folderProjectRepository.GetByUserAsync(currentUserId);
        var payload = folders.Select(folder =>
        {
            var sources = folder.Documents.OrderBy(source => source.FolderSourceOrder).ThenBy(source => source.CreatedAt).ToList();
            var deck = folder.SlideDecks.OrderByDescending(item => item.CreatedAt).FirstOrDefault();
            return BuildFolderPayload(folder, sources, deck);
        });

        return Ok(payload);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetFolder(int id)
    {
        var folder = await _folderProjectRepository.GetByIdAsync(id);
        if (folder == null)
        {
            return NotFound();
        }

        var authResult = EnsureOwnerAccess(folder.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var sources = folder.Documents.OrderBy(source => source.FolderSourceOrder).ThenBy(source => source.CreatedAt).ToList();
        var deck = folder.SlideDecks.OrderByDescending(item => item.CreatedAt).FirstOrDefault();
        return Ok(BuildFolderPayload(folder, sources, deck));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteFolder(int id)
    {
        var folder = await _folderProjectRepository.GetByIdAsync(id);
        if (folder == null)
        {
            return NotFound();
        }

        var authResult = EnsureOwnerAccess(folder.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        foreach (var source in folder.Documents)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(source.FilePath) && System.IO.File.Exists(source.FilePath))
                {
                    System.IO.File.Delete(source.FilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete source file {FilePath} for folder {FolderId}", source.FilePath, id);
            }
        }

        await _folderProjectRepository.DeleteAsync(id);
        return NoContent();
    }

    [HttpPost("{id}/sources/upload")]
    public async Task<IActionResult> UploadSource(int id, [FromForm] IFormFile file)
    {
        var folder = await _folderProjectRepository.GetByIdAsync(id);
        if (folder == null)
        {
            return NotFound("Folder project not found");
        }

        var authResult = EnsureOwnerAccess(folder.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        try
        {
            var createdDocument = await _documentIngestionService.UploadDocumentAsync(file, CurrentUserIdAsString, id);
            await _folderProjectRepository.TouchAsync(id);
            _documentJobStore.TryGetJob(createdDocument.Id, out var progressState);
            _documentIngestionService.StartBackgroundProcessing(createdDocument.Id);

            return Ok(new
            {
                source = BuildSourcePayload(createdDocument, 0),
                message = "Source uploaded successfully. Processing started.",
                progressUrl = $"/api/documents/{createdDocument.Id}/progress",
                progress = JobProgressPayloadFactory.BuildDocument(progressState, createdDocument)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading source into folder {FolderId}", id);
            return ex is InvalidOperationException
                ? BadRequest(ex.Message)
                : StatusCode(500, "Error uploading folder source");
        }
    }

    [HttpGet("{id}/sources")]
    public async Task<IActionResult> GetFolderSources(int id)
    {
        var folder = await _folderProjectRepository.GetByIdAsync(id);
        if (folder == null)
        {
            return NotFound();
        }

        var authResult = EnsureOwnerAccess(folder.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var sources = await _documentRepository.GetByFolderProjectIdAsync(id);
        var payload = sources.Select(source => BuildSourcePayload(source, source.Questions.Count));
        return Ok(payload);
    }

    [HttpPut("{id}/sources/{sourceId}/slide-selection")]
    public async Task<IActionResult> UpdateSlideSelection(int id, int sourceId, [FromBody] UpdateFolderSourceSelectionRequest request)
    {
        var folder = await _folderProjectRepository.GetByIdAsync(id);
        if (folder == null)
        {
            return NotFound("Folder project not found");
        }

        var authResult = EnsureOwnerAccess(folder.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var source = await _documentRepository.GetByIdAsync(sourceId);
        if (source == null || source.FolderProjectId != id)
        {
            return NotFound("Folder source not found");
        }

        authResult = EnsureOwnerAccess(source.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        source.IncludeInFolderSlides = request.IncludeInFolderSlides;
        source.UpdatedAt = DateTime.UtcNow;
        await _documentRepository.UpdateAsync(source.Id, source);
        await _folderProjectRepository.TouchAsync(id);

        return Ok(BuildSourcePayload(source, source.Questions.Count));
    }

    private object BuildFolderPayload(FolderProject folder, IReadOnlyCollection<Document> sources, SlideDeck? deck)
    {
        var readySourceCount = sources.Count(source => source.Status == DocumentStatus.Completed);
        var selectedSourceCount = sources.Count(source => source.IncludeInFolderSlides);
        var latestDeckUpdatedAt = deck?.UpdatedAt ?? deck?.CompletedAt;
        var isStale = deck != null && sources.Any(source =>
            source.CreatedAt > latestDeckUpdatedAt || source.UpdatedAt > latestDeckUpdatedAt);

        return new
        {
            id = folder.Id,
            name = folder.Name,
            description = folder.Description,
            uploadedBy = folder.UploadedBy,
            createdAt = folder.CreatedAt,
            updatedAt = folder.UpdatedAt,
            sourceCount = sources.Count,
            readySourceCount,
            selectedSourceCount,
            latestDeck = deck == null
                ? null
                : new
                {
                    id = deck.Id,
                    folderProjectId = deck.FolderProjectId,
                    status = deck.Status.ToString(),
                    title = deck.Title,
                    subtitle = deck.Subtitle,
                    slideCount = deck.Items.Count,
                    updatedAt = deck.UpdatedAt,
                    completedAt = deck.CompletedAt,
                    isStale
                }
        };
    }

    private object BuildSourcePayload(Document source, int questionsCount)
    {
        _documentJobStore.TryGetJob(source.Id, out var progressState);

        return new
        {
            id = source.Id,
            folderProjectId = source.FolderProjectId,
            fileName = source.FileName,
            fileType = source.FileType,
            filePath = source.FilePath,
            fileSize = source.FileSize,
            extractedText = source.ExtractedText,
            rawOcrText = source.RawOcrText,
            cleanedText = source.CleanedText,
            isTextReviewed = source.IsTextReviewed,
            mainTopics = source.GetMainTopics(),
            keyPoints = source.GetKeyPoints(),
            coverageChunkCount = source.GetCoverageMap().Count,
            summary = source.Summary,
            language = source.Language,
            status = source.Status,
            uploadedBy = source.UploadedBy,
            createdAt = source.CreatedAt,
            updatedAt = source.UpdatedAt,
            includeInFolderSlides = source.IncludeInFolderSlides,
            folderSourceOrder = source.FolderSourceOrder,
            questionsCount,
            processingProgress = JobProgressPayloadFactory.BuildDocument(progressState, source)
        };
    }
}

public class CreateFolderProjectRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public class UpdateFolderSourceSelectionRequest
{
    public bool IncludeInFolderSlides { get; set; }
}
