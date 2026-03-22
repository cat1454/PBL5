namespace ELearnGamePlatform.Core.Entities;

public class SlideGenerationProgressUpdate
{
    public int Percent { get; set; }
    public string Stage { get; set; } = "running";
    public string? StageLabel { get; set; }
    public string? Message { get; set; }
    public string? Detail { get; set; }
    public int? Current { get; set; }
    public int? Total { get; set; }
    public string? UnitLabel { get; set; }
    public int? StageIndex { get; set; }
    public int? StageCount { get; set; }
    public int? SlideDeckId { get; set; }
}
