namespace ELearnGamePlatform.API.Contracts;

public sealed class QuestionGenerationMetricsDto
{
    public int DocumentId { get; init; }
    public int GeneratedQuestionCount { get; init; }
    public int ValidQuestionCount { get; init; }
    public int DocumentTopicCount { get; init; }
    public int CoveredTopicCount { get; init; }
    public double Coverage { get; init; }
    public double ValidRate { get; init; }
    public double AverageQualityScore { get; init; }
    public IReadOnlyList<string> MissingTopics { get; init; } = Array.Empty<string>();
}
