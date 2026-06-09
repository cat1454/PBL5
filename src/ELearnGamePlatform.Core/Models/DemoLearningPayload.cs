using System.Text.Json;
using System.Text.Json.Serialization;

namespace ELearnGamePlatform.Core.Models;

public class DemoLearningPayload
{
    [JsonPropertyName("document_analysis")]
    public DemoDocumentAnalysis DocumentAnalysis { get; set; } = null!;

    [JsonPropertyName("questions")]
    public List<DemoQuestion> Questions { get; set; } = null!;

    [JsonPropertyName("slide_deck")]
    public DemoSlideDeck SlideDeck { get; set; } = null!;
}

public class DemoDocumentAnalysis
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("document_type")]
    public string DocumentType { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("main_topics")]
    public List<string> MainTopics { get; set; } = new();

    [JsonPropertyName("key_points")]
    public List<string> KeyPoints { get; set; } = new();
}

public class DemoQuestion
{
    [JsonPropertyName("question_text")]
    public string QuestionText { get; set; } = string.Empty;

    [JsonPropertyName("question_type")]
    public string QuestionType { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    public List<DemoQuestionOption>? Options { get; set; }

    [JsonPropertyName("correct_answer")]
    public string CorrectAnswer { get; set; } = string.Empty;

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;

    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = string.Empty;

    [JsonPropertyName("topic")]
    public string Topic { get; set; } = string.Empty;
}

public class DemoQuestionOption
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("is_correct")]
    public bool IsCorrect { get; set; }
}

public class DemoSlideDeck
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("subtitle")]
    public string Subtitle { get; set; } = string.Empty;

    [JsonPropertyName("theme_key")]
    public string ThemeKey { get; set; } = string.Empty;

    [JsonPropertyName("slides")]
    public List<DemoSlide> Slides { get; set; } = new();
}

public class DemoSlide
{
    [JsonPropertyName("slide_index")]
    public int SlideIndex { get; set; }

    [JsonPropertyName("slide_type")]
    public string SlideType { get; set; } = string.Empty;

    [JsonPropertyName("heading")]
    public string Heading { get; set; } = string.Empty;

    [JsonPropertyName("subheading")]
    public string Subheading { get; set; } = string.Empty;

    [JsonPropertyName("goal")]
    public string Goal { get; set; } = string.Empty;

    [JsonPropertyName("key_message")]
    public string KeyMessage { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public List<string> Body { get; set; } = new();

    [JsonPropertyName("evidence_from_text")]
    public string EvidenceFromText { get; set; } = string.Empty;

    [JsonPropertyName("speaker_notes")]
    public string SpeakerNotes { get; set; } = string.Empty;

    [JsonPropertyName("accent_tone")]
    public string AccentTone { get; set; } = string.Empty;

    [JsonPropertyName("image_plan")]
    public JsonElement? ImagePlan { get; set; }
}
