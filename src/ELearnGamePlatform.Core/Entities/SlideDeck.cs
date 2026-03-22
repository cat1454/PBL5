using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ELearnGamePlatform.Core.Entities;

[Table("slide_decks")]
public class SlideDeck
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [Column("document_id")]
    public int DocumentId { get; set; }

    [Column("status")]
    public SlideDeckStatus Status { get; set; } = SlideDeckStatus.Queued;

    [MaxLength(240)]
    [Column("title")]
    public string? Title { get; set; }

    [MaxLength(400)]
    [Column("subtitle")]
    public string? Subtitle { get; set; }

    [MaxLength(80)]
    [Column("theme_key")]
    public string? ThemeKey { get; set; }

    [Column("outline")]
    public string? OutlineJson { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [ForeignKey("DocumentId")]
    public virtual Document? Document { get; set; }

    public virtual ICollection<SlideItem> Items { get; set; } = new List<SlideItem>();
}

[Table("slide_items")]
public class SlideItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [Column("slide_deck_id")]
    public int SlideDeckId { get; set; }

    [Column("slide_index")]
    public int SlideIndex { get; set; }

    [Column("slide_type")]
    public SlideItemType SlideType { get; set; } = SlideItemType.Content;

    [Column("status")]
    public SlideItemStatus Status { get; set; } = SlideItemStatus.Pending;

    [MaxLength(240)]
    [Column("heading")]
    public string? Heading { get; set; }

    [MaxLength(400)]
    [Column("subheading")]
    public string? Subheading { get; set; }

    [MaxLength(400)]
    [Column("goal")]
    public string? Goal { get; set; }

    [Column("body")]
    public string? BodyJson { get; set; }

    [Column("speaker_notes")]
    public string? SpeakerNotes { get; set; }

    [MaxLength(80)]
    [Column("accent_tone")]
    public string? AccentTone { get; set; }

    [Column("verifier_score")]
    public int? VerifierScore { get; set; }

    [Column("verifier_issues")]
    public string? VerifierIssuesJson { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("SlideDeckId")]
    public virtual SlideDeck? SlideDeck { get; set; }
}

public enum SlideDeckStatus
{
    Queued,
    GeneratingOutline,
    GeneratingSlides,
    Completed,
    Failed
}

public enum SlideItemStatus
{
    Pending,
    Generating,
    Completed,
    Failed
}

public enum SlideItemType
{
    Title,
    SectionDivider,
    Content,
    Quote,
    Highlight,
    Stat
}
