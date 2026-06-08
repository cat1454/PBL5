using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.API.Services;

public sealed class ClassroomLeaderboardService : IClassroomLeaderboardService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IClassroomPermissionService _permissionService;

    public ClassroomLeaderboardService(
        ApplicationDbContext dbContext,
        IClassroomPermissionService permissionService)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
    }

    public async Task<AssignmentLeaderboardResponse> GetAssignmentLeaderboardAsync(
        int assignmentId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _dbContext.ClassroomAssignments
            .Include(a => a.ClassroomWorkspace)
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);

        if (assignment == null)
        {
            throw new KeyNotFoundException("Assignment was not found.");
        }

        // Permission check
        var isTeacher = await _permissionService.CanManageClassroomAsync(assignment.ClassroomWorkspaceId, actorUserId, cancellationToken);
        if (!isTeacher)
        {
            var isStudent = await _permissionService.IsStudentAsync(assignment.ClassroomWorkspaceId, actorUserId, cancellationToken);
            if (!isStudent)
            {
                throw new UnauthorizedAccessException("You are not a member of this classroom.");
            }
            if (assignment.Status == ClassroomAssignmentStatus.Draft)
            {
                throw new UnauthorizedAccessException("This assignment is not available.");
            }
        }

        // Get all active student members
        var students = await _dbContext.ClassroomMembers
            .Include(m => m.User)
            .Where(m => m.ClassroomWorkspaceId == assignment.ClassroomWorkspaceId
                        && m.Role == ClassroomRole.Student
                        && m.Status == ClassroomMemberStatus.Active)
            .Select(m => m.User)
            .Where(u => u != null && u.IsActive)
            .ToListAsync(cancellationToken);

        // Get all attempts for this assignment for the active students
        var studentIds = students.Select(s => s!.Id).ToList();
        var attempts = await _dbContext.ClassroomAssignmentAttempts
            .Where(a => a.ClassroomAssignmentId == assignmentId && studentIds.Contains(a.UserId))
            .ToListAsync(cancellationToken);

        var attemptsByStudent = attempts.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.ToList());

        var rowsList = new List<AssignmentLeaderboardRow>();
        int submittedCount = 0;
        int inProgressCount = 0;

        foreach (var student in students)
        {
            if (student == null) continue;

            var studentAttempts = attemptsByStudent.TryGetValue(student.Id, out var atts) ? atts : new List<ClassroomAssignmentAttempt>();
            var attemptCount = studentAttempts.Count;

            // Find best submitted attempt
            var submittedAttempts = studentAttempts.Where(a => a.Status == ClassroomAttemptStatus.Submitted).ToList();
            var bestAttempt = submittedAttempts
                .OrderByDescending(a => a.PercentScore)
                .ThenByDescending(a => a.RawScore)
                .ThenBy(a => a.DurationSeconds ?? int.MaxValue)
                .ThenBy(a => a.SubmittedAt ?? DateTime.MaxValue)
                .FirstOrDefault();

            string statusLabel;
            if (bestAttempt != null)
            {
                statusLabel = "Submitted";
                submittedCount++;
            }
            else if (studentAttempts.Any(a => a.Status == ClassroomAttemptStatus.InProgress))
            {
                statusLabel = "InProgress";
                inProgressCount++;
            }
            else
            {
                statusLabel = "NotStarted";
            }

            rowsList.Add(new AssignmentLeaderboardRow
            {
                UserId = student.Id,
                DisplayName = student.FullName,
                Email = student.Email,
                BestAttemptId = bestAttempt?.Id,
                AttemptNumber = bestAttempt?.AttemptNumber,
                RawScore = bestAttempt?.RawScore,
                PercentScore = bestAttempt?.PercentScore,
                DurationSeconds = bestAttempt?.DurationSeconds,
                SubmittedAt = bestAttempt?.SubmittedAt,
                AttemptCount = attemptCount,
                StatusLabel = statusLabel
            });
        }

        // Rank the rows: only rank submitted
        var rankedRows = rowsList
            .Where(r => r.StatusLabel == "Submitted")
            .OrderByDescending(r => r.PercentScore ?? 0m)
            .ThenByDescending(r => r.RawScore ?? 0m)
            .ThenBy(r => r.DurationSeconds ?? int.MaxValue)
            .ThenBy(r => r.SubmittedAt ?? DateTime.MaxValue)
            .ToList();

        for (int i = 0; i < rankedRows.Count; i++)
        {
            rankedRows[i].Rank = i + 1;
        }

        var nonRankedRows = rowsList
            .Where(r => r.StatusLabel != "Submitted")
            .OrderBy(r => r.StatusLabel == "InProgress" ? 0 : 1)
            .ThenBy(r => r.DisplayName)
            .ThenBy(r => r.UserId)
            .ToList();

        var finalRows = rankedRows.Concat(nonRankedRows).ToList();

        // Determine score finality
        string scoreFinality;
        if (assignment.Status == ClassroomAssignmentStatus.Closed)
        {
            scoreFinality = "Final";
        }
        else if (assignment.ScoringMode == ClassroomScoringMode.EmpiricalDifficulty)
        {
            scoreFinality = "Temporary";
        }
        else
        {
            scoreFinality = "Final";
        }

        return new AssignmentLeaderboardResponse
        {
            AssignmentId = assignment.Id,
            ClassroomId = assignment.ClassroomWorkspaceId,
            AssignmentTitle = assignment.Title,
            AssignmentStatus = assignment.Status.ToString(),
            ScoringMode = assignment.ScoringMode.ToString(),
            ScoreFinality = scoreFinality,
            GeneratedAt = DateTime.UtcNow,
            TotalStudents = students.Count,
            SubmittedStudents = submittedCount,
            InProgressStudents = inProgressCount,
            NotStartedStudents = students.Count - submittedCount - inProgressCount,
            Rows = finalRows
        };
    }

    public async Task<ClassroomLeaderboardResponse> GetClassroomLeaderboardAsync(
        int classroomId,
        int actorUserId,
        CancellationToken cancellationToken = default)
    {
        var classroom = await _dbContext.ClassroomWorkspaces
            .FirstOrDefaultAsync(w => w.Id == classroomId && !w.IsArchived, cancellationToken);

        if (classroom == null)
        {
            throw new KeyNotFoundException("Classroom workspace was not found.");
        }

        // Permission check
        var canView = await _permissionService.CanViewClassroomAsync(classroomId, actorUserId, cancellationToken);
        if (!canView)
        {
            throw new UnauthorizedAccessException("You do not have permission to view this classroom's leaderboard.");
        }

        // Get all active student members
        var students = await _dbContext.ClassroomMembers
            .Include(m => m.User)
            .Where(m => m.ClassroomWorkspaceId == classroomId
                        && m.Role == ClassroomRole.Student
                        && m.Status == ClassroomMemberStatus.Active)
            .Select(m => m.User)
            .Where(u => u != null && u.IsActive)
            .ToListAsync(cancellationToken);

        // Get all Published or Closed assignments
        var assignments = await _dbContext.ClassroomAssignments
            .Where(a => a.ClassroomWorkspaceId == classroomId
                        && (a.Status == ClassroomAssignmentStatus.Published || a.Status == ClassroomAssignmentStatus.Closed))
            .ToListAsync(cancellationToken);

        var assignmentIds = assignments.Select(a => a.Id).ToList();
        var studentIds = students.Select(s => s!.Id).ToList();

        // Load all attempts for these assignments of active students
        var attempts = await _dbContext.ClassroomAssignmentAttempts
            .Where(a => assignmentIds.Contains(a.ClassroomAssignmentId) && studentIds.Contains(a.UserId))
            .ToListAsync(cancellationToken);

        // Group attempts by student first, then by assignment
        var attemptsByStudentAndAssignment = attempts
            .GroupBy(a => (a.UserId, a.ClassroomAssignmentId))
            .ToDictionary(g => g.Key, g => g.ToList());

        var rowsList = new List<ClassroomLeaderboardRow>();

        foreach (var student in students)
        {
            if (student == null) continue;

            int completedAssignmentsCount = 0;
            int totalSubmittedAttemptsCount = 0;
            decimal totalPercentScore = 0m;
            decimal bestPercentScore = 0m;
            DateTime? latestSubmittedAt = null;

            foreach (var assignment in assignments)
            {
                var key = (student.Id, assignment.Id);
                if (attemptsByStudentAndAssignment.TryGetValue(key, out var studentAssignmentAttempts))
                {
                    var submittedAttempts = studentAssignmentAttempts.Where(a => a.Status == ClassroomAttemptStatus.Submitted).ToList();
                    totalSubmittedAttemptsCount += submittedAttempts.Count;

                    var bestAttempt = submittedAttempts
                        .OrderByDescending(a => a.PercentScore)
                        .ThenByDescending(a => a.RawScore)
                        .ThenBy(a => a.DurationSeconds ?? int.MaxValue)
                        .ThenBy(a => a.SubmittedAt ?? DateTime.MaxValue)
                        .FirstOrDefault();

                    if (bestAttempt != null)
                    {
                        completedAssignmentsCount++;
                        totalPercentScore += bestAttempt.PercentScore;
                        if (bestAttempt.PercentScore > bestPercentScore)
                        {
                            bestPercentScore = bestAttempt.PercentScore;
                        }

                        if (bestAttempt.SubmittedAt.HasValue)
                        {
                            if (latestSubmittedAt == null || bestAttempt.SubmittedAt.Value > latestSubmittedAt.Value)
                            {
                                latestSubmittedAt = bestAttempt.SubmittedAt.Value;
                            }
                        }
                    }
                }
            }

            decimal averagePercentScore = completedAssignmentsCount > 0
                ? Math.Round(totalPercentScore / completedAssignmentsCount, 2, MidpointRounding.AwayFromZero)
                : 0m;

            rowsList.Add(new ClassroomLeaderboardRow
            {
                UserId = student.Id,
                DisplayName = student.FullName,
                Email = student.Email,
                CompletedAssignments = completedAssignmentsCount,
                SubmittedAttempts = totalSubmittedAttemptsCount,
                AveragePercentScore = averagePercentScore,
                TotalPercentScore = totalPercentScore,
                BestPercentScore = bestPercentScore,
                LatestSubmittedAt = latestSubmittedAt
            });
        }

        // Rank the students
        var sortedRows = rowsList
            .OrderByDescending(r => r.AveragePercentScore)
            .ThenByDescending(r => r.CompletedAssignments)
            .ThenByDescending(r => r.TotalPercentScore)
            .ThenBy(r => r.LatestSubmittedAt ?? DateTime.MaxValue)
            .ThenBy(r => r.DisplayName)
            .ThenBy(r => r.UserId)
            .ToList();

        for (int i = 0; i < sortedRows.Count; i++)
        {
            sortedRows[i].Rank = i + 1;
        }

        return new ClassroomLeaderboardResponse
        {
            ClassroomId = classroom.Id,
            ClassroomName = classroom.Name,
            GeneratedAt = DateTime.UtcNow,
            AssignmentCount = assignments.Count,
            ActiveStudentCount = students.Count,
            Rows = sortedRows
        };
    }
}
