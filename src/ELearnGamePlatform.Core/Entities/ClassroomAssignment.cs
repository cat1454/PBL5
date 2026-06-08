using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ELearnGamePlatform.Core.Enums;

namespace ELearnGamePlatform.Core.Entities;

[Table("classroom_assignments")]
public class ClassroomAssignment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("classroom_workspace_id")]
    public int ClassroomWorkspaceId { get; set; }

    [Column("question_set_id")]
    public int QuestionSetId { get; set; }

    [Required]
    [MaxLength(240)]
    [Column("title")]
    public required string Title { get; set; }

    [MaxLength(1200)]
    [Column("description")]
    public string? Description { get; set; }

    [Column("type")]
    public ClassroomAssignmentType Type { get; set; } = ClassroomAssignmentType.Quiz;

    [Column("status")]
    public ClassroomAssignmentStatus Status { get; set; } = ClassroomAssignmentStatus.Draft;

    [Column("start_at")]
    public DateTime? StartAt { get; set; }

    [Column("due_at")]
    public DateTime? DueAt { get; set; }

    [Column("time_limit_minutes")]
    public int? TimeLimitMinutes { get; set; }

    [Column("attempt_limit")]
    public int AttemptLimit { get; set; } = 1;

    [Column("shuffle_questions")]
    public bool ShuffleQuestions { get; set; }

    [Column("shuffle_options")]
    public bool ShuffleOptions { get; set; }

    [Column("show_answer_after_submit")]
    public bool ShowAnswerAfterSubmit { get; set; }

    [Column("created_by_user_id")]
    public int CreatedByUserId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ClassroomWorkspaceId))]
    public virtual ClassroomWorkspace? ClassroomWorkspace { get; set; }

    [ForeignKey(nameof(QuestionSetId))]
    public virtual ClassroomQuestionSet? QuestionSet { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public virtual AppUser? CreatedByUser { get; set; }

    public virtual ICollection<ClassroomAssignmentAttempt> Attempts { get; set; } = new List<ClassroomAssignmentAttempt>();
}
