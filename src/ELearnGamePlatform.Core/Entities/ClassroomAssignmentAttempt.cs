using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ELearnGamePlatform.Core.Enums;

namespace ELearnGamePlatform.Core.Entities;

[Table("classroom_assignment_attempts")]
public class ClassroomAssignmentAttempt
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("classroom_assignment_id")]
    public int ClassroomAssignmentId { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("started_at")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    [Column("submitted_at")]
    public DateTime? SubmittedAt { get; set; }

    [Column("status")]
    public ClassroomAttemptStatus Status { get; set; } = ClassroomAttemptStatus.InProgress;

    [Column("raw_score", TypeName = "numeric(10,2)")]
    public decimal RawScore { get; set; }

    [Column("percent_score", TypeName = "numeric(5,2)")]
    public decimal PercentScore { get; set; }

    [Column("duration_seconds")]
    public int? DurationSeconds { get; set; }

    [Column("attempt_number")]
    public int AttemptNumber { get; set; } = 1;

    [ForeignKey(nameof(ClassroomAssignmentId))]
    public virtual ClassroomAssignment? Assignment { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual AppUser? User { get; set; }

    public virtual ICollection<ClassroomAssignmentAnswer> Answers { get; set; } = new List<ClassroomAssignmentAnswer>();
}
