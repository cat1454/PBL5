using System.Text.Json;
using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Extensions;

/// <summary>
/// Extension methods for entities to handle JSON serialization/deserialization
/// of complex properties stored as JSONB in PostgreSQL
/// </summary>
public static class EntityExtensions
{
    // Document extensions
    public static List<string> GetMainTopics(this Document document)
    {
        if (string.IsNullOrEmpty(document.MainTopicsJson))
            return new List<string>();
        
        return JsonSerializer.Deserialize<List<string>>(document.MainTopicsJson) ?? new List<string>();
    }

    public static void SetMainTopics(this Document document, List<string> mainTopics)
    {
        document.MainTopicsJson = JsonSerializer.Serialize(mainTopics);
    }

    public static List<string> GetKeyPoints(this Document document)
    {
        if (string.IsNullOrEmpty(document.KeyPointsJson))
            return new List<string>();
        
        return JsonSerializer.Deserialize<List<string>>(document.KeyPointsJson) ?? new List<string>();
    }

    public static void SetKeyPoints(this Document document, List<string> keyPoints)
    {
        document.KeyPointsJson = JsonSerializer.Serialize(keyPoints);
    }

    public static List<DocumentCoverageChunk> GetCoverageMap(this Document document)
    {
        if (string.IsNullOrEmpty(document.CoverageMapJson))
            return new List<DocumentCoverageChunk>();

        return JsonSerializer.Deserialize<List<DocumentCoverageChunk>>(document.CoverageMapJson) ?? new List<DocumentCoverageChunk>();
    }

    public static void SetCoverageMap(this Document document, List<DocumentCoverageChunk> coverageMap)
    {
        document.CoverageMapJson = JsonSerializer.Serialize(coverageMap);
    }

    // Question extensions
    public static List<QuestionOption> GetOptions(this Question question)
    {
        if (string.IsNullOrEmpty(question.OptionsJson))
            return new List<QuestionOption>();
        
        return JsonSerializer.Deserialize<List<QuestionOption>>(question.OptionsJson) ?? new List<QuestionOption>();
    }

    public static void SetOptions(this Question question, List<QuestionOption> options)
    {
        question.OptionsJson = JsonSerializer.Serialize(options);
    }

    public static List<string> GetVerifierIssues(this Question question)
    {
        if (string.IsNullOrEmpty(question.VerifierIssuesJson))
            return new List<string>();

        return JsonSerializer.Deserialize<List<string>>(question.VerifierIssuesJson) ?? new List<string>();
    }

    public static void SetVerifierIssues(this Question question, List<string> issues)
    {
        question.VerifierIssuesJson = JsonSerializer.Serialize(issues);
    }

    // Slide extensions
    public static List<string> GetBodyBlocks(this SlideItem item)
    {
        if (string.IsNullOrEmpty(item.BodyJson))
            return new List<string>();

        return JsonSerializer.Deserialize<List<string>>(item.BodyJson) ?? new List<string>();
    }

    public static void SetBodyBlocks(this SlideItem item, List<string> bodyBlocks)
    {
        item.BodyJson = JsonSerializer.Serialize(bodyBlocks);
    }

    public static List<string> GetVerifierIssues(this SlideItem item)
    {
        if (string.IsNullOrEmpty(item.VerifierIssuesJson))
            return new List<string>();

        return JsonSerializer.Deserialize<List<string>>(item.VerifierIssuesJson) ?? new List<string>();
    }

    public static void SetVerifierIssues(this SlideItem item, List<string> issues)
    {
        item.VerifierIssuesJson = JsonSerializer.Serialize(issues);
    }

    // GameSession extensions
    public static List<int> GetQuestionIds(this GameSession session)
    {
        if (string.IsNullOrEmpty(session.QuestionIdsJson))
            return new List<int>();
        
        return JsonSerializer.Deserialize<List<int>>(session.QuestionIdsJson) ?? new List<int>();
    }

    public static void SetQuestionIds(this GameSession session, List<int> questionIds)
    {
        session.QuestionIdsJson = JsonSerializer.Serialize(questionIds);
    }
}
