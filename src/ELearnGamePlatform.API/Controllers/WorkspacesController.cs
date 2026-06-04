using ELearnGamePlatform.API.Contracts;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkspacesController : AuthenticatedControllerBase
{
    private readonly IFolderProjectRepository _folderProjectRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentProcessingJobStore _documentJobStore;
    private readonly IDocumentIngestionService _documentIngestionService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IWorkspacePayloadService _workspacePayloadService;
    private readonly ILogger<WorkspacesController> _logger;

    public WorkspacesController(
        IFolderProjectRepository folderProjectRepository,
        IDocumentRepository documentRepository,
        IDocumentProcessingJobStore documentJobStore,
        IDocumentIngestionService documentIngestionService,
        IWorkspaceService workspaceService,
        IWorkspacePayloadService workspacePayloadService,
        ILogger<WorkspacesController> logger)
    {
        _folderProjectRepository = folderProjectRepository;
        _documentRepository = documentRepository;
        _documentJobStore = documentJobStore;
        _documentIngestionService = documentIngestionService;
        _workspaceService = workspaceService;
        _workspacePayloadService = workspacePayloadService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateWorkspace([FromBody] CreateWorkspaceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required");
        }

        if (CurrentUserId == null)
        {
            return Unauthorized("User context is required");
        }

        var workspace = await _folderProjectRepository.CreateAsync(new FolderProject
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            UploadedBy = CurrentUserIdAsString,
        });

        return Ok(_workspacePayloadService.BuildWorkspacePayload(workspace));
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserWorkspaces(string userId)
    {
        var authResult = EnsureCurrentUserMatches(userId);
        if (authResult != null)
        {
            return authResult;
        }

        var currentUserId = CurrentUserIdAsString;
        var defaultWorkspace = await _workspaceService.EnsureDefaultWorkspaceAsync(currentUserId);
        await _workspaceService.AttachOrphanDocumentsAsync(currentUserId, defaultWorkspace.Id);

        var workspaces = await _folderProjectRepository.GetByUserAsync(currentUserId);
        var payload = workspaces.Select(_workspacePayloadService.BuildWorkspacePayload).ToList();
        return Ok(payload);
    }

    [HttpGet("default/user/{userId}")]
    public async Task<IActionResult> GetDefaultWorkspace(string userId)
    {
        var authResult = EnsureCurrentUserMatches(userId);
        if (authResult != null)
        {
            return authResult;
        }

        var currentUserId = CurrentUserIdAsString;
        var workspace = await _workspaceService.EnsureDefaultWorkspaceAsync(currentUserId);
        await _workspaceService.AttachOrphanDocumentsAsync(currentUserId, workspace.Id);
        return Ok(_workspacePayloadService.BuildWorkspacePayload(workspace));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetWorkspace(int id)
    {
        var workspace = await _folderProjectRepository.GetByIdAsync(id);
        if (workspace == null)
        {
            return NotFound();
        }

        var authResult = EnsureOwnerAccess(workspace.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        return Ok(_workspacePayloadService.BuildWorkspacePayload(workspace));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteWorkspace(int id)
    {
        var workspace = await _folderProjectRepository.GetByIdAsync(id);
        if (workspace == null)
        {
            return NotFound();
        }

        var authResult = EnsureOwnerAccess(workspace.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        foreach (var source in workspace.Documents)
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
                _logger.LogWarning(ex, "Could not delete source file {FilePath} for workspace {WorkspaceId}", source.FilePath, id);
            }
        }

        await _folderProjectRepository.DeleteAsync(id);
        return NoContent();
    }

    [HttpPost("{id}/sources/upload")]
    public async Task<IActionResult> UploadSource(int id, [FromForm] IFormFile file)
    {
        var workspace = await _folderProjectRepository.GetByIdAsync(id);
        if (workspace == null)
        {
            return NotFound("Workspace not found");
        }

        var authResult = EnsureOwnerAccess(workspace.UploadedBy);
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
                source = await _workspacePayloadService.BuildSourcePayloadAsync(createdDocument, 0),
                workspaceId = id,
                message = "Source uploaded successfully. Processing started.",
                progressUrl = $"/api/documents/{createdDocument.Id}/progress",
                progress = JobProgressPayloadFactory.BuildDocument(progressState, createdDocument),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading source into workspace {WorkspaceId}", id);
            return ex is InvalidOperationException
                ? BadRequest(ex.Message)
                : StatusCode(500, "Error uploading workspace source");
        }
    }

    [HttpGet("{id}/sources")]
    public async Task<IActionResult> GetWorkspaceSources(int id)
    {
        var workspace = await _folderProjectRepository.GetByIdAsync(id);
        if (workspace == null)
        {
            return NotFound();
        }

        var authResult = EnsureOwnerAccess(workspace.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var sources = await _documentRepository.GetByFolderProjectIdAsync(id);
        return Ok(await _workspacePayloadService.BuildSourcePayloadsAsync(sources));
    }

    [HttpPut("{id}/sources/{sourceId}/slide-selection")]
    public async Task<IActionResult> UpdateSourceSelection(int id, int sourceId, [FromBody] UpdateWorkspaceSourceSelectionRequest request)
    {
        var source = await _documentRepository.GetByIdAsync(sourceId);
        if (source == null || source.FolderProjectId != id)
        {
            return NotFound("Workspace source not found");
        }

        var authResult = EnsureOwnerAccess(source.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        source.IncludeInFolderSlides = request.IncludeInWorkspaceSlides;
        source.UpdatedAt = DateTime.UtcNow;
        await _documentRepository.UpdateAsync(source.Id, source);
        await _folderProjectRepository.TouchAsync(id);

        return Ok(await _workspacePayloadService.BuildSourcePayloadAsync(source, source.Questions.Count));
    }
}

public class CreateWorkspaceRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public class UpdateWorkspaceSourceSelectionRequest
{
    public bool IncludeInWorkspaceSlides { get; set; }
}
