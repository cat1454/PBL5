using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Threading.Tasks;
using ELearnGamePlatform.API.Services;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/dev")]
[AllowAnonymous]
public class DevController : ControllerBase
{
    private readonly DemoPayloadImporter _importer;
    private readonly IWebHostEnvironment _env;

    public DevController(DemoPayloadImporter importer, IWebHostEnvironment env)
    {
        _importer = importer;
        _env = env;
    }

    [HttpPost("import-demo-learning-payload")]
    public async Task<IActionResult> ImportDemoLearningPayload([FromBody] ImportDemoPayloadRequest request)
    {
        if (!_env.IsDevelopment())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This endpoint is only available in Development environment." });
        }

        if (request == null || string.IsNullOrWhiteSpace(request.FilePath))
        {
            return BadRequest(new { message = "filePath is required." });
        }

        try
        {
            var result = await _importer.ImportAsync(request.FilePath, request.UserId, request.Replace);
            return Ok(result);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
        }
    }
}

public class ImportDemoPayloadRequest
{
    public string? UserId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public bool Replace { get; set; }
}
