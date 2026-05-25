using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("analytics_events")]
public class AnalyticsEvent
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("user_id")]
    public required string UserId { get; set; }

    [Required]
    [MaxLength(120)]
    [Column("name")]
    public required string Name { get; set; }

    [Column("properties_json")]
    public string PropertiesJson { get; set; } = "{}";

    [MaxLength(120)]
    [Column("session_id")]
    public string? SessionId { get; set; }

    [Column("occurred_at")]
    public DateTime OccurredAt { get; set; }

    [Column("received_at")]
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
