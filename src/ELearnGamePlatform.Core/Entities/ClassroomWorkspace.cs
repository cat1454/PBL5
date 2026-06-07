using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("classroom_workspaces")]
public class ClassroomWorkspace
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [MaxLength(240)]
    [Column("name")]
    public required string Name { get; set; }

    [MaxLength(1200)]
    [Column("description")]
    public string? Description { get; set; }

    [Column("owner_user_id")]
    public int OwnerUserId { get; set; }

    [Column("is_archived")]
    public bool IsArchived { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(OwnerUserId))]
    public virtual AppUser? Owner { get; set; }

    public virtual ICollection<ClassroomMember> Members { get; set; } = new List<ClassroomMember>();
    public virtual ICollection<ClassroomJoinCode> JoinCodes { get; set; } = new List<ClassroomJoinCode>();
    public virtual ICollection<ClassroomQuestionSet> QuestionSets { get; set; } = new List<ClassroomQuestionSet>();
}
