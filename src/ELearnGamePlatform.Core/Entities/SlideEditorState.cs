namespace ELearnGamePlatform.Core.Entities;

public class SlideEditorState
{
    public string Version { get; set; } = "2";
    public long Revision { get; set; }
    public string LayoutVariant { get; set; } = "standard";
    public SlideCanvasState Canvas { get; set; } = new();
    public List<SlideElementState> Elements { get; set; } = new();
    public SlideTextBlockState Title { get; set; } = new();
    public SlideTextBlockState Subtitle { get; set; } = new();
    public SlideTextBlockState Goal { get; set; } = new();
    public SlideTextBlockState Body { get; set; } = new();
    public SlideTextBlockState Notes { get; set; } = new();
}

public class SlideCanvasState
{
    public int Width { get; set; } = 1600;
    public int Height { get; set; } = 900;
    public string Background { get; set; } = "theme";
}

public class SlideElementState
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
    public string Role { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int ZIndex { get; set; }
    public bool Locked { get; set; }
    public string Text { get; set; } = string.Empty;
    public int FontSize { get; set; } = 24;
    public bool Bold { get; set; }
    public string Color { get; set; } = "#FFFFFF";
    public string Align { get; set; } = "left";
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
