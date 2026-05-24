using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("question_generation_runs")]
public class QuestionGenerationRun
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("document_id")]
    public int DocumentId { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("user_id")]
    public string UserId { get; set; } = "demo-user";

    [Required]
    [MaxLength(40)]
    [Column("mode")]
    public string Mode { get; set; } = "balanced";

    [Required]
    [MaxLength(40)]
    [Column("status")]
    public string Status { get; set; } = "Pending";

    [Required]
    [MaxLength(80)]
    [Column("stage")]
    public string Stage { get; set; } = "Created";

    [Column("target_draft_count")]
    public int TargetDraftCount { get; set; }

    [Column("generated_draft_count")]
    public int GeneratedDraftCount { get; set; }

    [Column("verified_draft_count")]
    public int VerifiedDraftCount { get; set; }

    [Column("imported_count")]
    public int ImportedCount { get; set; }

    [Column("duplicate_count")]
    public int DuplicateCount { get; set; }

    [Column("rejected_count")]
    public int RejectedCount { get; set; }

    [Column("borderline_count")]
    public int BorderlineCount { get; set; }

    [Column("quarantined_count")]
    public int QuarantinedCount { get; set; }

    [Column("requested_question_types")]
    public string RequestedQuestionTypesJson { get; set; } = "[]";

    [Column("requested_difficulties")]
    public string RequestedDifficultiesJson { get; set; } = "[]";

    [Column("model_profile")]
    public string ModelProfileJson { get; set; } = "{}";

    [Column("failure_stats")]
    public string FailureStatsJson { get; set; } = "{}";

    [Column("metrics")]
    public string MetricsJson { get; set; } = "{}";

    [Column("error_message")]
    public string ErrorMessage { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("started_at")]
    public DateTime? StartedAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [ForeignKey(nameof(DocumentId))]
    public virtual Document? Document { get; set; }

    public virtual ICollection<QuestionSourceUnit> SourceUnits { get; set; } = new List<QuestionSourceUnit>();
    public virtual ICollection<QuestionDraft> Drafts { get; set; } = new List<QuestionDraft>();
}
