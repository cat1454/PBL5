using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("classroom_join_codes")]
public class ClassroomJoinCode
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("classroom_workspace_id")]
    public int ClassroomWorkspaceId { get; set; }

    [Required]
    [MaxLength(32)]
    [Column("code")]
    public required string Code { get; set; }

    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [Column("max_uses")]
    public int? MaxUses { get; set; }

    [Column("used_count")]
    public int UsedCount { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_by_user_id")]
    public int CreatedByUserId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ClassroomWorkspaceId))]
    public virtual ClassroomWorkspace? ClassroomWorkspace { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public virtual AppUser? CreatedByUser { get; set; }
}
