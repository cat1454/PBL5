using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ELearnGamePlatform.API.Services;

public interface IClassroomAnalyticsService
{
    Task<ClassroomAnalyticsResponse> GetTeacherAnalyticsAsync(
        int classroomId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<StudentClassroomAnalyticsResponse> GetStudentAnalyticsAsync(
        int classroomId,
        int actorUserId,
        CancellationToken cancellationToken = default);
}

// ──────────────────────────────────────────────────────────────────────────────
// Teacher analytics DTOs
// ──────────────────────────────────────────────────────────────────────────────

public sealed class ClassroomAnalyticsResponse
{
    public int ClassroomId { get; set; }
    public string ClassroomName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }

    public ClassroomAnalyticsOverview Overview { get; set; } = new();
    public List<AssignmentAnalyticsSummary> AssignmentSummaries { get; set; } = new();
    public QuestionDifficultyInsights QuestionInsights { get; set; } = new();
    public List<AtRiskStudent> AtRiskStudents { get; set; } = new();
}

public sealed class ClassroomAnalyticsOverview
{
    public int ActiveStudentCount { get; set; }
    public int AssignmentCount { get; set; }
    public int PublishedAssignmentCount { get; set; }
    public int ClosedAssignmentCount { get; set; }
    public int SubmittedAttemptCount { get; set; }
    public decimal AverageScore { get; set; }
    public decimal CompletionRate { get; set; }
}

public sealed class AssignmentAnalyticsSummary
{
    public int AssignmentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ScoringMode { get; set; } = string.Empty;
    public string ScoreFinality { get; set; } = string.Empty;
    public int TotalStudents { get; set; }
    public int SubmittedStudents { get; set; }
    public int InProgressStudents { get; set; }
    public int NotStartedStudents { get; set; }
    public decimal CompletionRate { get; set; }
    public decimal AveragePercentScore { get; set; }
    public decimal BestPercentScore { get; set; }
    public decimal LowestPercentScore { get; set; }
}

public sealed class QuestionDifficultyInsights
{
    public List<QuestionDifficultyStat> HardestQuestions { get; set; } = new();
    public List<QuestionDifficultyStat> EasiestQuestions { get; set; } = new();
    public List<QuestionDifficultyStat> SuspiciousQuestions { get; set; } = new();
}

public sealed class QuestionDifficultyStat
{
    public int QuestionId { get; set; }
    public int AssignmentId { get; set; }
    public string AssignmentTitle { get; set; } = string.Empty;
    public string? QuestionText { get; set; }
    public int CorrectCount { get; set; }
    public int AnsweredCount { get; set; }
    public decimal SmoothedCorrectRate { get; set; }
    public decimal DifficultyWeight { get; set; }
    public string? QualityFlag { get; set; }
}

public sealed class AtRiskStudent
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int CompletedAssignments { get; set; }
    public decimal AveragePercentScore { get; set; }
    public DateTime? LastSubmittedAt { get; set; }
}

// ──────────────────────────────────────────────────────────────────────────────
// Student analytics DTOs
// ──────────────────────────────────────────────────────────────────────────────

public sealed class StudentClassroomAnalyticsResponse
{
    public int ClassroomId { get; set; }
    public string ClassroomName { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }

    public StudentAnalyticsSummary Summary { get; set; } = new();
    public List<StudentAttemptSummary> RecentAttempts { get; set; } = new();

    // Simple hint flags
    public bool NeedsPractice { get; set; }
    public bool HasPendingAssignments { get; set; }
}

public sealed class StudentAnalyticsSummary
{
    public int CompletedAssignments { get; set; }
    public int TotalAssignments { get; set; }
    public decimal AveragePercentScore { get; set; }
    public decimal BestPercentScore { get; set; }
    public DateTime? LatestSubmittedAt { get; set; }
}

public sealed class StudentAttemptSummary
{
    public int AttemptId { get; set; }
    public int AssignmentId { get; set; }
    public string AssignmentTitle { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal RawScore { get; set; }
    public decimal PercentScore { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string ScoreFinality { get; set; } = string.Empty;
}
