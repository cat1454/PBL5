using System.Text.Json;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Infrastructure.Data;

namespace ELearnGamePlatform.API.Services;

public sealed class AnalyticsEventService : IAnalyticsEventService
{
    public const int MaxBatchSize = 50;
    private const int MaxNameLength = 120;
    private const int MaxSessionIdLength = 120;
    private const int MaxPropertiesJsonLength = 8_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _dbContext;

    public AnalyticsEventService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> RecordEventsAsync(
        string userId,
        IReadOnlyList<AnalyticsEventInput> events,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }

        if (events.Count > MaxBatchSize)
        {
            throw new InvalidOperationException($"At most {MaxBatchSize} analytics events can be recorded in one request.");
        }

        var now = DateTime.UtcNow;
        foreach (var input in events)
        {
            var name = NormalizeName(input.Name);
            var sessionId = NormalizeOptional(input.SessionId, MaxSessionIdLength);
            var propertiesJson = NormalizeProperties(input.Properties);
            var occurredAt = NormalizeOccurredAt(input.OccurredAt, now);

            _dbContext.AnalyticsEvents.Add(new AnalyticsEvent
            {
                UserId = userId,
                Name = name,
                PropertiesJson = propertiesJson,
                SessionId = sessionId,
                OccurredAt = occurredAt,
                ReceivedAt = now
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return events.Count;
    }

    private static string NormalizeName(string? name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Event name is required.");
        }

        if (normalized.Length > MaxNameLength)
        {
            throw new InvalidOperationException($"Event name must be {MaxNameLength} characters or fewer.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > maxLength)
        {
            throw new InvalidOperationException($"Value must be {maxLength} characters or fewer.");
        }

        return normalized;
    }

    private static string NormalizeProperties(JsonElement? properties)
    {
        if (!properties.HasValue || properties.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return "{}";
        }

        if (properties.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Event properties must be an object.");
        }

        var json = JsonSerializer.Serialize(properties.Value, JsonOptions);
        if (json.Length > MaxPropertiesJsonLength)
        {
            throw new InvalidOperationException($"Event properties must serialize to {MaxPropertiesJsonLength} characters or fewer.");
        }

        return json;
    }

    private static DateTime NormalizeOccurredAt(DateTime? occurredAt, DateTime nowUtc)
    {
        if (!occurredAt.HasValue)
        {
            return nowUtc;
        }

        return occurredAt.Value.Kind switch
        {
            DateTimeKind.Utc => occurredAt.Value,
            DateTimeKind.Local => occurredAt.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(occurredAt.Value, DateTimeKind.Utc)
        };
    }
}
