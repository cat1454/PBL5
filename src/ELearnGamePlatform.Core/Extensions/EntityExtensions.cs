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

    public static SlideEditorState BuildDefaultEditorState(this SlideItem item)
    {
        return new SlideEditorState
        {
            LayoutVariant = item.SlideType switch
            {
                SlideItemType.Title => "cover",
                SlideItemType.SectionDivider => "divider",
                SlideItemType.Highlight => "highlight",
                SlideItemType.Stat => "stat",
                _ => "standard"
            },
            Title = new SlideTextBlockState
            {
                Text = item.Heading ?? string.Empty,
                FontFamily = item.SlideType == SlideItemType.Title ? "Georgia" : "Trebuchet MS",
                FontSize = item.SlideType == SlideItemType.Title ? 34 : 24,
                Bold = true,
                Align = "left"
            },
            Subtitle = new SlideTextBlockState
            {
                Text = item.Subheading ?? string.Empty,
                FontFamily = "Segoe UI",
                FontSize = 16,
                Align = "left"
            },
            Goal = new SlideTextBlockState
            {
                Text = item.Goal ?? string.Empty,
                FontFamily = "Segoe UI",
                FontSize = 14,
                Bold = true,
                Align = "left"
            },
            Body = new SlideTextBlockState
            {
                Text = string.Join('\n', item.GetBodyBlocks()),
                FontFamily = "Segoe UI",
                FontSize = 18,
                Align = "left",
                Bullet = item.SlideType != SlideItemType.Quote
            },
            Notes = new SlideTextBlockState
            {
                Text = item.SpeakerNotes ?? string.Empty,
                FontFamily = "Segoe UI",
                FontSize = 14,
                Align = "left"
            }
        };
    }

    public static SlideEditorState GetEditorState(this SlideItem item)
    {
        if (string.IsNullOrEmpty(item.EditorStateJson))
            return item.BuildDefaultEditorState();

        try
        {
            return NormalizeEditorState(JsonSerializer.Deserialize<SlideEditorState>(item.EditorStateJson), item);
        }
        catch
        {
            return item.BuildDefaultEditorState();
        }
    }

    public static void SetEditorState(this SlideItem item, SlideEditorState? editorState)
    {
        item.EditorStateJson = JsonSerializer.Serialize(NormalizeEditorState(editorState, item));
    }

    public static void ApplyEditorState(this SlideItem item, SlideEditorState? editorState)
    {
        var normalized = NormalizeEditorState(editorState, item);
        item.Heading = normalized.Title.Text;
        item.Subheading = normalized.Subtitle.Text;
        item.Goal = normalized.Goal.Text;
        item.SpeakerNotes = normalized.Notes.Text;
        item.SetBodyBlocks(normalized.Body.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(block => block.Trim())
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .ToList());
        item.SetEditorState(normalized);
    }

    private static SlideEditorState NormalizeEditorState(SlideEditorState? editorState, SlideItem item)
    {
        var fallback = item.BuildDefaultEditorState();
        var source = editorState ?? fallback;

        return new SlideEditorState
        {
            LayoutVariant = string.IsNullOrWhiteSpace(source.LayoutVariant) ? fallback.LayoutVariant : source.LayoutVariant.Trim(),
            Title = NormalizeTextBlock(source.Title, fallback.Title),
            Subtitle = NormalizeTextBlock(source.Subtitle, fallback.Subtitle),
            Goal = NormalizeTextBlock(source.Goal, fallback.Goal),
            Body = NormalizeTextBlock(source.Body, fallback.Body),
            Notes = NormalizeTextBlock(source.Notes, fallback.Notes)
        };
    }

    private static SlideTextBlockState NormalizeTextBlock(SlideTextBlockState? block, SlideTextBlockState fallback)
    {
        var source = block ?? fallback;

        return new SlideTextBlockState
        {
            Text = source.Text ?? fallback.Text ?? string.Empty,
            FontFamily = string.IsNullOrWhiteSpace(source.FontFamily) ? fallback.FontFamily : source.FontFamily.Trim(),
            FontSize = source.FontSize <= 0 ? fallback.FontSize : source.FontSize,
            Bold = source.Bold,
            Italic = source.Italic,
            Underline = source.Underline,
            Align = string.IsNullOrWhiteSpace(source.Align) ? fallback.Align : source.Align.Trim().ToLowerInvariant(),
            Bullet = source.Bullet
        };
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

    public static SlideImagePlan? GetImagePlan(this SlideItem item)
    {
        if (string.IsNullOrEmpty(item.ImagePlanJson))
            return null;

        return JsonSerializer.Deserialize<SlideImagePlan>(item.ImagePlanJson);
    }

    public static void SetImagePlan(this SlideItem item, SlideImagePlan? plan)
    {
        item.ImagePlanJson = plan == null ? null : JsonSerializer.Serialize(plan);
    }

    public static List<SlideImageCandidate> GetImageCandidates(this SlideItem item)
    {
        if (string.IsNullOrEmpty(item.ImageCandidatesJson))
            return new List<SlideImageCandidate>();

        return JsonSerializer.Deserialize<List<SlideImageCandidate>>(item.ImageCandidatesJson) ?? new List<SlideImageCandidate>();
    }

    public static void SetImageCandidates(this SlideItem item, List<SlideImageCandidate> candidates)
    {
        item.ImageCandidatesJson = JsonSerializer.Serialize(candidates ?? new List<SlideImageCandidate>());
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
