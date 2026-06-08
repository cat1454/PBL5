using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("classroom_assignment_answers")]
public class ClassroomAssignmentAnswer
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("attempt_id")]
    public int AttemptId { get; set; }

    [Column("question_id")]
    public int QuestionId { get; set; }

    [MaxLength(500)]
    [Column("selected_answer")]
    public string? SelectedAnswer { get; set; }

    [Column("is_correct")]
    public bool IsCorrect { get; set; }

    [Column("point_earned", TypeName = "numeric(10,2)")]
    public decimal PointEarned { get; set; }

    [Column("time_spent_seconds")]
    public int? TimeSpentSeconds { get; set; }

    [Column("answered_at")]
    public DateTime? AnsweredAt { get; set; }

    [ForeignKey(nameof(AttemptId))]
    public virtual ClassroomAssignmentAttempt? Attempt { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public virtual Question? Question { get; set; }
}
