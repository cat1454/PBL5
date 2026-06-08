using ELearnGamePlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ELearnGamePlatform.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppUser> AppUsers { get; set; }
    public DbSet<ClassroomWorkspace> ClassroomWorkspaces { get; set; }
    public DbSet<ClassroomMember> ClassroomMembers { get; set; }
    public DbSet<ClassroomJoinCode> ClassroomJoinCodes { get; set; }
    public DbSet<ClassroomQuestionSet> ClassroomQuestionSets { get; set; }
    public DbSet<ClassroomQuestionSetItem> ClassroomQuestionSetItems { get; set; }
    public DbSet<ClassroomAssignment> ClassroomAssignments { get; set; }
    public DbSet<ClassroomAssignmentAttempt> ClassroomAssignmentAttempts { get; set; }
    public DbSet<ClassroomAssignmentAnswer> ClassroomAssignmentAnswers { get; set; }
    public DbSet<ClassroomAssignmentQuestionStat> ClassroomAssignmentQuestionStats { get; set; }
    public DbSet<Document> Documents { get; set; }
    public DbSet<FolderProject> FolderProjects { get; set; }
    public DbSet<Question> Questions { get; set; }
    public DbSet<QuestionGenerationRun> QuestionGenerationRuns { get; set; }
    public DbSet<QuestionSourceUnit> QuestionSourceUnits { get; set; }
    public DbSet<QuestionDraft> QuestionDrafts { get; set; }
    public DbSet<QuestionReviewEvent> QuestionReviewEvents { get; set; }
    public DbSet<GameSession> GameSessions { get; set; }
    public DbSet<LearningAttempt> LearningAttempts { get; set; }
    public DbSet<LearningProgress> LearningProgresses { get; set; }
    public DbSet<LearningTestResult> LearningTestResults { get; set; }
    public DbSet<AnalyticsEvent> AnalyticsEvents { get; set; }
    public DbSet<DocumentUnderstandingRun> DocumentUnderstandingRuns { get; set; }
    public DbSet<SlideDeck> SlideDecks { get; set; }
    public DbSet<SlideItem> SlideItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.Role);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<ClassroomWorkspace>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OwnerUserId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.UpdatedAt);

            entity.Property(e => e.Name)
                .HasMaxLength(240)
                .IsRequired();

            entity.Property(e => e.Description)
                .HasMaxLength(1200);

            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.Members)
                .WithOne(member => member.ClassroomWorkspace)
                .HasForeignKey(member => member.ClassroomWorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.JoinCodes)
                .WithOne(code => code.ClassroomWorkspace)
                .HasForeignKey(code => code.ClassroomWorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.QuestionSets)
                .WithOne(questionSet => questionSet.ClassroomWorkspace)
                .HasForeignKey(questionSet => questionSet.ClassroomWorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Assignments)
                .WithOne(assignment => assignment.ClassroomWorkspace)
                .HasForeignKey(assignment => assignment.ClassroomWorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClassroomMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ClassroomWorkspaceId, e.UserId }).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Role);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClassroomJoinCode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => new { e.ClassroomWorkspaceId, e.IsActive });
            entity.HasIndex(e => e.CreatedByUserId);

            entity.Property(e => e.Code)
                .HasMaxLength(32)
                .IsRequired();

            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClassroomQuestionSet>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClassroomWorkspaceId);
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.CreatedByUserId);

            entity.Property(e => e.Title)
                .HasMaxLength(240)
                .IsRequired();

            entity.Property(e => e.Description)
                .HasMaxLength(1200);

            entity.HasOne(e => e.Document)
                .WithMany(document => document.ClassroomQuestionSets)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.Items)
                .WithOne(item => item.ClassroomQuestionSet)
                .HasForeignKey(item => item.ClassroomQuestionSetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Assignments)
                .WithOne(assignment => assignment.QuestionSet)
                .HasForeignKey(assignment => assignment.QuestionSetId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClassroomQuestionSetItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ClassroomQuestionSetId, e.QuestionId }).IsUnique();
            entity.HasIndex(e => e.QuestionId);

            entity.Property(e => e.SectionCode)
                .HasMaxLength(80);

            entity.HasOne(e => e.Question)
                .WithMany(question => question.ClassroomQuestionSetItems)
                .HasForeignKey(e => e.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClassroomAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClassroomWorkspaceId);
            entity.HasIndex(e => e.QuestionSetId);
            entity.HasIndex(e => e.CreatedByUserId);
            entity.HasIndex(e => e.Status);

            entity.Property(e => e.Title)
                .HasMaxLength(240)
                .IsRequired();

            entity.Property(e => e.Description)
                .HasMaxLength(1200);

            // Phase 4: numeric columns for scoring config
            entity.Property(e => e.MinQuestionWeight)
                .HasColumnType("numeric(10,4)");
            entity.Property(e => e.MaxQuestionWeight)
                .HasColumnType("numeric(10,4)");
            entity.Property(e => e.SmoothingAlpha)
                .HasColumnType("numeric(10,4)");
            entity.Property(e => e.SmoothingBeta)
                .HasColumnType("numeric(10,4)");

            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.Attempts)
                .WithOne(attempt => attempt.Assignment)
                .HasForeignKey(attempt => attempt.ClassroomAssignmentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.QuestionStats)
                .WithOne(stat => stat.Assignment)
                .HasForeignKey(stat => stat.ClassroomAssignmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClassroomAssignmentQuestionStat>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ClassroomAssignmentId, e.QuestionId }).IsUnique();
            entity.HasIndex(e => e.ClassroomAssignmentId);
            entity.HasIndex(e => e.QuestionId);

            entity.Property(e => e.SmoothedCorrectRate)
                .HasColumnType("numeric(8,6)");
            entity.Property(e => e.DifficultyWeight)
                .HasColumnType("numeric(10,4)");
            entity.Property(e => e.DiscriminationIndex)
                .HasColumnType("numeric(8,4)");
            entity.Property(e => e.QualityFlag)
                .HasMaxLength(80);

            entity.HasOne(e => e.Question)
                .WithMany()
                .HasForeignKey(e => e.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClassroomAssignmentAttempt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ClassroomAssignmentId, e.UserId });
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);

            entity.Property(e => e.RawScore)
                .HasColumnType("numeric(10,2)");

            entity.Property(e => e.PercentScore)
                .HasColumnType("numeric(5,2)");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.Answers)
                .WithOne(answer => answer.Attempt)
                .HasForeignKey(answer => answer.AttemptId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClassroomAssignmentAnswer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.AttemptId, e.QuestionId }).IsUnique();
            entity.HasIndex(e => e.QuestionId);

            entity.Property(e => e.SelectedAnswer)
                .HasMaxLength(500);

            entity.Property(e => e.PointEarned)
                .HasColumnType("numeric(10,2)");

            entity.HasOne(e => e.Question)
                .WithMany(question => question.ClassroomAssignmentAnswers)
                .HasForeignKey(e => e.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FolderProject>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UploadedBy);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.UpdatedAt);

            entity.HasMany(folder => folder.Documents)
                .WithOne(document => document.FolderProject)
                .HasForeignKey(document => document.FolderProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(folder => folder.SlideDecks)
                .WithOne(deck => deck.FolderProject)
                .HasForeignKey(deck => deck.FolderProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Document configuration
        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UploadedBy);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.FolderProjectId);
            entity.HasIndex(e => new { e.FolderProjectId, e.FolderSourceOrder });

            entity.Property(e => e.ExtractedText)
                .HasColumnType("text");

            entity.Property(e => e.MainTopicsJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.KeyPointsJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.CoverageMapJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.Summary)
                .HasColumnType("text");

            // Configure relationships
            entity.HasMany(d => d.Questions)
                .WithOne(q => q.Document)
                .HasForeignKey(q => q.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(d => d.QuestionGenerationRuns)
                .WithOne(run => run.Document)
                .HasForeignKey(run => run.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(d => d.QuestionSourceUnits)
                .WithOne(unit => unit.Document)
                .HasForeignKey(unit => unit.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(d => d.QuestionDrafts)
                .WithOne(draft => draft.Document)
                .HasForeignKey(draft => draft.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(d => d.GameSessions)
                .WithOne(g => g.Document)
                .HasForeignKey(g => g.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(d => d.LearningTestResults)
                .WithOne(result => result.Document)
                .HasForeignKey(result => result.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(d => d.SlideDecks)
                .WithOne(deck => deck.Document)
                .HasForeignKey(deck => deck.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(d => d.UnderstandingRuns)
                .WithOne(run => run.Document)
                .HasForeignKey(run => run.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.ProcessedMetadataJson)
                .HasColumnName("processed_metadata")
                .HasColumnType("text");
        });

        modelBuilder.Entity<DocumentUnderstandingRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.DocumentId, e.CreatedAt });

            entity.Property(e => e.Status)
                .HasMaxLength(80)
                .IsRequired();

            entity.Property(e => e.CombinedText)
                .HasColumnType("text");

            entity.Property(e => e.ResultJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.FailureReasonsJson)
                .HasColumnType("jsonb");
        });

        // Question configuration
        modelBuilder.Entity<Question>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.IsArchived);
            entity.HasIndex(e => new { e.DocumentId, e.QuestionType });
            entity.HasIndex(e => e.SourceDraftId)
                .IsUnique()
                .HasFilter("source_draft_id IS NOT NULL");

            entity.Property(e => e.QuestionText)
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.OptionsJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.Explanation)
                .HasColumnType("text");

            entity.Property(e => e.VerifierIssuesJson)
                .HasColumnType("jsonb");
        });

        modelBuilder.Entity<QuestionGenerationRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DocumentId, e.CreatedAt });
            entity.HasIndex(e => e.Status);

            entity.Property(e => e.RequestedQuestionTypesJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.RequestedDifficultiesJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.ModelProfileJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.FailureStatsJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.MetricsJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.ErrorMessage)
                .HasColumnType("text");
        });

        modelBuilder.Entity<QuestionSourceUnit>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DocumentId, e.TopicTag });
            entity.HasIndex(e => e.GenerationRunId);
            entity.HasIndex(e => e.SourceHash);

            entity.Property(e => e.Content)
                .HasColumnType("text");

            entity.Property(e => e.MetadataJson)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.GenerationRun)
                .WithMany(run => run.SourceUnits)
                .HasForeignKey(e => e.GenerationRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuestionDraft>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DocumentId, e.Status });
            entity.HasIndex(e => new { e.GenerationRunId, e.Status });
            entity.HasIndex(e => new { e.TopicTag, e.Difficulty });
            entity.HasIndex(e => e.ParentDraftId);
            entity.HasIndex(e => e.SourceUnitId);
            entity.HasIndex(e => e.StemHash);

            entity.Property(e => e.QuestionText)
                .HasColumnType("text");

            entity.Property(e => e.OptionsJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.CorrectAnswer)
                .HasColumnType("text");

            entity.Property(e => e.Explanation)
                .HasColumnType("text");

            entity.Property(e => e.FailureReason)
                .HasColumnType("text");

            entity.Property(e => e.SourceEvidence)
                .HasColumnType("text");

            entity.Property(e => e.MetadataJson)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.GenerationRun)
                .WithMany(run => run.Drafts)
                .HasForeignKey(e => e.GenerationRunId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceUnit)
                .WithMany(unit => unit.Drafts)
                .HasForeignKey(e => e.SourceUnitId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ParentDraft)
                .WithMany(parent => parent.Variants)
                .HasForeignKey(e => e.ParentDraftId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QuestionReviewEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.QuestionDraftId);
            entity.HasIndex(e => new { e.QuestionDraftId, e.CreatedAt });
            entity.HasIndex(e => e.Action);

            entity.Property(e => e.BeforeJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.AfterJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.Note)
                .HasColumnType("text");

            entity.HasOne(e => e.QuestionDraft)
                .WithMany(draft => draft.ReviewEvents)
                .HasForeignKey(e => e.QuestionDraftId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // GameSession configuration
        modelBuilder.Entity<GameSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });

            entity.Property(e => e.QuestionIdsJson)
                .HasColumnType("jsonb");
        });

        modelBuilder.Entity<LearningAttempt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.QuestionId);
            entity.HasIndex(e => e.TestResultId);
            entity.HasIndex(e => new { e.UserId, e.DocumentId, e.QuestionId });
            entity.HasIndex(e => new { e.UserId, e.DocumentId, e.CreatedAt });

            entity.Property(e => e.Confidence)
                .HasMaxLength(40);

            entity.HasOne(e => e.Document)
                .WithMany(d => d.LearningAttempts)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Question)
                .WithMany(q => q.LearningAttempts)
                .HasForeignKey(e => e.QuestionId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.TestResult)
                .WithMany(result => result.Attempts)
                .HasForeignKey(e => e.TestResultId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<LearningProgress>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.QuestionId);
            entity.HasIndex(e => new { e.UserId, e.DocumentId });
            entity.HasIndex(e => new { e.UserId, e.DocumentId, e.QuestionId }).IsUnique();

            entity.HasOne(e => e.Document)
                .WithMany(d => d.LearningProgresses)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Question)
                .WithMany(q => q.LearningProgresses)
                .HasForeignKey(e => e.QuestionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<LearningTestResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.SubmittedAt);
            entity.HasIndex(e => e.TestType);
            entity.HasIndex(e => e.TestSessionId).IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.UserId, e.DocumentId });
            entity.HasIndex(e => new { e.UserId, e.DocumentId, e.SubmittedAt });

            entity.Property(e => e.QuestionIdsJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.ResultSnapshotJson)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Document)
                .WithMany(d => d.LearningTestResults)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SlideDeck>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.FolderProjectId);
            entity.HasIndex(e => new { e.DocumentId, e.CreatedAt });
            entity.HasIndex(e => new { e.FolderProjectId, e.CreatedAt });
            entity.HasIndex(e => e.Status);

            entity.Property(e => e.OutlineJson)
                .HasColumnType("jsonb");

            entity.HasMany(deck => deck.Items)
                .WithOne(item => item.SlideDeck)
                .HasForeignKey(item => item.SlideDeckId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AnalyticsEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.ReceivedAt);
            entity.HasIndex(e => new { e.UserId, e.ReceivedAt });

            entity.Property(e => e.PropertiesJson)
                .HasColumnType("jsonb");
        });

        modelBuilder.Entity<SlideItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SlideDeckId);
            entity.HasIndex(e => new { e.SlideDeckId, e.SlideIndex });
            entity.HasIndex(e => e.Status);

            entity.Property(e => e.BodyJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.EvidenceFromText)
                .HasColumnType("text");

            entity.Property(e => e.EvidenceDebugJson)
                .HasColumnName("evidence_debug")
                .HasColumnType("text");

            entity.Property(e => e.SpeakerNotes)
                .HasColumnType("text");

            entity.Property(e => e.VerifierIssuesJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.ImagePlanJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.ImageCandidatesJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.EditorStateJson)
                .HasColumnType("jsonb");
        });
    }

    // Helper methods for JSON serialization
    public static string SerializeToJson<T>(T obj)
    {
        return JsonSerializer.Serialize(obj);
    }

    public static T? DeserializeFromJson<T>(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return default;
        return JsonSerializer.Deserialize<T>(json);
    }
}
