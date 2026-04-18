using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("folder_projects")]
public class FolderProject
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(240)]
    [Column("name")]
    public required string Name { get; set; }

    [MaxLength(1200)]
    [Column("description")]
    public string? Description { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("uploaded_by")]
    public required string UploadedBy { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
    public virtual ICollection<SlideDeck> SlideDecks { get; set; } = new List<SlideDeck>();
}
