using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ELearnGamePlatform.API.Contracts;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;

namespace ELearnGamePlatform.API.Services;

public sealed class QuestionMetricsService : IQuestionMetricsService
{
    private static readonly Regex SeparatorRegex = new(@"[>/|;:,]+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private readonly IQuestionRepository _questionRepository;

    public QuestionMetricsService(IQuestionRepository questionRepository)
    {
        _questionRepository = questionRepository;
    }

    public async Task<QuestionGenerationMetricsDto> GetMetricsAsync(Document document, CancellationToken cancellationToken = default)
    {
        var questions = (await _questionRepository.GetByDocumentIdAsync(document.Id)).ToList();
        var documentTopics = BuildDocumentTopics(document);
        var questionTopics = questions
            .SelectMany(question => BuildTopicCandidates(question.Topic))
            .Where(topic => !string.IsNullOrWhiteSpace(topic.Normalized))
            .ToList();

        var coveredTopics = documentTopics
            .Where(topic => IsCoveredByAnyQuestionTopic(topic.Normalized, questionTopics))
            .ToList();

        var validQuestionCount = questions.Count(IsValidQuestion);
        var averageQualityScore = questions.Count == 0
            ? 0d
            : Math.Round(questions.Average(question => CalculateQualityScore(question, documentTopics)), 1);

        return new QuestionGenerationMetricsDto
        {
            DocumentId = document.Id,
            GeneratedQuestionCount = questions.Count,
            ValidQuestionCount = validQuestionCount,
            DocumentTopicCount = documentTopics.Count,
            CoveredTopicCount = coveredTopics.Count,
            Coverage = documentTopics.Count == 0
                ? 0d
                : Math.Round(coveredTopics.Count * 100d / documentTopics.Count, 1),
            ValidRate = questions.Count == 0
                ? 0d
                : Math.Round(validQuestionCount * 100d / questions.Count, 1),
            AverageQualityScore = averageQualityScore,
            MissingTopics = documentTopics
                .Where(topic => !coveredTopics.Any(covered => covered.Normalized == topic.Normalized))
                .Select(topic => topic.Display)
                .ToList()
        };
    }

    private static double CalculateQualityScore(Question question, IReadOnlyList<TopicCandidate> documentTopics)
    {
        var relevance = CalculateRelevanceScore(question, documentTopics);
        var answer = CalculateAnswerScore(question);
        var clarity = CalculateClarityScore(question);
        var explanation = CalculateExplanationScore(question);

        return Math.Round((0.35d * relevance) + (0.35d * answer) + (0.15d * clarity) + (0.15d * explanation), 1);
    }

    private static double CalculateRelevanceScore(Question question, IReadOnlyList<TopicCandidate> documentTopics)
    {
        var topicCandidates = BuildTopicCandidates(question.Topic);
        if (documentTopics.Count > 0 && topicCandidates.Any(candidate => IsCoveredByAnyQuestionTopic(candidate.Normalized, documentTopics)))
        {
            return 100d;
        }

        if (!string.IsNullOrWhiteSpace(question.Topic))
        {
            return 70d;
        }

        return string.IsNullOrWhiteSpace(question.QuestionText) ? 0d : 40d;
    }

    private static double CalculateAnswerScore(Question question)
    {
        return question.QuestionType switch
        {
            QuestionType.MultipleChoice => IsValidMultipleChoiceQuestion(question) ? 100d : 0d,
            QuestionType.TrueFalse => IsValidTrueFalseQuestion(question) ? 100d : 0d,
            QuestionType.ShortAnswer or QuestionType.FillInTheBlank => string.IsNullOrWhiteSpace(question.CorrectAnswer) ? 0d : 100d,
            _ => string.IsNullOrWhiteSpace(question.CorrectAnswer) ? 0d : 100d
        };
    }

    private static double CalculateClarityScore(Question question)
    {
        if (string.IsNullOrWhiteSpace(question.QuestionText))
        {
            return 0d;
        }

        var score = 100d;
        var text = question.QuestionText.Trim();
        if (text.Length < 12)
        {
            score -= 30d;
        }
        else if (text.Length > 450)
        {
            score -= 15d;
        }

        var issueCount = question.GetVerifierIssues().Count;
        score -= Math.Min(40d, issueCount * 10d);

        if (question.VerifierScore.HasValue && question.VerifierScore.Value < 70)
        {
            score -= 15d;
        }

        return Math.Clamp(score, 0d, 100d);
    }

    private static double CalculateExplanationScore(Question question)
    {
        if (string.IsNullOrWhiteSpace(question.Explanation))
        {
            return 0d;
        }

        return question.Explanation.Trim().Length >= 20 ? 100d : 60d;
    }

    private static bool IsValidQuestion(Question question)
    {
        if (string.IsNullOrWhiteSpace(question.QuestionText))
        {
            return false;
        }

        return question.QuestionType switch
        {
            QuestionType.MultipleChoice => IsValidMultipleChoiceQuestion(question),
            QuestionType.TrueFalse => IsValidTrueFalseQuestion(question),
            QuestionType.ShortAnswer or QuestionType.FillInTheBlank => !string.IsNullOrWhiteSpace(question.CorrectAnswer),
            _ => !string.IsNullOrWhiteSpace(question.CorrectAnswer)
        };
    }

    private static bool IsValidMultipleChoiceQuestion(Question question)
    {
        var options = question.GetOptions();
        if (options.Count < 2 || string.IsNullOrWhiteSpace(question.CorrectAnswer))
        {
            return false;
        }

        var correctAnswer = question.CorrectAnswer.Trim();
        var matchingOptions = options.Count(option =>
            string.Equals(option.Key?.Trim(), correctAnswer, StringComparison.OrdinalIgnoreCase));

        return matchingOptions == 1 && options.All(option => !string.IsNullOrWhiteSpace(option.Text));
    }

    private static bool IsValidTrueFalseQuestion(Question question)
    {
        if (string.IsNullOrWhiteSpace(question.CorrectAnswer))
        {
            return false;
        }

        var normalized = NormalizeTopic(question.CorrectAnswer);
        return normalized is "a" or "b" or "true" or "false" or "dung" or "sai";
    }

    private static List<TopicCandidate> BuildDocumentTopics(Document document)
    {
        var topics = new List<string>();
        topics.AddRange(document.GetMainTopics());
        topics.AddRange(document.GetCoverageMap().Select(SelectCoverageTopic));

        return topics
            .SelectMany(BuildTopicCandidates)
            .Where(topic => topic.Normalized.Length >= 3)
            .GroupBy(topic => topic.Normalized)
            .Select(group => group.First())
            .OrderBy(topic => topic.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string SelectCoverageTopic(DocumentCoverageChunk chunk)
    {
        if (!string.IsNullOrWhiteSpace(chunk.HeadingText))
        {
            return chunk.HeadingText;
        }

        if (!string.IsNullOrWhiteSpace(chunk.NormalizedHeading))
        {
            return chunk.NormalizedHeading;
        }

        if (!string.IsNullOrWhiteSpace(chunk.HeadingPath))
        {
            return chunk.HeadingPath;
        }

        return chunk.Label;
    }

    private static List<TopicCandidate> BuildTopicCandidates(string? rawTopic)
    {
        if (string.IsNullOrWhiteSpace(rawTopic))
        {
            return new List<TopicCandidate>();
        }

        var candidates = SeparatorRegex.Split(rawTopic)
            .Append(rawTopic)
            .Select(candidate => candidate.Trim())
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => new TopicCandidate(candidate, NormalizeTopic(candidate)))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Normalized))
            .GroupBy(candidate => candidate.Normalized)
            .Select(group => group.First())
            .ToList();

        return candidates;
    }

    private static bool IsCoveredByAnyQuestionTopic(string normalizedTopic, IReadOnlyList<TopicCandidate> candidates)
    {
        return candidates.Any(candidate => TopicsMatch(normalizedTopic, candidate.Normalized));
    }

    private static bool TopicsMatch(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        if (left.Length < 5 || right.Length < 5)
        {
            return false;
        }

        return left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal);
    }

    private static string NormalizeTopic(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
            }
        }

        return WhitespaceRegex.Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ").Trim();
    }

    private sealed record TopicCandidate(string Display, string Normalized);
}
