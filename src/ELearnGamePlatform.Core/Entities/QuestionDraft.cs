using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("question_drafts")]
public class QuestionDraft
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("document_id")]
    public int DocumentId { get; set; }

    [Required]
    [Column("generation_run_id")]
    public int GenerationRunId { get; set; }

    [Column("source_unit_id")]
    public int? SourceUnitId { get; set; }

    [Required]
    [MaxLength(40)]
    [Column("status")]
    public string Status { get; set; } = "Draft";

    [Required]
    [MaxLength(40)]
    [Column("draft_kind")]
    public string DraftKind { get; set; } = "Canonical";

    [Column("parent_draft_id")]
    public int? ParentDraftId { get; set; }

    [Column("question_text")]
    public string QuestionText { get; set; } = string.Empty;

    [Required]
    [MaxLength(40)]
    [Column("question_type")]
    public string QuestionType { get; set; } = "MultipleChoice";

    [Column("options")]
    public string OptionsJson { get; set; } = "[]";

    [Column("correct_answer")]
    public string CorrectAnswer { get; set; } = string.Empty;

    [Column("explanation")]
    public string Explanation { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    [Column("difficulty")]
    public string Difficulty { get; set; } = "Medium";

    [Required]
    [MaxLength(40)]
    [Column("learning_objective")]
    public string LearningObjective { get; set; } = "Understand";

    [MaxLength(200)]
    [Column("topic_tag")]
    public string TopicTag { get; set; } = string.Empty;

    [Column("grounding_score")]
    public double GroundingScore { get; set; }

    [Column("answer_score")]
    public double AnswerScore { get; set; }

    [Column("clarity_score")]
    public double ClarityScore { get; set; }

    [Column("duplicate_score")]
    public double DuplicateScore { get; set; } = 1.0;

    [Column("overall_score")]
    public double OverallScore { get; set; }

    [Column("repair_count")]
    public int RepairCount { get; set; }

    [Column("failure_reason")]
    public string FailureReason { get; set; } = string.Empty;

    [Column("source_evidence")]
    public string SourceEvidence { get; set; } = string.Empty;

    [MaxLength(128)]
    [Column("stem_hash")]
    public string StemHash { get; set; } = string.Empty;

    [Column("metadata")]
    public string MetadataJson { get; set; } = "{}";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("verified_at")]
    public DateTime? VerifiedAt { get; set; }

    [Column("imported_at")]
    public DateTime? ImportedAt { get; set; }

    [ForeignKey(nameof(DocumentId))]
    public virtual Document? Document { get; set; }

    [ForeignKey(nameof(GenerationRunId))]
    public virtual QuestionGenerationRun? GenerationRun { get; set; }

    [ForeignKey(nameof(SourceUnitId))]
    public virtual QuestionSourceUnit? SourceUnit { get; set; }

    [ForeignKey(nameof(ParentDraftId))]
    public virtual QuestionDraft? ParentDraft { get; set; }

    public virtual ICollection<QuestionDraft> Variants { get; set; } = new List<QuestionDraft>();
    public virtual ICollection<QuestionReviewEvent> ReviewEvents { get; set; } = new List<QuestionReviewEvent>();
}
