using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("question_source_units")]
public class QuestionSourceUnit
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("document_id")]
    public int DocumentId { get; set; }

    [Column("generation_run_id")]
    public int? GenerationRunId { get; set; }

    [Required]
    [MaxLength(40)]
    [Column("unit_type")]
    public string UnitType { get; set; } = "Concept";

    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [MaxLength(200)]
    [Column("topic_tag")]
    public string TopicTag { get; set; } = string.Empty;

    [MaxLength(128)]
    [Column("source_hash")]
    public string SourceHash { get; set; } = string.Empty;

    [Column("start_offset")]
    public int StartOffset { get; set; }

    [Column("end_offset")]
    public int EndOffset { get; set; }

    [Column("confidence")]
    public double Confidence { get; set; } = 1.0;

    [Column("metadata")]
    public string MetadataJson { get; set; } = "{}";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(DocumentId))]
    public virtual Document? Document { get; set; }

    [ForeignKey(nameof(GenerationRunId))]
    public virtual QuestionGenerationRun? GenerationRun { get; set; }

    public virtual ICollection<QuestionDraft> Drafts { get; set; } = new List<QuestionDraft>();
}
