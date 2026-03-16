namespace ELearnGamePlatform.Core.Entities;

public class QuestionGenerationProgressUpdate
{
    public int Percent { get; set; }
    public string Stage { get; set; } = "running";
    public string? Message { get; set; }
    public int? Current { get; set; }
    public int? Total { get; set; }
    public string? TopicTag { get; set; }
}
