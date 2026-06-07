using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("document_understanding_runs")]
public class DocumentUnderstandingRun
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("document_id")]
    public int DocumentId { get; set; }

    [Required]
    [MaxLength(80)]
    [Column("status")]
    public required string Status { get; set; }

    [Column("document_confidence")]
    public double? DocumentConfidence { get; set; }

    [Column("needs_review")]
    public bool NeedsReview { get; set; }

    [Column("combined_text")]
    public string? CombinedText { get; set; }

    [Column("result")]
    public string? ResultJson { get; set; }

    [Column("failure_reasons")]
    public string? FailureReasonsJson { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("DocumentId")]
    public virtual Document Document { get; set; } = null!;
}
