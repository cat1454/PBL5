using System.Collections.Concurrent;

namespace ELearnGamePlatform.API.Services;

public interface IQuestionGenerationJobStore
{
    string CreateJob(int documentId, int count, string? questionType, string createdByUserId);
    bool TryGetJob(string jobId, out QuestionGenerationJobState? state);
    void UpdateJob(string jobId, Action<QuestionGenerationJobState> updater);
}

public class QuestionGenerationJobStore : IQuestionGenerationJobStore
{
    private readonly ConcurrentDictionary<string, QuestionGenerationJobState> _jobs = new();

    public string CreateJob(int documentId, int count, string? questionType, string createdByUserId)
    {
        var now = DateTime.UtcNow;
        var jobId = Guid.NewGuid().ToString("N");
        var state = new QuestionGenerationJobState
        {
            JobId = jobId,
            DocumentId = documentId,
            Count = count,
            QuestionType = questionType,
            CreatedByUserId = createdByUserId,
            Status = "queued",
            Percent = 0,
            Stage = "queued",
            StageLabel = "Cho xu ly",
            Message = "Da tao job",
            CreatedAt = now,
            UpdatedAt = now,
            ElapsedSeconds = 0
        };

        _jobs[jobId] = state;
        return jobId;
    }

    public bool TryGetJob(string jobId, out QuestionGenerationJobState? state)
    {
        var found = _jobs.TryGetValue(jobId, out var result);
        state = result;
        return found;
    }

    public void UpdateJob(string jobId, Action<QuestionGenerationJobState> updater)
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

public class QuestionGenerationJobState
{
    public string JobId { get; set; } = string.Empty;
    public int DocumentId { get; set; }
    public int Count { get; set; }
    public string? QuestionType { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
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
    public string? TopicTag { get; set; }
    public int? QuestionsGenerated { get; set; }
    public int? ElapsedSeconds { get; set; }
    public int? EstimatedRemainingSeconds { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
