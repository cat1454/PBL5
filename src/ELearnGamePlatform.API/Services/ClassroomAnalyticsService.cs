using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ELearnGamePlatform.Core.Enums;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ELearnGamePlatform.API.Services;

public sealed class ClassroomAnalyticsService : IClassroomAnalyticsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IClassroomPermissionService _permissionService;

    public ClassroomAnalyticsService(
        ApplicationDbContext dbContext,
        IClassroomPermissionService permissionService)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Teacher analytics
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<ClassroomAnalyticsResponse> GetTeacherAnalyticsAsync(
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

        var canManage = await _permissionService.CanManageClassroomAsync(classroomId, actorUserId, cancellationToken);
        if (!canManage)
        {
            throw new UnauthorizedAccessException("Only teachers or owners of this classroom can view classroom analytics.");
        }

        // Active students
        var students = await _dbContext.ClassroomMembers
            .Include(m => m.User)
            .Where(m => m.ClassroomWorkspaceId == classroomId
                        && m.Role == ClassroomRole.Student
                        && m.Status == ClassroomMemberStatus.Active)
            .Select(m => m.User)
            .Where(u => u != null && u.IsActive)
            .ToListAsync(cancellationToken);

        var studentIds = students.Select(s => s!.Id).ToList();

        // All assignments (including Draft for overview counts)
        var allAssignments = await _dbContext.ClassroomAssignments
            .Where(a => a.ClassroomWorkspaceId == classroomId)
            .ToListAsync(cancellationToken);

        // Visible assignments (Published + Closed) for attempt aggregation
        var visibleAssignments = allAssignments
            .Where(a => a.Status == ClassroomAssignmentStatus.Published || a.Status == ClassroomAssignmentStatus.Closed)
            .ToList();

        var visibleAssignmentIds = visibleAssignments.Select(a => a.Id).ToList();

        // All attempts for visible assignments
        var attempts = await _dbContext.ClassroomAssignmentAttempts
            .Where(a => visibleAssignmentIds.Contains(a.ClassroomAssignmentId) && studentIds.Contains(a.UserId))
            .ToListAsync(cancellationToken);

        var submittedAttempts = attempts.Where(a => a.Status == ClassroomAttemptStatus.Submitted).ToList();

        // Overview
        var overview = BuildOverview(students.Count, allAssignments, visibleAssignments, studentIds, submittedAttempts);

        // Assignment summaries
        var assignmentSummaries = BuildAssignmentSummaries(visibleAssignments, studentIds, attempts);

        // Question difficulty insights
        var questionInsights = await BuildQuestionInsightsAsync(visibleAssignmentIds, allAssignments, cancellationToken);

        // At-risk students
        var atRiskStudents = BuildAtRiskStudents(students!, visibleAssignments, attempts);

        return new ClassroomAnalyticsResponse
        {
            ClassroomId = classroom.Id,
            ClassroomName = classroom.Name,
            GeneratedAt = DateTime.UtcNow,
            Overview = overview,
            AssignmentSummaries = assignmentSummaries,
            QuestionInsights = questionInsights,
            AtRiskStudents = atRiskStudents
        };
    }

    private static ClassroomAnalyticsOverview BuildOverview(
        int activeStudentCount,
        List<Core.Entities.ClassroomAssignment> allAssignments,
        List<Core.Entities.ClassroomAssignment> visibleAssignments,
        List<int> studentIds,
        List<Core.Entities.ClassroomAssignmentAttempt> submittedAttempts)
    {
        var publishedCount = allAssignments.Count(a => a.Status == ClassroomAssignmentStatus.Published);
        var closedCount = allAssignments.Count(a => a.Status == ClassroomAssignmentStatus.Closed);

        var totalPossibleCompletions = (long)visibleAssignments.Count * activeStudentCount;

        // Count students who completed each assignment (has at least one submitted attempt)
        long totalCompletions = 0;
        foreach (var assignment in visibleAssignments)
        {
            var submittedStudentIds = submittedAttempts
                .Where(a => a.ClassroomAssignmentId == assignment.Id)
                .Select(a => a.UserId)
                .Distinct();
            totalCompletions += submittedStudentIds.Count(id => studentIds.Contains(id));
        }

        var averageScore = submittedAttempts.Count > 0
            ? Math.Round(submittedAttempts.Average(a => a.PercentScore), 2, MidpointRounding.AwayFromZero)
            : 0m;

        var completionRate = totalPossibleCompletions > 0
            ? Math.Round((decimal)totalCompletions / totalPossibleCompletions * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        return new ClassroomAnalyticsOverview
        {
            ActiveStudentCount = activeStudentCount,
            AssignmentCount = allAssignments.Count,
            PublishedAssignmentCount = publishedCount,
            ClosedAssignmentCount = closedCount,
            SubmittedAttemptCount = submittedAttempts.Count,
            AverageScore = averageScore,
            CompletionRate = completionRate
        };
    }

    private static List<AssignmentAnalyticsSummary> BuildAssignmentSummaries(
        List<Core.Entities.ClassroomAssignment> visibleAssignments,
        List<int> studentIds,
        List<Core.Entities.ClassroomAssignmentAttempt> attempts)
    {
        var summaries = new List<AssignmentAnalyticsSummary>();

        foreach (var assignment in visibleAssignments)
        {
            var assignmentAttempts = attempts.Where(a => a.ClassroomAssignmentId == assignment.Id).ToList();
            var attemptsByStudent = assignmentAttempts.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.ToList());

            int submittedStudents = 0;
            int inProgressStudents = 0;
            var bestScores = new List<decimal>();

            foreach (var studentId in studentIds)
            {
                var studentAttempts = attemptsByStudent.TryGetValue(studentId, out var atts) ? atts : new List<Core.Entities.ClassroomAssignmentAttempt>();
                var submittedStudentAttempts = studentAttempts.Where(a => a.Status == ClassroomAttemptStatus.Submitted).ToList();

                if (submittedStudentAttempts.Count > 0)
                {
                    submittedStudents++;
                    var best = submittedStudentAttempts.Max(a => a.PercentScore);
                    bestScores.Add(best);
                }
                else if (studentAttempts.Any(a => a.Status == ClassroomAttemptStatus.InProgress))
                {
                    inProgressStudents++;
                }
            }

            int notStarted = studentIds.Count - submittedStudents - inProgressStudents;
            decimal completionRate = studentIds.Count > 0
                ? Math.Round((decimal)submittedStudents / studentIds.Count * 100m, 2, MidpointRounding.AwayFromZero)
                : 0m;

            decimal avgScore = bestScores.Count > 0
                ? Math.Round(bestScores.Average(), 2, MidpointRounding.AwayFromZero)
                : 0m;
            decimal bestScore = bestScores.Count > 0 ? bestScores.Max() : 0m;
            decimal lowestScore = bestScores.Count > 0 ? bestScores.Min() : 0m;

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

            summaries.Add(new AssignmentAnalyticsSummary
            {
                AssignmentId = assignment.Id,
                Title = assignment.Title,
                Status = assignment.Status.ToString(),
                ScoringMode = assignment.ScoringMode.ToString(),
                ScoreFinality = scoreFinality,
                TotalStudents = studentIds.Count,
                SubmittedStudents = submittedStudents,
                InProgressStudents = inProgressStudents,
                NotStartedStudents = notStarted,
                CompletionRate = completionRate,
                AveragePercentScore = avgScore,
                BestPercentScore = bestScore,
                LowestPercentScore = lowestScore
            });
        }

        return summaries;
    }

    private async Task<QuestionDifficultyInsights> BuildQuestionInsightsAsync(
        List<int> visibleAssignmentIds,
        List<Core.Entities.ClassroomAssignment> allAssignments,
        CancellationToken cancellationToken)
    {
        if (visibleAssignmentIds.Count == 0)
        {
            return new QuestionDifficultyInsights();
        }

        var stats = await _dbContext.ClassroomAssignmentQuestionStats
            .Include(s => s.Question)
            .Include(s => s.Assignment)
            .Where(s => visibleAssignmentIds.Contains(s.ClassroomAssignmentId) && s.AnsweredCount > 0)
            .ToListAsync(cancellationToken);

        var assignmentTitleMap = allAssignments.ToDictionary(a => a.Id, a => a.Title);

        var statDtos = stats.Select(s => new QuestionDifficultyStat
        {
            QuestionId = s.QuestionId,
            AssignmentId = s.ClassroomAssignmentId,
            AssignmentTitle = assignmentTitleMap.TryGetValue(s.ClassroomAssignmentId, out var title) ? title : string.Empty,
            QuestionText = s.Question?.QuestionText,
            CorrectCount = s.CorrectCount,
            AnsweredCount = s.AnsweredCount,
            SmoothedCorrectRate = s.SmoothedCorrectRate,
            DifficultyWeight = s.DifficultyWeight,
            QualityFlag = s.QualityFlag
        }).ToList();

        // Hardest = highest difficultyWeight (hardest questions have the highest weight)
        var hardest = statDtos
            .OrderByDescending(s => s.DifficultyWeight)
            .ThenBy(s => s.SmoothedCorrectRate)
            .Take(5)
            .ToList();

        // Easiest = lowest difficultyWeight
        var easiest = statDtos
            .OrderBy(s => s.DifficultyWeight)
            .ThenByDescending(s => s.SmoothedCorrectRate)
            .Take(5)
            .ToList();

        // Suspicious = has a qualityFlag
        var suspicious = statDtos
            .Where(s => !string.IsNullOrWhiteSpace(s.QualityFlag))
            .OrderByDescending(s => s.DifficultyWeight)
            .ToList();

        return new QuestionDifficultyInsights
        {
            HardestQuestions = hardest,
            EasiestQuestions = easiest,
            SuspiciousQuestions = suspicious
        };
    }

    private static List<AtRiskStudent> BuildAtRiskStudents(
        List<Core.Entities.AppUser> students,
        List<Core.Entities.ClassroomAssignment> visibleAssignments,
        List<Core.Entities.ClassroomAssignmentAttempt> attempts)
    {
        if (students.Count == 0 || visibleAssignments.Count == 0)
        {
            return new List<AtRiskStudent>();
        }

        var attemptsByStudentAndAssignment = attempts
            .GroupBy(a => (a.UserId, a.ClassroomAssignmentId))
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<(Core.Entities.AppUser Student, int Completed, decimal AvgScore, DateTime? LastSubmittedAt)>();

        foreach (var student in students)
        {
            int completed = 0;
            decimal totalScore = 0m;
            DateTime? lastSubmittedAt = null;

            foreach (var assignment in visibleAssignments)
            {
                var key = (student.Id, assignment.Id);
                if (!attemptsByStudentAndAssignment.TryGetValue(key, out var studentAttempts))
                {
                    continue;
                }

                var submitted = studentAttempts.Where(a => a.Status == ClassroomAttemptStatus.Submitted).ToList();
                if (submitted.Count == 0) continue;

                var best = submitted
                    .OrderByDescending(a => a.PercentScore)
                    .ThenByDescending(a => a.RawScore)
                    .First();

                completed++;
                totalScore += best.PercentScore;

                if (best.SubmittedAt.HasValue && (lastSubmittedAt == null || best.SubmittedAt.Value > lastSubmittedAt.Value))
                {
                    lastSubmittedAt = best.SubmittedAt.Value;
                }
            }

            decimal avgScore = completed > 0
                ? Math.Round(totalScore / completed, 2, MidpointRounding.AwayFromZero)
                : 0m;

            rows.Add((student, completed, avgScore, lastSubmittedAt));
        }

        // At-risk = low average score OR low completed count; take top 5 most at-risk
        return rows
            .OrderBy(r => r.AvgScore)
            .ThenBy(r => r.Completed)
            .Take(5)
            .Select(r => new AtRiskStudent
            {
                UserId = r.Student.Id,
                DisplayName = r.Student.FullName,
                Email = r.Student.Email,
                CompletedAssignments = r.Completed,
                AveragePercentScore = r.AvgScore,
                LastSubmittedAt = r.LastSubmittedAt
            })
            .ToList();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Student analytics
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<StudentClassroomAnalyticsResponse> GetStudentAnalyticsAsync(
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

        var isStudent = await _permissionService.IsStudentAsync(classroomId, actorUserId, cancellationToken);
        if (!isStudent)
        {
            throw new UnauthorizedAccessException("Only active student members of this classroom can view personal analytics.");
        }

        var user = await _dbContext.AppUsers.FirstOrDefaultAsync(u => u.Id == actorUserId, cancellationToken);
        if (user == null)
        {
            throw new KeyNotFoundException("User not found.");
        }

        // Visible assignments (Published + Closed)
        var assignments = await _dbContext.ClassroomAssignments
            .Where(a => a.ClassroomWorkspaceId == classroomId
                        && (a.Status == ClassroomAssignmentStatus.Published || a.Status == ClassroomAssignmentStatus.Closed))
            .ToListAsync(cancellationToken);

        var assignmentIds = assignments.Select(a => a.Id).ToList();
        var assignmentMap = assignments.ToDictionary(a => a.Id);

        // Student's own attempts only
        var myAttempts = await _dbContext.ClassroomAssignmentAttempts
            .Where(a => assignmentIds.Contains(a.ClassroomAssignmentId) && a.UserId == actorUserId)
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .ToListAsync(cancellationToken);

        // Build summary
        var submittedAttempts = myAttempts.Where(a => a.Status == ClassroomAttemptStatus.Submitted).ToList();

        // Best attempt per assignment
        var attemptsByAssignment = submittedAttempts
            .GroupBy(a => a.ClassroomAssignmentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.PercentScore).First());

        int completedCount = attemptsByAssignment.Count;
        decimal avgScore = completedCount > 0
            ? Math.Round(attemptsByAssignment.Values.Average(a => a.PercentScore), 2, MidpointRounding.AwayFromZero)
            : 0m;
        decimal bestScore = completedCount > 0
            ? attemptsByAssignment.Values.Max(a => a.PercentScore)
            : 0m;
        DateTime? latestSubmittedAt = submittedAttempts.Count > 0
            ? submittedAttempts.Where(a => a.SubmittedAt.HasValue).Max(a => a.SubmittedAt)
            : null;

        var summary = new StudentAnalyticsSummary
        {
            CompletedAssignments = completedCount,
            TotalAssignments = assignments.Count,
            AveragePercentScore = avgScore,
            BestPercentScore = bestScore,
            LatestSubmittedAt = latestSubmittedAt
        };

        // Recent attempts (last 20, submitted or in-progress, NO answer details)
        var recentAttempts = myAttempts
            .Take(20)
            .Select(a =>
            {
                string scoreFinality;
                if (assignmentMap.TryGetValue(a.ClassroomAssignmentId, out var assignment))
                {
                    if (assignment.Status == ClassroomAssignmentStatus.Closed)
                        scoreFinality = "Final";
                    else if (assignment.ScoringMode == ClassroomScoringMode.EmpiricalDifficulty)
                        scoreFinality = "Temporary";
                    else
                        scoreFinality = "Final";
                }
                else
                {
                    scoreFinality = "Final";
                }

                return new StudentAttemptSummary
                {
                    AttemptId = a.Id,
                    AssignmentId = a.ClassroomAssignmentId,
                    AssignmentTitle = assignmentMap.TryGetValue(a.ClassroomAssignmentId, out var asgn) ? asgn.Title : string.Empty,
                    AttemptNumber = a.AttemptNumber,
                    Status = a.Status.ToString(),
                    RawScore = a.RawScore,
                    PercentScore = a.PercentScore,
                    SubmittedAt = a.SubmittedAt,
                    ScoreFinality = scoreFinality
                };
            })
            .ToList();

        // Hint flags
        bool needsPractice = avgScore < 50m;
        bool hasPending = completedCount < assignments.Count;

        return new StudentClassroomAnalyticsResponse
        {
            ClassroomId = classroom.Id,
            ClassroomName = classroom.Name,
            UserId = actorUserId,
            DisplayName = user.FullName,
            GeneratedAt = DateTime.UtcNow,
            Summary = summary,
            RecentAttempts = recentAttempts,
            NeedsPractice = needsPractice,
            HasPendingAssignments = hasPending
        };
    }
}
