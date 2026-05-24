using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("question_review_events")]
public class QuestionReviewEvent
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("question_draft_id")]
    public int QuestionDraftId { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("user_id")]
    public string UserId { get; set; } = "demo-user";

    [Required]
    [MaxLength(40)]
    [Column("action")]
    public string Action { get; set; } = string.Empty;

    [Column("before")]
    public string BeforeJson { get; set; } = "{}";

    [Column("after")]
    public string AfterJson { get; set; } = "{}";

    [Column("note")]
    public string Note { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(QuestionDraftId))]
    public virtual QuestionDraft? QuestionDraft { get; set; }
}
