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
    public DbSet<Document> Documents { get; set; }
    public DbSet<FolderProject> FolderProjects { get; set; }
    public DbSet<Question> Questions { get; set; }
    public DbSet<GameSession> GameSessions { get; set; }
    public DbSet<LearningAttempt> LearningAttempts { get; set; }
    public DbSet<LearningProgress> LearningProgresses { get; set; }
    public DbSet<LearningTestResult> LearningTestResults { get; set; }
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

            entity.Property(e => e.ProcessedMetadataJson)
                .HasColumnName("processed_metadata")
                .HasColumnType("text");
        });

        // Question configuration
        modelBuilder.Entity<Question>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.IsArchived);
            entity.HasIndex(e => new { e.DocumentId, e.QuestionType });

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
