using ELearnGamePlatform.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class AnalyticsController : AuthenticatedControllerBase
{
    private readonly IAnalyticsEventService _analyticsEventService;
    private readonly IPersonalAnalyticsService _personalAnalyticsService;

    public AnalyticsController(
        IAnalyticsEventService analyticsEventService,
        IPersonalAnalyticsService personalAnalyticsService)
    {
        _analyticsEventService = analyticsEventService;
        _personalAnalyticsService = personalAnalyticsService;
    }

    [HttpGet("personal")]
    public async Task<IActionResult> GetPersonalSummary(CancellationToken cancellationToken)
    {
        if (CurrentUserId == null || string.IsNullOrWhiteSpace(CurrentUserIdAsString))
        {
            return Unauthorized();
        }

        var summary = await _personalAnalyticsService.GetPersonalSummaryAsync(
            CurrentUserIdAsString,
            cancellationToken);

        return Ok(summary);
    }

    [HttpPost("events")]
    public async Task<IActionResult> RecordEvents(
        [FromBody] AnalyticsEventsRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId == null || string.IsNullOrWhiteSpace(CurrentUserIdAsString))
        {
            return Unauthorized();
        }

        if (request.Events.Count == 0)
        {
            return ApiBadRequest("analytics_events_required", "At least one analytics event is required.");
        }

        if (request.Events.Count > AnalyticsEventService.MaxBatchSize)
        {
            return ApiBadRequest("analytics_batch_too_large", $"At most {AnalyticsEventService.MaxBatchSize} analytics events can be recorded in one request.");
        }

        try
        {
            var recordedCount = await _analyticsEventService.RecordEventsAsync(
                CurrentUserIdAsString,
                request.Events,
                cancellationToken);

            return Ok(new { recordedCount });
        }
        catch (InvalidOperationException ex)
        {
            return ApiBadRequest("analytics_event_invalid", ex.Message);
        }
    }
}

public sealed class AnalyticsEventsRequest
{
    public List<AnalyticsEventInput> Events { get; set; } = new();
}
