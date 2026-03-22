using ELearnGamePlatform.Core.Entities;
using System.Text.Json;

namespace ELearnGamePlatform.Infrastructure.Extensions;

public static class EntityExtensions
{
    // Document extensions
    public static List<string> GetMainTopics(this Document document)
    {
        if (string.IsNullOrEmpty(document.MainTopicsJson))
            return new List<string>();
        
        return JsonSerializer.Deserialize<List<string>>(document.MainTopicsJson) ?? new List<string>();
    }

    public static void SetMainTopics(this Document document, List<string> topics)
    {
        document.MainTopicsJson = JsonSerializer.Serialize(topics);
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
