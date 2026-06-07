using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("classroom_question_set_items")]
public class ClassroomQuestionSetItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("classroom_question_set_id")]
    public int ClassroomQuestionSetId { get; set; }

    [Column("question_id")]
    public int QuestionId { get; set; }

    [Column("order_index")]
    public int OrderIndex { get; set; }

    [Column("point_weight")]
    public double PointWeight { get; set; } = 1;

    [MaxLength(80)]
    [Column("section_code")]
    public string? SectionCode { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ClassroomQuestionSetId))]
    public virtual ClassroomQuestionSet? ClassroomQuestionSet { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public virtual Question? Question { get; set; }
}
