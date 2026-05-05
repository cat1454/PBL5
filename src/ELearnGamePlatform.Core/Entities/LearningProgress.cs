using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("learning_progresses")]
public class LearningProgress
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("user_id")]
    public required string UserId { get; set; }

    [Required]
    [Column("document_id")]
    public int DocumentId { get; set; }

    [Required]
    [Column("question_id")]
    public int QuestionId { get; set; }

    [Column("attempt_count")]
    public int AttemptCount { get; set; }

    [Column("correct_count")]
    public int CorrectCount { get; set; }

    [Column("wrong_count")]
    public int WrongCount { get; set; }

    [Column("current_streak")]
    public int CurrentStreak { get; set; }

    [Column("best_streak")]
    public int BestStreak { get; set; }

    [Column("last_reviewed_at")]
    public DateTime? LastReviewedAt { get; set; }

    [Column("memory_score")]
    public double MemoryScore { get; set; }

    [Column("mastery_score")]
    public double MasteryScore { get; set; }

    [Column("level")]
    public LearningLevel Level { get; set; } = LearningLevel.Weak;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("DocumentId")]
    public virtual Document? Document { get; set; }

    [ForeignKey("QuestionId")]
    public virtual Question? Question { get; set; }
}

public enum LearningLevel
{
    Weak = 1,
    Learning = 2,
    Good = 3,
    Mastered = 4
}
