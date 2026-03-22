using System.Collections.Concurrent;

namespace ELearnGamePlatform.API.Services;

public interface IDocumentProcessingJobStore
{
    void StartJob(int documentId, string fileName);
    bool TryGetJob(int documentId, out DocumentProcessingJobState? state);
    void UpdateJob(int documentId, Action<DocumentProcessingJobState> updater);
}

public class DocumentProcessingJobStore : IDocumentProcessingJobStore
{
    private readonly ConcurrentDictionary<int, DocumentProcessingJobState> _jobs = new();

    public void StartJob(int documentId, string fileName)
    {
        var now = DateTime.UtcNow;
        _jobs[documentId] = new DocumentProcessingJobState
        {
            DocumentId = documentId,
            FileName = fileName,
            Status = "queued",
            Percent = 0,
            Stage = "queued",
            StageLabel = "Cho xu ly",
            Message = "Da xep hang xu ly tai lieu",
            StageIndex = 1,
            StageCount = 6,
            CreatedAt = now,
            UpdatedAt = now,
            ElapsedSeconds = 0
        };
    }

    public bool TryGetJob(int documentId, out DocumentProcessingJobState? state)
    {
        var found = _jobs.TryGetValue(documentId, out var result);
        state = result;
        return found;
    }

    public void UpdateJob(int documentId, Action<DocumentProcessingJobState> updater)
    {
        if (!_jobs.TryGetValue(documentId, out var state))
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

public class DocumentProcessingJobState
{
    public int DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
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
    public int? ElapsedSeconds { get; set; }
    public int? EstimatedRemainingSeconds { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? EtaAnchorStage { get; set; }
    public int? EtaAnchorCurrent { get; set; }
    public int? EtaAnchorTotal { get; set; }
    public DateTime? EtaAnchorAt { get; set; }
}
