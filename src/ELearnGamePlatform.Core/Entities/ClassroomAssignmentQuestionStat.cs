using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("classroom_assignment_question_stats")]
public class ClassroomAssignmentQuestionStat
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("classroom_assignment_id")]
    public int ClassroomAssignmentId { get; set; }

    [Column("question_id")]
    public int QuestionId { get; set; }

    [Column("answered_count")]
    public int AnsweredCount { get; set; }

    [Column("correct_count")]
    public int CorrectCount { get; set; }

    [Column("smoothed_correct_rate", TypeName = "numeric(8,6)")]
    public decimal SmoothedCorrectRate { get; set; }

    [Column("difficulty_weight", TypeName = "numeric(10,4)")]
    public decimal DifficultyWeight { get; set; }

    [Column("discrimination_index", TypeName = "numeric(8,4)")]
    public decimal? DiscriminationIndex { get; set; }

    [MaxLength(80)]
    [Column("quality_flag")]
    public string? QualityFlag { get; set; }

    [Column("calculated_at")]
    public DateTime CalculatedAt { get; set; }

    [ForeignKey(nameof(ClassroomAssignmentId))]
    public virtual ClassroomAssignment? Assignment { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public virtual Question? Question { get; set; }
}
