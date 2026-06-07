using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("learning_attempts")]
public class LearningAttempt
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
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

    [Column("mode")]
    public LearningMode Mode { get; set; }

    [MaxLength(1000)]
    [Column("selected_answer")]
    public string? SelectedAnswer { get; set; }

    [Column("is_correct")]
    public bool IsCorrect { get; set; }

    [MaxLength(40)]
    [Column("confidence")]
    public string? Confidence { get; set; }

    [Column("response_time_ms")]
    public int? ResponseTimeMs { get; set; }

    [Column("test_result_id")]
    public int? TestResultId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("DocumentId")]
    public virtual Document? Document { get; set; }

    [ForeignKey("QuestionId")]
    public virtual Question? Question { get; set; }

    [ForeignKey("TestResultId")]
    public virtual LearningTestResult? TestResult { get; set; }
}

public enum LearningMode
{
    Flashcard = 1,
    Quiz = 2,
    Test = 3,
    Streak = 4
}
