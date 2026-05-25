using System.Text.Json;

namespace ELearnGamePlatform.API.Services;

public interface IAnalyticsEventService
{
    Task<int> RecordEventsAsync(
        string userId,
        IReadOnlyList<AnalyticsEventInput> events,
        CancellationToken cancellationToken = default);
}

public sealed class AnalyticsEventInput
{
    public string Name { get; set; } = string.Empty;
    public JsonElement? Properties { get; set; }
    public DateTime? OccurredAt { get; set; }
    public string? SessionId { get; set; }
}
