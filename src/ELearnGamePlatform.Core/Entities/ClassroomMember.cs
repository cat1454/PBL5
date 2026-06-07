using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ELearnGamePlatform.Core.Enums;

namespace ELearnGamePlatform.Core.Entities;

[Table("classroom_members")]
public class ClassroomMember
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("classroom_workspace_id")]
    public int ClassroomWorkspaceId { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("role")]
    public ClassroomRole Role { get; set; } = ClassroomRole.Student;

    [Column("status")]
    public ClassroomMemberStatus Status { get; set; } = ClassroomMemberStatus.Active;

    [Column("joined_at")]
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ClassroomWorkspaceId))]
    public virtual ClassroomWorkspace? ClassroomWorkspace { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual AppUser? User { get; set; }
}
