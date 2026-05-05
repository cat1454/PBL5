using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("learning_test_results")]
public class LearningTestResult
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

    [Column("total_questions")]
    public int TotalQuestions { get; set; }

    [Column("correct_count")]
    public int CorrectCount { get; set; }

    [Column("wrong_count")]
    public int WrongCount { get; set; }

    [Column("score")]
    public double Score { get; set; }

    [Column("started_at")]
    public DateTime StartedAt { get; set; }

    [Column("submitted_at")]
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    [Column("duration_ms")]
    public long DurationMs { get; set; }

    [Column("test_type")]
    public LearningTestType TestType { get; set; } = LearningTestType.PracticeTest;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("DocumentId")]
    public virtual Document? Document { get; set; }

    public virtual ICollection<LearningAttempt> Attempts { get; set; } = new List<LearningAttempt>();
}

public enum LearningTestType
{
    PreTest = 1,
    PostTest = 2,
    Retention = 3,
    PracticeTest = 4
}
