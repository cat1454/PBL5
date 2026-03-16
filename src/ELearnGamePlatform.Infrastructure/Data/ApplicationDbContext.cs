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

    public DbSet<Document> Documents { get; set; }
    public DbSet<Question> Questions { get; set; }
    public DbSet<GameSession> GameSessions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Document configuration
        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UploadedBy);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Status);

            entity.Property(e => e.ExtractedText)
                .HasColumnType("text");

            entity.Property(e => e.MainTopicsJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.KeyPointsJson)
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
        });

        // Question configuration
        modelBuilder.Entity<Question>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => new { e.DocumentId, e.QuestionType });

            entity.Property(e => e.QuestionText)
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.OptionsJson)
                .HasColumnType("jsonb");

            entity.Property(e => e.Explanation)
                .HasColumnType("text");
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
