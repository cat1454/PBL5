using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Interfaces;

public interface ISlideGenerator
{
    Task<SlideOutlineResult> GenerateOutlineAsync(
        string content,
        ProcessedContent? processedContent,
        SlideDeckBrief? brief,
        int desiredSlideCount,
        IProgress<SlideGenerationProgressUpdate>? progress = null);

    Task<SlideContentResult> GenerateSlideAsync(
        string content,
        ProcessedContent? processedContent,
        SlideDeckBrief? brief,
        SlideOutlineSlide outlineSlide,
        int slideNumber,
        int totalSlides,
        IProgress<SlideGenerationProgressUpdate>? progress = null);

    string RenderDeckHtml(SlideDeck deck, IReadOnlyList<SlideItem> items);
}

public class SlideDeckBrief
{
    public string ThemeKey { get; set; } = "editorial-sunrise";
    public string Audience { get; set; } = "Sinh vien va nguoi hoc";
    public string Tone { get; set; } = "Rõ ràng, hiện đại, dễ nhớ";
    public string NarrativeGoal { get; set; } = "Giup nguoi doc hieu nhanh va ghi nho cac y chinh";
    public string LanguageStyle { get; set; } = "Tieng Viet don gian, chuyen nghiep";

    public SlideDeckBrief()
    {
        Tone = "Ro rang, hien dai, de nho";
    }
}

public class SlideOutlineResult
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string ThemeKey { get; set; } = "editorial-sunrise";
    public SlideDeckBrief? Brief { get; set; }
    public List<SlideOutlineSlide> Slides { get; set; } = new();
}

public class SlideOutlineSlide
{
    public int SlideIndex { get; set; }
    public SlideItemType SlideType { get; set; } = SlideItemType.Content;
    public string Heading { get; set; } = string.Empty;
    public string? Subheading { get; set; }
    public string Goal { get; set; } = string.Empty;
    public List<string> PreferredChunkIds { get; set; } = new();
}

public class SlideContentResult
{
    public string? Heading { get; set; }
    public string? Subheading { get; set; }
    public string? Goal { get; set; }
    public List<string> BodyBlocks { get; set; } = new();
    public string? SpeakerNotes { get; set; }
    public string? AccentTone { get; set; }
    public int? VerifierScore { get; set; }
    public List<string> VerifierIssues { get; set; } = new();
}
