using System.Collections.Concurrent;

namespace ELearnGamePlatform.API.Services;

public interface ISlideGenerationJobStore
{
    string CreateJob(int documentId, int desiredSlideCount, string createdByUserId);
    string CreateFolderJob(int folderProjectId, int desiredSlideCount, string createdByUserId);
    bool TryGetJob(string jobId, out SlideGenerationJobState? state);
    bool TryGetLatestJobForDocument(int documentId, out SlideGenerationJobState? state);
    bool TryGetLatestJobForFolder(int folderProjectId, out SlideGenerationJobState? state);
    void UpdateJob(string jobId, Action<SlideGenerationJobState> updater);
}

public class SlideGenerationJobStore : ISlideGenerationJobStore
{
    private readonly ConcurrentDictionary<string, SlideGenerationJobState> _jobs = new();
    private readonly ConcurrentDictionary<int, string> _latestJobByDocument = new();
    private readonly ConcurrentDictionary<int, string> _latestJobByFolder = new();

    public string CreateJob(int documentId, int desiredSlideCount, string createdByUserId)
    {
        var now = DateTime.UtcNow;
        var jobId = Guid.NewGuid().ToString("N");
        var state = new SlideGenerationJobState
        {
            JobId = jobId,
            DocumentId = documentId,
            DesiredSlideCount = desiredSlideCount,
            CreatedByUserId = createdByUserId,
            Status = "queued",
            Percent = 0,
            Stage = "queued",
            StageLabel = "Cho xu ly",
            Message = "Da tao job sinh slide",
            CreatedAt = now,
            UpdatedAt = now,
            ElapsedSeconds = 0
        };

        _jobs[jobId] = state;
        _latestJobByDocument[documentId] = jobId;
        return jobId;
    }

    public string CreateFolderJob(int folderProjectId, int desiredSlideCount, string createdByUserId)
    {
        var now = DateTime.UtcNow;
        var jobId = Guid.NewGuid().ToString("N");
        var state = new SlideGenerationJobState
        {
            JobId = jobId,
            FolderProjectId = folderProjectId,
            DesiredSlideCount = desiredSlideCount,
            CreatedByUserId = createdByUserId,
            Status = "queued",
            Percent = 0,
            Stage = "queued",
            StageLabel = "Cho xu ly",
            Message = "Da tao job sinh slide",
            CreatedAt = now,
            UpdatedAt = now,
            ElapsedSeconds = 0
        };

        _jobs[jobId] = state;
        _latestJobByFolder[folderProjectId] = jobId;
        return jobId;
    }

    public bool TryGetJob(string jobId, out SlideGenerationJobState? state)
    {
        var found = _jobs.TryGetValue(jobId, out var result);
        state = result;
        return found;
    }

    public bool TryGetLatestJobForDocument(int documentId, out SlideGenerationJobState? state)
    {
        state = null;
        if (!_latestJobByDocument.TryGetValue(documentId, out var jobId))
        {
            return false;
        }

        return TryGetJob(jobId, out state);
    }

    public bool TryGetLatestJobForFolder(int folderProjectId, out SlideGenerationJobState? state)
    {
        state = null;
        if (!_latestJobByFolder.TryGetValue(folderProjectId, out var jobId))
        {
            return false;
        }

        return TryGetJob(jobId, out state);
    }

    public void UpdateJob(string jobId, Action<SlideGenerationJobState> updater)
    {
        if (!_jobs.TryGetValue(jobId, out var state))
        {
            return;
        }

        lock (state)
        {
            updater(state);
            state.UpdatedAt = DateTime.UtcNow;
        }
    }
}

public class SlideGenerationJobState
{
    public string JobId { get; set; } = string.Empty;
    public int? DocumentId { get; set; }
    public int? FolderProjectId { get; set; }
    public int DesiredSlideCount { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public int? SlideDeckId { get; set; }
    public string Status { get; set; } = "queued";
    public int Percent { get; set; }
    public string Stage { get; set; } = "queued";
    public string? StageLabel { get; set; }
    public string? Message { get; set; }
    public string? Detail { get; set; }
    public int? Current { get; set; }
    public int? Total { get; set; }
    public string? UnitLabel { get; set; }
    public int? StageIndex { get; set; }
    public int? StageCount { get; set; }
    public int? SlidesGenerated { get; set; }
    public int? ElapsedSeconds { get; set; }
    public int? EstimatedRemainingSeconds { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
