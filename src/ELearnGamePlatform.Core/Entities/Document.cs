using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("documents")]
public class Document
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(500)]
    [Column("file_name")]
    public required string FileName { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("file_type")]
    public required string FileType { get; set; } // PDF, DOCX, PNG, JPG

    [Required]
    [MaxLength(1000)]
    [Column("file_path")]
    public required string FilePath { get; set; }

    [Column("file_size")]
    public long FileSize { get; set; }

    [Column("extracted_text")]
    public string? ExtractedText { get; set; }

    [Column("main_topics")]
    public string? MainTopicsJson { get; set; } // JSON serialized

    [Column("key_points")]
    public string? KeyPointsJson { get; set; } // JSON serialized

    [Column("coverage_map")]
    public string? CoverageMapJson { get; set; } // JSON serialized

    [Column("summary")]
    public string? Summary { get; set; }

    [Column("language")]
    [MaxLength(50)]
    public string? Language { get; set; }

    [Column("status")]
    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;

    [Required]
    [MaxLength(100)]
    [Column("uploaded_by")]
    public required string UploadedBy { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<Question> Questions { get; set; } = new List<Question>();
    public virtual ICollection<GameSession> GameSessions { get; set; } = new List<GameSession>();
    public virtual ICollection<SlideDeck> SlideDecks { get; set; } = new List<SlideDeck>();
}

public enum DocumentStatus
{
    Uploaded,
    Extracting,
    Analyzing,
    Completed,
    Failed
}
