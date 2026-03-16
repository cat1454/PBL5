using System.Collections.Concurrent;

namespace ELearnGamePlatform.API.Services;

public interface IQuestionGenerationJobStore
{
    string CreateJob(int documentId, int count, string? questionType);
    bool TryGetJob(string jobId, out QuestionGenerationJobState? state);
    void UpdateJob(string jobId, Action<QuestionGenerationJobState> updater);
}

public class QuestionGenerationJobStore : IQuestionGenerationJobStore
{
    private readonly ConcurrentDictionary<string, QuestionGenerationJobState> _jobs = new();

    public string CreateJob(int documentId, int count, string? questionType)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var state = new QuestionGenerationJobState
        {
            JobId = jobId,
            DocumentId = documentId,
            Count = count,
            QuestionType = questionType,
            Status = "queued",
            Percent = 0,
            Stage = "queued",
            Message = "Đã tạo job",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
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
    public string Status { get; set; } = "queued";
    public int Percent { get; set; }
    public string Stage { get; set; } = "queued";
    public string? Message { get; set; }
    public int? Current { get; set; }
    public int? Total { get; set; }
    public string? TopicTag { get; set; }
    public int? QuestionsGenerated { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
