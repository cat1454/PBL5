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
        int? testResultId = null,
        CancellationToken cancellationToken = default);

    Task<LearningTestResultSnapshot> SubmitTestAsync(
        string userId,
        int documentId,
        LearningTestType testType,
        DateTime? startedAt,
        long? durationMs,
        IReadOnlyList<LearningTestAnswerSubmission> answers,
        bool recordAttempts,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LearningTestResultSnapshot>> GetDocumentTestResultsAsync(
        string userId,
        int documentId,
        CancellationToken cancellationToken = default);

    Task<LearningTestSummarySnapshot> GetDocumentTestSummaryAsync(
        string userId,
        int documentId,
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

public class LearningTestAnswerSubmission
{
    public int QuestionId { get; set; }
    public string? SelectedAnswer { get; set; }
    public bool IsCorrect { get; set; }
    public int? ResponseTimeMs { get; set; }
    public string? QuestionText { get; set; }
    public string? CorrectAnswer { get; set; }
    public string? Topic { get; set; }
}

public class LearningTestResultSnapshot
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int DocumentId { get; set; }
    public int TotalQuestions { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public double Score { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime SubmittedAt { get; set; }
    public long DurationMs { get; set; }
    public LearningTestType TestType { get; set; }
    public DateTime CreatedAt { get; set; }
    public double MasteryScoreAfterTest { get; set; }
    public double MemoryScoreAfterTest { get; set; }
    public IReadOnlyList<LearningTestWeakQuestionSnapshot> WeakQuestions { get; set; } = Array.Empty<LearningTestWeakQuestionSnapshot>();
}

public class LearningTestWeakQuestionSnapshot
{
    public int QuestionId { get; set; }
    public string? QuestionText { get; set; }
    public string? SelectedAnswer { get; set; }
    public string? CorrectAnswer { get; set; }
    public string? Topic { get; set; }
    public double MasteryScore { get; set; }
}

public class LearningTestSummarySnapshot
{
    public int TotalTests { get; set; }
    public double AverageScore { get; set; }
    public double BestScore { get; set; }
    public LearningTestResultSnapshot? LatestResult { get; set; }
}
