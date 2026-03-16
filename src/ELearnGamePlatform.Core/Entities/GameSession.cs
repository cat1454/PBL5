using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("game_sessions")]
public class GameSession
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [Column("document_id")]
    public int DocumentId { get; set; }

    [Column("game_type")]
    public GameType GameType { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("user_id")]
    public required string UserId { get; set; }

    [Column("question_ids")]
    public string? QuestionIdsJson { get; set; } // JSON serialized List<int>

    [Column("score")]
    public int Score { get; set; }

    [Column("total_questions")]
    public int TotalQuestions { get; set; }

    [Column("correct_answers")]
    public int CorrectAnswers { get; set; }

    [Column("status")]
    public GameStatus Status { get; set; } = GameStatus.NotStarted;

    [Column("started_at")]
    public DateTime? StartedAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    [ForeignKey("DocumentId")]
    public virtual Document? Document { get; set; }
}

public enum GameType
{
    Quiz,
    Flashcard,
    Test
}

public enum GameStatus
{
    NotStarted,
    InProgress,
    Completed
}
