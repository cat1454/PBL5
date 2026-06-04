namespace ELearnGamePlatform.Core.Entities;

public class SlideEditorState
{
    public string LayoutVariant { get; set; } = "standard";
    public SlideTextBlockState Title { get; set; } = new();
    public SlideTextBlockState Subtitle { get; set; } = new();
    public SlideTextBlockState Goal { get; set; } = new();
    public SlideTextBlockState Body { get; set; } = new();
    public SlideTextBlockState Notes { get; set; } = new();
}

public class SlideTextBlockState
{
    public string Text { get; set; } = string.Empty;
    public string FontFamily { get; set; } = "Georgia";
    public int FontSize { get; set; } = 18;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string Align { get; set; } = "left";
    public bool Bullet { get; set; }
}
