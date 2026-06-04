using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DashboardController : AuthenticatedControllerBase
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IWorkspacePayloadService _workspacePayloadService;
    private readonly IWorkspaceService _workspaceService;

    public DashboardController(
        IDocumentRepository documentRepository,
        IWorkspacePayloadService workspacePayloadService,
        IWorkspaceService workspaceService)
    {
        _documentRepository = documentRepository;
        _workspacePayloadService = workspacePayloadService;
        _workspaceService = workspaceService;
    }

    [HttpGet("home")]
    public async Task<IActionResult> GetHome()
    {
        if (CurrentUserId == null || string.IsNullOrWhiteSpace(CurrentUserIdAsString))
        {
            return Unauthorized();
        }

        var workspace = await _workspaceService.EnsureDefaultWorkspaceAsync(CurrentUserIdAsString);
        await _workspaceService.AttachOrphanDocumentsAsync(CurrentUserIdAsString, workspace.Id);

        var sources = await _documentRepository.GetByFolderProjectIdAsync(workspace.Id);
        var payload = await _workspacePayloadService.BuildDashboardHomePayloadAsync(workspace, sources);

        return Ok(payload);
    }
}
