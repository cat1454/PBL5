using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ELearnGamePlatform.Core.Enums;

namespace ELearnGamePlatform.Core.Entities;

[Table("app_users")]
public class AppUser
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    [Column("full_name")]
    public required string FullName { get; set; }

    [Required]
    [MaxLength(320)]
    [Column("email")]
    public required string Email { get; set; }

    [Required]
    [MaxLength(1000)]
    [Column("password_hash")]
    public required string PasswordHash { get; set; }

    [Column("role")]
    public UserRole Role { get; set; } = UserRole.Learner;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
