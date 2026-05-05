using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : AuthenticatedControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview()
    {
        var users = await _dbContext.AppUsers
            .OrderByDescending(user => user.CreatedAt)
            .Select(user => new
            {
                id = user.Id,
                fullName = user.FullName,
                email = user.Email,
                role = user.Role.ToString().ToUpperInvariant(),
                isActive = user.IsActive,
                createdAt = user.CreatedAt,
                updatedAt = user.UpdatedAt
            })
            .ToListAsync();

        var documents = await _dbContext.Documents
            .OrderByDescending(document => document.CreatedAt)
            .Select(document => new
            {
                id = document.Id,
                fileName = document.FileName,
                status = document.Status.ToString(),
                uploadedBy = document.UploadedBy,
                createdAt = document.CreatedAt,
                updatedAt = document.UpdatedAt,
                folderProjectId = document.FolderProjectId
            })
            .Take(100)
            .ToListAsync();

        return Ok(new
        {
            totals = new
            {
                users = users.Count,
                documents = documents.Count
            },
            users,
            documents
        });
    }
}
