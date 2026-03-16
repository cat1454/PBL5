using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("questions")]
public class Question
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [Column("document_id")]
    public int DocumentId { get; set; }

    [Required]
    [Column("question_text")]
    public required string QuestionText { get; set; }

    [Column("question_type")]
    public QuestionType QuestionType { get; set; }

    [Column("options")]
    public string? OptionsJson { get; set; } // JSON serialized List<QuestionOption>

    [Column("correct_answer")]
    [MaxLength(500)]
    public string? CorrectAnswer { get; set; }

    [Column("explanation")]
    public string? Explanation { get; set; }

    [Column("difficulty")]
    public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Medium;

    [Column("topic")]
    [MaxLength(200)]
    public string? Topic { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    [ForeignKey("DocumentId")]
    public virtual Document? Document { get; set; }
}

public class QuestionOption
{
    public required string Key { get; set; } // A, B, C, D
    public required string Text { get; set; }
    public bool IsCorrect { get; set; }
}

public enum QuestionType
{
    MultipleChoice,
    TrueFalse,
    ShortAnswer,
    FillInTheBlank
}

public enum DifficultyLevel
{
    Easy,
    Medium,
    Hard
}
