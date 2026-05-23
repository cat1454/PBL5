using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;

namespace ELearnGamePlatform.API.Services;

public class LearningProgressService : ILearningProgressService
{
    private const double NeutralSpeedScore = 50d;

    private readonly ApplicationDbContext _dbContext;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public LearningProgressService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<LearningProgressSnapshot> RecordAttemptAsync(
        string userId,
        int documentId,
        int questionId,
        LearningMode mode,
        string? selectedAnswer,
        bool isCorrect,
        int? responseTimeMs,
        int? testResultId = null,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await RecordAttemptCoreAsync(
                    userId,
                    documentId,
                    questionId,
                    mode,
                    selectedAnswer,
                    isCorrect,
                    responseTimeMs,
                    testResultId,
                    cancellationToken);
            }
            catch (DbUpdateException ex) when (attempt == 0 && IsLearningProgressUniqueViolation(ex))
            {
                _dbContext.ChangeTracker.Clear();
            }
        }

        return await RecordAttemptCoreAsync(
            userId,
            documentId,
            questionId,
            mode,
            selectedAnswer,
            isCorrect,
            responseTimeMs,
            testResultId,
            cancellationToken);
    }

    private async Task<LearningProgressSnapshot> RecordAttemptCoreAsync(
        string userId,
        int documentId,
        int questionId,
        LearningMode mode,
        string? selectedAnswer,
        bool isCorrect,
        int? responseTimeMs,
        int? testResultId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        AddAttempt(userId, documentId, questionId, mode, selectedAnswer, isCorrect, responseTimeMs, now, testResultId);
        var progress = await ApplyProgressAsync(userId, documentId, questionId, isCorrect, responseTimeMs, now, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToSnapshot(progress, now);
    }

    public async Task<LearningTestStartSnapshot> StartTestAsync(
        string userId,
        int documentId,
        LearningTestType testType,
        IReadOnlyList<LearningTestQuestionStartSnapshot> questions,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var testResult = new LearningTestResult
        {
            UserId = userId,
            DocumentId = documentId,
            TotalQuestions = questions.Count,
            CorrectCount = 0,
            WrongCount = 0,
            Score = 0d,
            StartedAt = now,
            SubmittedAt = now,
            DurationMs = 0,
            TestType = testType,
            TestSessionId = Guid.NewGuid(),
            Status = LearningTestResultStatus.InProgress,
            QuestionIdsJson = JsonSerializer.Serialize(questions.Select(question => question.Id).ToList(), JsonOptions),
            CreatedAt = now
        };

        _dbContext.LearningTestResults.Add(testResult);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new LearningTestStartSnapshot
        {
            TestSessionId = testResult.TestSessionId,
            TestResultDraftId = testResult.Id,
            DocumentId = documentId,
            TestType = testType,
            StartedAt = testResult.StartedAt,
            Questions = questions
        };
    }

    public async Task<LearningTestResultSnapshot> SubmitTestAsync(
        string userId,
        Guid testSessionId,
        long? durationMs,
        IReadOnlyList<LearningTestAnswerSubmission> answers,
        CancellationToken cancellationToken = default)
    {
        var submittedAt = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var sessionLockKey = $"learning-test:{userId}:{testSessionId}";
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({sessionLockKey})::bigint);",
            cancellationToken);

        var testResult = await _dbContext.LearningTestResults
            .FirstOrDefaultAsync(
                result => result.UserId == userId && result.TestSessionId == testSessionId,
                cancellationToken);

        if (testResult == null)
        {
            throw new InvalidOperationException("Test session not found.");
        }

        if (testResult.Status == LearningTestResultStatus.Completed)
        {
            await transaction.CommitAsync(cancellationToken);
            return ToTestResultSnapshot(testResult, new Dictionary<int, LearningProgress>(), Array.Empty<LearningTestAnswerSubmission>(), submittedAt);
        }

        var expectedQuestionIds = GetQuestionIds(testResult);
        if (expectedQuestionIds.Count == 0)
        {
            throw new InvalidOperationException("Test session has no questions.");
        }

        var submittedQuestionIds = answers.Select(answer => answer.QuestionId).ToHashSet();
        if (submittedQuestionIds.Count != expectedQuestionIds.Count || expectedQuestionIds.Any(questionId => !submittedQuestionIds.Contains(questionId)))
        {
            throw new InvalidOperationException("Test answers do not match the started test session.");
        }

        var resolvedDurationMs = Math.Max(0, durationMs ?? (long)Math.Max(0, (submittedAt - testResult.StartedAt).TotalMilliseconds));

        var correctCount = answers.Count(answer => answer.IsCorrect);
        var wrongCount = answers.Count - correctCount;

        var progressByQuestionId = new Dictionary<int, LearningProgress>();
        foreach (var answer in answers)
        {
            AddAttempt(
                userId,
                testResult.DocumentId,
                answer.QuestionId,
                LearningMode.Test,
                answer.SelectedAnswer,
                answer.IsCorrect,
                answer.ResponseTimeMs,
                submittedAt,
                testResult);

            progressByQuestionId[answer.QuestionId] = await ApplyProgressAsync(
                userId,
                testResult.DocumentId,
                answer.QuestionId,
                answer.IsCorrect,
                answer.ResponseTimeMs,
                submittedAt,
                cancellationToken);
        }

        testResult.TotalQuestions = answers.Count;
        testResult.CorrectCount = correctCount;
        testResult.WrongCount = wrongCount;
        testResult.Score = answers.Count > 0 ? RoundScore((double)correctCount / answers.Count * 100d) : 0d;
        testResult.SubmittedAt = submittedAt;
        testResult.DurationMs = resolvedDurationMs;
        testResult.Status = LearningTestResultStatus.Completed;
        testResult.ResultSnapshotJson = JsonSerializer.Serialize(
            BuildPersistedSnapshot(testResult, progressByQuestionId, answers, submittedAt),
            JsonOptions);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToTestResultSnapshot(testResult, progressByQuestionId, answers, submittedAt);
    }

    public async Task<IReadOnlyList<LearningTestResultSnapshot>> GetDocumentTestResultsAsync(
        string userId,
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var results = await _dbContext.LearningTestResults
            .Where(item => item.UserId == userId
                && item.DocumentId == documentId
                && item.Status == LearningTestResultStatus.Completed)
            .OrderByDescending(item => item.SubmittedAt)
            .ToListAsync(cancellationToken);

        if (results.Count == 0)
        {
            return Array.Empty<LearningTestResultSnapshot>();
        }

        return results
            .Select(result => ToTestResultSnapshot(result, new Dictionary<int, LearningProgress>(), Array.Empty<LearningTestAnswerSubmission>(), now))
            .ToList();
    }

    public async Task<LearningTestSummarySnapshot> GetDocumentTestSummaryAsync(
        string userId,
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var results = await GetDocumentTestResultsAsync(userId, documentId, cancellationToken);
        var latest = results.FirstOrDefault();

        return new LearningTestSummarySnapshot
        {
            TotalTests = results.Count,
            AverageScore = RoundScore(results.Any() ? results.Average(item => item.Score) : 0d),
            BestScore = RoundScore(results.Any() ? results.Max(item => item.Score) : 0d),
            LatestResult = latest
        };
    }

    public async Task<IReadOnlyList<LearningProgressSnapshot>> GetDocumentProgressAsync(
        string userId,
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var progresses = await _dbContext.LearningProgresses
            .Where(item => item.UserId == userId && item.DocumentId == documentId)
            .OrderBy(item => item.QuestionId)
            .ToListAsync(cancellationToken);

        return progresses.Select(progress => ToSnapshot(progress, now)).ToList();
    }

    public async Task<LearningProgressSummarySnapshot> GetDocumentSummaryAsync(
        string userId,
        int documentId,
        int totalQuestions,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var progresses = await _dbContext.LearningProgresses
            .Where(item => item.UserId == userId && item.DocumentId == documentId)
            .ToListAsync(cancellationToken);

        var snapshots = progresses.Select(progress => ToSnapshot(progress, now)).ToList();

        return new LearningProgressSummarySnapshot
        {
            TotalQuestions = totalQuestions,
            AttemptedQuestions = snapshots.Count(item => item.AttemptCount > 0),
            AverageMasteryScore = RoundScore(snapshots.Any() ? snapshots.Average(item => item.MasteryScore) : 0d),
            AverageMemoryScore = RoundScore(snapshots.Any() ? snapshots.Average(item => item.MemoryScore) : 0d),
            WeakCount = snapshots.Count(item => item.Level == LearningLevel.Weak),
            MasteredCount = snapshots.Count(item => item.Level == LearningLevel.Mastered)
        };
    }

    private void AddAttempt(
        string userId,
        int documentId,
        int questionId,
        LearningMode mode,
        string? selectedAnswer,
        bool isCorrect,
        int? responseTimeMs,
        DateTime createdAt,
        int? testResultId = null)
    {
        _dbContext.LearningAttempts.Add(new LearningAttempt
        {
            UserId = userId,
            DocumentId = documentId,
            QuestionId = questionId,
            Mode = mode,
            SelectedAnswer = selectedAnswer,
            IsCorrect = isCorrect,
            ResponseTimeMs = responseTimeMs,
            TestResultId = testResultId,
            CreatedAt = createdAt
        });
    }

    private void AddAttempt(
        string userId,
        int documentId,
        int questionId,
        LearningMode mode,
        string? selectedAnswer,
        bool isCorrect,
        int? responseTimeMs,
        DateTime createdAt,
        LearningTestResult testResult)
    {
        _dbContext.LearningAttempts.Add(new LearningAttempt
        {
            UserId = userId,
            DocumentId = documentId,
            QuestionId = questionId,
            Mode = mode,
            SelectedAnswer = selectedAnswer,
            IsCorrect = isCorrect,
            ResponseTimeMs = responseTimeMs,
            TestResult = testResult,
            CreatedAt = createdAt
        });
    }

    private async Task<LearningProgress> ApplyProgressAsync(
        string userId,
        int documentId,
        int questionId,
        bool isCorrect,
        int? responseTimeMs,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var lockKey = $"{userId}:{documentId}:{questionId}";
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({lockKey})::bigint);",
            cancellationToken);

        var progress = await _dbContext.LearningProgresses
            .FirstOrDefaultAsync(
                item => item.UserId == userId
                    && item.DocumentId == documentId
                    && item.QuestionId == questionId,
                cancellationToken);

        if (progress == null)
        {
            progress = new LearningProgress
            {
                UserId = userId,
                DocumentId = documentId,
                QuestionId = questionId,
                LastReviewedAt = now,
                UpdatedAt = now
            };
            _dbContext.LearningProgresses.Add(progress);
        }

        var previousLastReviewedAt = progress.LastReviewedAt;

        progress.AttemptCount++;
        if (isCorrect)
        {
            progress.CorrectCount++;
            progress.CurrentStreak++;
            progress.BestStreak = Math.Max(progress.BestStreak, progress.CurrentStreak);
        }
        else
        {
            progress.WrongCount++;
            progress.CurrentStreak = 0;
        }

        var accuracyScore = CalculateAccuracyScore(progress);
        var recencyScore = CalculateRecencyScore(previousLastReviewedAt, now);
        var streakScore = CalculateStreakScore(progress);
        var speedScore = CalculateSpeedScore(responseTimeMs);
        var masteryScore = CalculateMasteryScore(accuracyScore, recencyScore, streakScore, speedScore);
        var memoryScore = CalculateMemoryScore(masteryScore, previousLastReviewedAt, now);

        progress.MasteryScore = masteryScore;
        progress.MemoryScore = memoryScore;
        progress.Level = ClassifyLevel(masteryScore);
        progress.LastReviewedAt = now;
        progress.UpdatedAt = now;

        return progress;
    }

    private static LearningProgressSnapshot ToSnapshot(LearningProgress progress, DateTime nowUtc)
    {
        return new LearningProgressSnapshot
        {
            Id = progress.Id,
            UserId = progress.UserId,
            DocumentId = progress.DocumentId,
            QuestionId = progress.QuestionId,
            AttemptCount = progress.AttemptCount,
            CorrectCount = progress.CorrectCount,
            WrongCount = progress.WrongCount,
            CurrentStreak = progress.CurrentStreak,
            BestStreak = progress.BestStreak,
            LastReviewedAt = progress.LastReviewedAt,
            MasteryScore = RoundScore(progress.MasteryScore),
            MemoryScore = RoundScore(CalculateMemoryScore(progress.MasteryScore, progress.LastReviewedAt, nowUtc)),
            Level = progress.Level,
            UpdatedAt = progress.UpdatedAt
        };
    }

    private static LearningTestResultSnapshot ToTestResultSnapshot(
        LearningTestResult result,
        IReadOnlyDictionary<int, LearningProgress> progressByQuestionId,
        IReadOnlyList<LearningTestAnswerSubmission> answers,
        DateTime nowUtc)
    {
        var persistedSnapshot = DeserializePersistedSnapshot(result.ResultSnapshotJson);
        if (persistedSnapshot != null)
        {
            return persistedSnapshot;
        }

        var relatedProgress = progressByQuestionId.Values
            .Where(progress => progress.DocumentId == result.DocumentId)
            .Select(progress => ToSnapshot(progress, nowUtc))
            .ToList();

        var masteryScoreAfterTest = relatedProgress.Count > 0
            ? RoundScore(relatedProgress.Average(item => item.MasteryScore))
            : 0d;
        var memoryScoreAfterTest = relatedProgress.Count > 0
            ? RoundScore(relatedProgress.Average(item => item.MemoryScore))
            : 0d;

        var weakQuestions = answers
            .Where(answer => !answer.IsCorrect)
            .Select(answer =>
            {
                progressByQuestionId.TryGetValue(answer.QuestionId, out var progress);
                return new LearningTestWeakQuestionSnapshot
                {
                    QuestionId = answer.QuestionId,
                    QuestionText = answer.QuestionText,
                    SelectedAnswer = answer.SelectedAnswer,
                    CorrectAnswer = answer.CorrectAnswer,
                    Topic = answer.Topic,
                    MasteryScore = RoundScore(progress?.MasteryScore ?? 0d)
                };
            })
            .ToList();

        return new LearningTestResultSnapshot
        {
            Id = result.Id,
            TestSessionId = result.TestSessionId,
            UserId = result.UserId,
            DocumentId = result.DocumentId,
            TotalQuestions = result.TotalQuestions,
            CorrectCount = result.CorrectCount,
            WrongCount = result.WrongCount,
            Score = RoundScore(result.Score),
            StartedAt = result.StartedAt,
            SubmittedAt = result.SubmittedAt,
            DurationMs = result.DurationMs,
            TestType = result.TestType,
            CreatedAt = result.CreatedAt,
            MasteryScoreAfterTest = masteryScoreAfterTest,
            MemoryScoreAfterTest = memoryScoreAfterTest,
            Answers = BuildAnswerResultSnapshots(answers, progressByQuestionId, nowUtc),
            WeakQuestions = weakQuestions
        };
    }

    private static LearningTestResultSnapshot BuildPersistedSnapshot(
        LearningTestResult result,
        IReadOnlyDictionary<int, LearningProgress> progressByQuestionId,
        IReadOnlyList<LearningTestAnswerSubmission> answers,
        DateTime nowUtc)
    {
        var answerResults = BuildAnswerResultSnapshots(answers, progressByQuestionId, nowUtc);
        var masteryScoreAfterTest = answerResults.Count > 0
            ? RoundScore(answerResults.Average(item => item.MasteryScore))
            : 0d;
        var memoryScoreAfterTest = answerResults.Count > 0
            ? RoundScore(answerResults.Average(item => item.MemoryScore))
            : 0d;

        return new LearningTestResultSnapshot
        {
            Id = result.Id,
            TestSessionId = result.TestSessionId,
            UserId = result.UserId,
            DocumentId = result.DocumentId,
            TotalQuestions = result.TotalQuestions,
            CorrectCount = result.CorrectCount,
            WrongCount = result.WrongCount,
            Score = RoundScore(result.Score),
            StartedAt = result.StartedAt,
            SubmittedAt = result.SubmittedAt,
            DurationMs = result.DurationMs,
            TestType = result.TestType,
            CreatedAt = result.CreatedAt,
            MasteryScoreAfterTest = masteryScoreAfterTest,
            MemoryScoreAfterTest = memoryScoreAfterTest,
            Answers = answerResults,
            WeakQuestions = answerResults
                .Where(answer => !answer.IsCorrect)
                .Select(answer => new LearningTestWeakQuestionSnapshot
                {
                    QuestionId = answer.QuestionId,
                    QuestionText = answer.QuestionText,
                    SelectedAnswer = answer.SelectedAnswer,
                    CorrectAnswer = answer.CorrectAnswer,
                    Topic = answer.Topic,
                    MasteryScore = answer.MasteryScore
                })
                .ToList()
        };
    }

    private static IReadOnlyList<LearningTestAnswerResultSnapshot> BuildAnswerResultSnapshots(
        IReadOnlyList<LearningTestAnswerSubmission> answers,
        IReadOnlyDictionary<int, LearningProgress> progressByQuestionId,
        DateTime nowUtc)
    {
        return answers
            .Select(answer =>
            {
                progressByQuestionId.TryGetValue(answer.QuestionId, out var progress);
                var progressSnapshot = progress != null ? ToSnapshot(progress, nowUtc) : null;

                return new LearningTestAnswerResultSnapshot
                {
                    QuestionId = answer.QuestionId,
                    QuestionText = answer.QuestionText,
                    SelectedAnswer = answer.SelectedAnswer,
                    CorrectAnswer = answer.CorrectAnswer,
                    IsCorrect = answer.IsCorrect,
                    ResponseTimeMs = answer.ResponseTimeMs,
                    Topic = answer.Topic,
                    MasteryScore = RoundScore(progressSnapshot?.MasteryScore ?? 0d),
                    MemoryScore = RoundScore(progressSnapshot?.MemoryScore ?? 0d)
                };
            })
            .ToList();
    }

    private static LearningTestResultSnapshot? DeserializePersistedSnapshot(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LearningTestResultSnapshot>(snapshotJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<int> GetQuestionIds(LearningTestResult result)
    {
        if (string.IsNullOrWhiteSpace(result.QuestionIdsJson))
        {
            return Array.Empty<int>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<int>>(result.QuestionIdsJson, JsonOptions) ?? new List<int>();
        }
        catch (JsonException)
        {
            return Array.Empty<int>();
        }
    }

    private static double CalculateAccuracyScore(LearningProgress progress)
    {
        if (progress.AttemptCount <= 0)
        {
            return 0d;
        }

        return (double)progress.CorrectCount / progress.AttemptCount * 100d;
    }

    private static double CalculateRecencyScore(DateTime? lastReviewedAt, DateTime nowUtc)
    {
        return CalculateDecayFactor(lastReviewedAt, nowUtc) * 100d;
    }

    private static double CalculateStreakScore(LearningProgress progress)
    {
        if (progress.BestStreak <= 0)
        {
            return 0d;
        }

        return (double)progress.CurrentStreak / progress.BestStreak * 100d;
    }

    private static double CalculateSpeedScore(int? responseTimeMs)
    {
        if (!responseTimeMs.HasValue || responseTimeMs.Value <= 0)
        {
            return NeutralSpeedScore;
        }

        return responseTimeMs.Value switch
        {
            <= 3000 => 100d,
            <= 7000 => 80d,
            <= 12000 => 60d,
            <= 20000 => 40d,
            _ => 20d
        };
    }

    private static double CalculateMasteryScore(
        double accuracyScore,
        double recencyScore,
        double streakScore,
        double speedScore)
    {
        return ClampScore(
            (0.5d * accuracyScore)
            + (0.2d * recencyScore)
            + (0.2d * streakScore)
            + (0.1d * speedScore));
    }

    private static double CalculateMemoryScore(double masteryScore, DateTime? lastReviewedAt, DateTime nowUtc)
    {
        return ClampScore(masteryScore * CalculateDecayFactor(lastReviewedAt, nowUtc));
    }

    private static double CalculateDecayFactor(DateTime? lastReviewedAt, DateTime nowUtc)
    {
        if (!lastReviewedAt.HasValue)
        {
            return 1d;
        }

        var daysSinceLastReview = Math.Max(0d, (nowUtc - lastReviewedAt.Value).TotalDays);
        return 1d / (1d + (daysSinceLastReview * 0.1d));
    }

    private static LearningLevel ClassifyLevel(double masteryScore)
    {
        if (masteryScore >= 86d)
        {
            return LearningLevel.Mastered;
        }

        if (masteryScore >= 70d)
        {
            return LearningLevel.Good;
        }

        if (masteryScore >= 40d)
        {
            return LearningLevel.Learning;
        }

        return LearningLevel.Weak;
    }

    private static double ClampScore(double score) => Math.Clamp(score, 0d, 100d);

    private static double RoundScore(double score) => Math.Round(score, 2, MidpointRounding.AwayFromZero);

    private static bool IsLearningProgressUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && string.Equals(
                postgresException.ConstraintName,
                "IX_learning_progresses_user_id_document_id_question_id",
                StringComparison.Ordinal);
    }
}
