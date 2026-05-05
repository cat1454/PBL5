using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.API.Services;

public interface ILearningProgressService
{
    Task<LearningProgressSnapshot> RecordAttemptAsync(
        string userId,
        int documentId,
        int questionId,
        LearningMode mode,
        string? selectedAnswer,
        bool isCorrect,
        int? responseTimeMs,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LearningProgressSnapshot>> GetDocumentProgressAsync(
        string userId,
        int documentId,
        CancellationToken cancellationToken = default);

    Task<LearningProgressSummarySnapshot> GetDocumentSummaryAsync(
        string userId,
        int documentId,
        int totalQuestions,
        CancellationToken cancellationToken = default);
}

public class LearningProgressSnapshot
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int DocumentId { get; set; }
    public int QuestionId { get; set; }
    public int AttemptCount { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int CurrentStreak { get; set; }
    public int BestStreak { get; set; }
    public DateTime? LastReviewedAt { get; set; }
    public double MemoryScore { get; set; }
    public double MasteryScore { get; set; }
    public LearningLevel Level { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class LearningProgressSummarySnapshot
{
    public int TotalQuestions { get; set; }
    public int AttemptedQuestions { get; set; }
    public double AverageMasteryScore { get; set; }
    public double AverageMemoryScore { get; set; }
    public int WeakCount { get; set; }
    public int MasteredCount { get; set; }
}
