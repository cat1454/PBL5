using System.Text.Json;
using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.Core.Extensions;

/// <summary>
/// Extension methods for entities to handle JSON serialization/deserialization
/// of complex properties stored as JSONB in PostgreSQL
/// </summary>
public static class EntityExtensions
{
    private const double SlideEditorDesignScaleX = 0.8;
    private const double SlideEditorDesignScaleY = 0.8;
    private const string SlideEditorFontFamily = "Lexend";

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

    public static DocumentProcessingMetadata GetProcessingMetadata(this Document document)
    {
        if (string.IsNullOrEmpty(document.ProcessedMetadataJson))
        {
            return new DocumentProcessingMetadata
            {
                Language = document.Language
            };
        }

        return JsonSerializer.Deserialize<DocumentProcessingMetadata>(document.ProcessedMetadataJson) ?? new DocumentProcessingMetadata
        {
            Language = document.Language
        };
    }

    public static void SetProcessingMetadata(this Document document, DocumentProcessingMetadata metadata)
    {
        if (metadata == null)
        {
            document.ProcessedMetadataJson = null;
            return;
        }

        document.ProcessedMetadataJson = JsonSerializer.Serialize(metadata);
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
        var layoutVariant = item.SlideType switch
        {
            SlideItemType.Title => "cover",
            SlideItemType.SectionDivider => "divider",
            SlideItemType.Highlight => "highlight",
            SlideItemType.Stat => "stat",
            _ => "standard"
        };

        var state = new SlideEditorState
        {
            Version = "2",
            Revision = 0,
            LayoutVariant = layoutVariant,
            Canvas = new SlideCanvasState(),
            Title = new SlideTextBlockState
            {
                Text = item.Heading ?? string.Empty,
                FontFamily = SlideEditorFontFamily,
                FontSize = item.SlideType == SlideItemType.Title ? 34 : 24,
                Bold = true,
                Align = "left"
            },
            Subtitle = new SlideTextBlockState
            {
                Text = item.Subheading ?? string.Empty,
                FontFamily = SlideEditorFontFamily,
                FontSize = 16,
                Align = "left"
            },
            Goal = new SlideTextBlockState
            {
                Text = item.KeyMessage ?? item.Goal ?? string.Empty,
                FontFamily = SlideEditorFontFamily,
                FontSize = 14,
                Bold = true,
                Align = "left"
            },
            Body = new SlideTextBlockState
            {
                Text = string.Join('\n', item.GetBodyBlocks()),
                FontFamily = SlideEditorFontFamily,
                FontSize = 18,
                Align = "left",
                Bullet = item.SlideType != SlideItemType.Quote
            },
            Notes = new SlideTextBlockState
            {
                Text = item.SpeakerNotes ?? string.Empty,
                FontFamily = SlideEditorFontFamily,
                FontSize = 14,
                Align = "left"
            }
        };

        state.Elements = BuildDefaultElements(item, state);
        return state;
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

        if (normalized.Elements.Any())
        {
            item.Heading = GetElementText(normalized, "title", normalized.Title.Text);
            item.Subheading = GetElementText(normalized, "subtitle", normalized.Subtitle.Text);
            item.Goal = GetElementText(normalized, "goal", normalized.Goal.Text);
            item.KeyMessage = item.Goal;
            item.SpeakerNotes = GetElementText(normalized, "notes", normalized.Notes.Text);
            item.SetBodyBlocks(SplitBodyBlocks(GetElementText(normalized, "body", normalized.Body.Text)));
        }
        else
        {
            item.Heading = normalized.Title.Text;
            item.Subheading = normalized.Subtitle.Text;
            item.Goal = normalized.Goal.Text;
            item.KeyMessage = normalized.Goal.Text;
            item.SpeakerNotes = normalized.Notes.Text;
            item.SetBodyBlocks(SplitBodyBlocks(normalized.Body.Text));
        }

        item.SetEditorState(normalized);
    }

    private static List<string> SplitBodyBlocks(string? bodyText)
    {
        return (bodyText ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(block => block.Trim())
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .ToList();
    }

    private static SlideEditorState NormalizeEditorState(SlideEditorState? editorState, SlideItem item)
    {
        var fallback = item.BuildDefaultEditorState();
        var source = editorState ?? fallback;
        var canvas = NormalizeCanvas(source.Canvas, fallback.Canvas);
        var elements = (source.Elements?.Any() == true ? source.Elements : fallback.Elements)
            .Select((element, index) => NormalizeElement(element, fallback.Elements.ElementAtOrDefault(index), canvas, index))
            .Where(element => !string.IsNullOrWhiteSpace(element.Id))
            .OrderBy(element => element.ZIndex)
            .ToList();

        var title = NormalizeTextBlock(source.Title, fallback.Title);
        var subtitle = NormalizeTextBlock(source.Subtitle, fallback.Subtitle);
        var goal = NormalizeTextBlock(source.Goal, fallback.Goal);
        var body = NormalizeTextBlock(source.Body, fallback.Body);
        var notes = NormalizeTextBlock(source.Notes, fallback.Notes);

        if (elements.Any())
        {
            title.Text = GetElementText(elements, "title", title.Text);
            subtitle.Text = GetElementText(elements, "subtitle", subtitle.Text);
            goal.Text = GetElementText(elements, "goal", goal.Text);
            body.Text = GetElementText(elements, "body", body.Text);
            notes.Text = GetElementText(elements, "notes", notes.Text);
        }

        return new SlideEditorState
        {
            Version = string.IsNullOrWhiteSpace(source.Version) ? fallback.Version : source.Version.Trim(),
            Revision = Math.Max(0, source.Revision),
            LayoutVariant = string.IsNullOrWhiteSpace(source.LayoutVariant) ? fallback.LayoutVariant : source.LayoutVariant.Trim(),
            Canvas = canvas,
            Elements = elements,
            Title = title,
            Subtitle = subtitle,
            Goal = goal,
            Body = body,
            Notes = notes
        };
    }

    private static SlideCanvasState NormalizeCanvas(SlideCanvasState? canvas, SlideCanvasState fallback)
    {
        var source = canvas ?? fallback;
        var width = source.Width <= 0 ? fallback.Width : source.Width;
        var height = source.Height <= 0 ? fallback.Height : source.Height;

        return new SlideCanvasState
        {
            Width = Math.Clamp(width, 320, 4000),
            Height = Math.Clamp(height, 180, 3000),
            Background = string.IsNullOrWhiteSpace(source.Background) ? fallback.Background : source.Background.Trim()
        };
    }

    private static SlideElementState NormalizeElement(
        SlideElementState? element,
        SlideElementState? fallback,
        SlideCanvasState canvas,
        int index)
    {
        var source = element ?? fallback ?? new SlideElementState();
        var role = NormalizeToken(source.Role, fallback?.Role ?? "element");
        var type = NormalizeToken(source.Type, fallback?.Type ?? "text");
        var sourceWidth = source.Width > 0 ? source.Width : source.W ?? 0;
        var sourceHeight = source.Height > 0 ? source.Height : source.H ?? 0;
        var width = ClampDimension(sourceWidth, fallback?.Width ?? fallback?.W ?? 320, canvas.Width);
        var height = ClampDimension(sourceHeight, fallback?.Height ?? fallback?.H ?? 120, canvas.Height);
        var x = Math.Clamp(source.X, 0, Math.Max(0, canvas.Width - width));
        var y = Math.Clamp(source.Y, 0, Math.Max(0, canvas.Height - height));
        var align = string.IsNullOrWhiteSpace(source.Align) ? source.TextAlign : source.Align;
        var fallbackAlign = string.IsNullOrWhiteSpace(fallback?.Align) ? fallback?.TextAlign ?? "left" : fallback!.Align;
        var src = FirstNonBlank(source.Src, source.Url, source.Base64, fallback?.Src, fallback?.Url, fallback?.Base64);

        return new SlideElementState
        {
            Id = string.IsNullOrWhiteSpace(source.Id) ? $"{role}-{index + 1}" : source.Id.Trim(),
            Type = type,
            Role = role,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            ZIndex = source.ZIndex <= 0 ? fallback?.ZIndex ?? ((index + 1) * 10) : source.ZIndex,
            Locked = source.Locked,
            Visible = source.Visible,
            Src = src,
            Text = source.Text ?? fallback?.Text ?? string.Empty,
            FontSize = Math.Clamp(source.FontSize <= 0 ? fallback?.FontSize ?? 24 : source.FontSize, 8, 160),
            Bold = source.Bold,
            Color = string.IsNullOrWhiteSpace(source.Color) ? fallback?.Color ?? "#FFFFFF" : source.Color.Trim(),
            Align = NormalizeAlign(align, fallbackAlign),
            TextAlign = NormalizeAlign(align, fallbackAlign),
            FillColor = FirstNonBlank(source.FillColor, fallback?.FillColor),
            BorderColor = FirstNonBlank(source.BorderColor, fallback?.BorderColor),
            BorderWidth = source.BorderWidth ?? fallback?.BorderWidth,
            Opacity = source.Opacity ?? fallback?.Opacity,
            Rotation = source.Rotation ?? fallback?.Rotation,
            EffectPreset = NormalizeEffectPreset(FirstNonBlank(source.EffectPreset, fallback?.EffectPreset)),
            ImportedAssetName = FirstNonBlank(source.ImportedAssetName, fallback?.ImportedAssetName)
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

    private static string NormalizeToken(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().ToLowerInvariant();
    }

    private static string NormalizeAlign(string? value, string fallback)
    {
        var align = NormalizeToken(value, fallback);
        return align is "left" or "center" or "right" ? align : fallback;
    }

    private static string NormalizeEffectPreset(string? value)
    {
        var preset = NormalizeToken(value, "none");
        return preset is "soft-shadow" or "neon-glow" or "glass-frame" or "paper-cut" or "duotone"
            ? preset
            : "none";
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static double ClampDimension(double value, double fallback, int max)
    {
        var dimension = value <= 0 ? fallback : value;
        return Math.Clamp(dimension, 24, max);
    }

    private static string GetElementText(SlideEditorState state, string role, string fallback)
        => GetElementText(state.Elements, role, fallback);

    private static string GetElementText(IEnumerable<SlideElementState> elements, string role, string fallback)
    {
        var text = elements
            .FirstOrDefault(element => string.Equals(element.Role, role, StringComparison.OrdinalIgnoreCase))
            ?.Text;

        return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
    }

    private static List<SlideElementState> BuildDefaultElements(SlideItem item, SlideEditorState state)
    {
        return item.SlideType switch
        {
            SlideItemType.Title => BuildCoverElements(item, state),
            SlideItemType.SectionDivider => BuildDividerElements(item, state),
            SlideItemType.Stat => BuildStatElements(item, state),
            _ => BuildStandardElements(item, state)
        };
    }

    private static List<SlideElementState> BuildCoverElements(SlideItem item, SlideEditorState state)
    {
        return new List<SlideElementState>
        {
            TextElement("title", 112, 190, 1110, 150, 10, state.Title.Text, 64, true, "#F8FAFC", "left"),
            TextElement("subtitle", 118, 360, 920, 96, 20, state.Subtitle.Text, 30, false, "#D8E5F2", "left"),
            TextElement("goal", 120, 510, 760, 84, 30, state.Goal.Text, 24, true, "#A7F3D0", "left")
        };
    }

    private static List<SlideElementState> BuildDividerElements(SlideItem item, SlideEditorState state)
    {
        return new List<SlideElementState>
        {
            TextElement("title", 130, 300, 1040, 130, 10, state.Title.Text, 56, true, "#F8FAFC", "left"),
            TextElement("subtitle", 134, 455, 860, 88, 20, state.Subtitle.Text, 28, false, "#C7D2FE", "left"),
            TextElement("goal", 136, 585, 720, 76, 30, state.Goal.Text, 22, true, "#FDE68A", "left")
        };
    }

    private static List<SlideElementState> BuildStatElements(SlideItem item, SlideEditorState state)
    {
        var bodyText = state.Body.Text ?? string.Empty;
        int lineCount = bodyText.Split('\n').Length;
        int fontSize = 66;
        if (lineCount > 3 || bodyText.Length > 60)
        {
            fontSize = 28;
        }
        else if (lineCount > 1 || bodyText.Length > 30)
        {
            fontSize = 42;
        }

        return new List<SlideElementState>
        {
            TextElement("title", 96, 78, 950, 96, 10, state.Title.Text, 42, true, "#EAF7FF", "left"),
            TextElement("body", 118, 244, 560, 250, 20, bodyText, fontSize, true, "#FFFFFF", "left"),
            TextElement("goal", 118, 555, 760, 90, 30, state.Goal.Text, 26, true, "#BAE6FD", "left"),
            ImageElement("image", 980, 190, 460, 420, 15)
        };
    }

    private static List<SlideElementState> BuildStandardElements(SlideItem item, SlideEditorState state)
    {
        var bodyText = state.Body.Text ?? string.Empty;
        int lineCount = bodyText.Split('\n').Length;
        int fontSize = 28;
        if (lineCount > 4 || bodyText.Length > 200)
        {
            fontSize = 20;
        }
        else if (lineCount > 2 || bodyText.Length > 120)
        {
            fontSize = 24;
        }

        var elements = new List<SlideElementState>
        {
            TextElement("title", 96, 72, 920, 96, 10, state.Title.Text, 42, true, "#EAF7FF", "left"),
            TextElement("subtitle", 98, 166, 780, 64, 15, state.Subtitle.Text, 24, false, "#C8D7EA", "left"),
            TextElement("goal", 96, 642, 780, 80, 30, state.Goal.Text, 22, true, "#A7F3D0", "left"),
            TextElement("body", 96, 244, 760, 360, 20, bodyText, fontSize, false, "#DCEBFF", "left")
        };

        if (item.SlideType != SlideItemType.Quote)
        {
            elements.Add(ImageElement("image", 980, 190, 460, 420, 15));
        }

        if (!string.IsNullOrWhiteSpace(state.Notes.Text))
        {
            elements.Add(TextElement("notes", 96, 748, 880, 70, 40, state.Notes.Text, 18, false, "#B6C6D8", "left"));
        }

        return elements;
    }

    private static SlideElementState TextElement(
        string role,
        double x,
        double y,
        double width,
        double height,
        int zIndex,
        string text,
        int fontSize,
        bool bold,
        string color,
        string align)
    {
        return new SlideElementState
        {
            Id = role,
            Type = "text",
            Role = role,
            X = ScaleX(x),
            Y = ScaleY(y),
            Width = ScaleX(width),
            Height = ScaleY(height),
            ZIndex = zIndex,
            Visible = true,
            Text = text,
            FontSize = ScaleFont(fontSize),
            Bold = bold,
            Color = color,
            Align = align,
            TextAlign = align
        };
    }

    private static SlideElementState ImageElement(string role, double x, double y, double width, double height, int zIndex)
    {
        return new SlideElementState
        {
            Id = role,
            Type = "image",
            Role = role,
            X = ScaleX(x),
            Y = ScaleY(y),
            Width = ScaleX(width),
            Height = ScaleY(height),
            ZIndex = zIndex,
            Locked = false,
            Visible = true
        };
    }

    private static double ScaleX(double value) => Math.Round(value * SlideEditorDesignScaleX, 2);

    private static double ScaleY(double value) => Math.Round(value * SlideEditorDesignScaleY, 2);

    private static int ScaleFont(int value) => Math.Max(8, (int)Math.Round(value * SlideEditorDesignScaleY));

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

    public static SlideEvidenceDebugMetadata? GetEvidenceDebug(this SlideItem item)
    {
        if (string.IsNullOrEmpty(item.EvidenceDebugJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<SlideEvidenceDebugMetadata>(item.EvidenceDebugJson);
    }

    public static void SetEvidenceDebug(this SlideItem item, SlideEvidenceDebugMetadata? metadata)
    {
        item.EvidenceDebugJson = metadata == null ? null : JsonSerializer.Serialize(metadata);
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
