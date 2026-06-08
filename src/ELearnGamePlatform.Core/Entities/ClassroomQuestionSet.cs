using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ELearnGamePlatform.Core.Enums;

namespace ELearnGamePlatform.Core.Entities;

[Table("classroom_question_sets")]
public class ClassroomQuestionSet
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("classroom_workspace_id")]
    public int ClassroomWorkspaceId { get; set; }

    [Column("document_id")]
    public int? DocumentId { get; set; }

    [Required]
    [MaxLength(240)]
    [Column("title")]
    public required string Title { get; set; }

    [MaxLength(1200)]
    [Column("description")]
    public string? Description { get; set; }

    [Column("created_by_user_id")]
    public int CreatedByUserId { get; set; }

    [Column("visibility")]
    public ClassroomQuestionSetVisibility Visibility { get; set; } = ClassroomQuestionSetVisibility.Draft;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ClassroomWorkspaceId))]
    public virtual ClassroomWorkspace? ClassroomWorkspace { get; set; }

    [ForeignKey(nameof(DocumentId))]
    public virtual Document? Document { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public virtual AppUser? CreatedByUser { get; set; }

    public virtual ICollection<ClassroomQuestionSetItem> Items { get; set; } = new List<ClassroomQuestionSetItem>();
    public virtual ICollection<ClassroomAssignment> Assignments { get; set; } = new List<ClassroomAssignment>();
}
