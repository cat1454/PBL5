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
