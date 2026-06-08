using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ELearnGamePlatform.API.Services;

public interface IClassroomLeaderboardService
{
    Task<AssignmentLeaderboardResponse> GetAssignmentLeaderboardAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default);

    Task<ClassroomLeaderboardResponse> GetClassroomLeaderboardAsync(
        int classroomId,
        int actorUserId,
        CancellationToken cancellationToken = default);
}

public sealed class AssignmentLeaderboardResponse
{
    public int AssignmentId { get; set; }
    public int ClassroomId { get; set; }
    public string AssignmentTitle { get; set; } = string.Empty;
    public string AssignmentStatus { get; set; } = string.Empty;
    public string ScoringMode { get; set; } = string.Empty;
    public string ScoreFinality { get; set; } = string.Empty; // "Final" or "Temporary"
    public DateTime GeneratedAt { get; set; }
    public int TotalStudents { get; set; }
    public int SubmittedStudents { get; set; }
    public int InProgressStudents { get; set; }
    public int NotStartedStudents { get; set; }
    public List<AssignmentLeaderboardRow> Rows { get; set; } = new();
}

public sealed class AssignmentLeaderboardRow
{
    public int? Rank { get; set; }
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int? BestAttemptId { get; set; }
    public int? AttemptNumber { get; set; }
    public decimal? RawScore { get; set; }
    public decimal? PercentScore { get; set; }
    public int? DurationSeconds { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public int AttemptCount { get; set; }
    public string StatusLabel { get; set; } = string.Empty; // "Submitted", "InProgress", "NotStarted"
}

public sealed class ClassroomLeaderboardResponse
{
    public int ClassroomId { get; set; }
    public string ClassroomName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public int AssignmentCount { get; set; }
    public int ActiveStudentCount { get; set; }
    public List<ClassroomLeaderboardRow> Rows { get; set; } = new();
}

public sealed class ClassroomLeaderboardRow
{
    public int? Rank { get; set; }
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int CompletedAssignments { get; set; }
    public int SubmittedAttempts { get; set; }
    public decimal AveragePercentScore { get; set; }
    public decimal TotalPercentScore { get; set; }
    public decimal BestPercentScore { get; set; }
    public DateTime? LatestSubmittedAt { get; set; }
}
