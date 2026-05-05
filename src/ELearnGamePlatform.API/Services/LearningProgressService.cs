using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.API.Services;

public class LearningProgressService : ILearningProgressService
{
    private const double NeutralSpeedScore = 50d;

    private readonly ApplicationDbContext _dbContext;

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
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var attempt = new LearningAttempt
        {
            UserId = userId,
            DocumentId = documentId,
            QuestionId = questionId,
            Mode = mode,
            SelectedAnswer = selectedAnswer,
            IsCorrect = isCorrect,
            ResponseTimeMs = responseTimeMs,
            CreatedAt = now
        };

        _dbContext.LearningAttempts.Add(attempt);

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

        progress.LastReviewedAt = now;
        progress.UpdatedAt = now;

        var accuracyScore = CalculateAccuracyScore(progress);
        var recencyScore = CalculateRecencyScore(progress.LastReviewedAt, now);
        var streakScore = CalculateStreakScore(progress);
        var speedScore = CalculateSpeedScore(responseTimeMs);
        var masteryScore = CalculateMasteryScore(accuracyScore, recencyScore, streakScore, speedScore);
        var memoryScore = CalculateMemoryScore(masteryScore, progress.LastReviewedAt, now);

        progress.MasteryScore = masteryScore;
        progress.MemoryScore = memoryScore;
        progress.Level = ClassifyLevel(masteryScore);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToSnapshot(progress, now);
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
        if (masteryScore >= 85d)
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
}
