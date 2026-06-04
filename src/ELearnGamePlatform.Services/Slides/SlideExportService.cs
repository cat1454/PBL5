using System.Net;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace ELearnGamePlatform.Services.Slides;

public class SlideExportService : ISlideExportService
{
    private const long EmuPerInch = 914400;
    private const long SlideWidth = 12192000;
    private const long SlideHeight = 6858000;
    private const string SlideFontFamily = "Lexend";
    private readonly ISlideGenerator _slideGenerator;

    public SlideExportService(ISlideGenerator slideGenerator)
    {
        _slideGenerator = slideGenerator;
    }

    public string RenderHtmlFile(SlideDeck deck, IReadOnlyList<SlideItem> items)
    {
        return _slideGenerator.RenderDeckHtml(deck, items.OrderBy(item => item.SlideIndex).ToList());
    }

    public string RenderPrintHtml(SlideDeck deck, IReadOnlyList<SlideItem> items)
    {
        var orderedItems = items.OrderBy(item => item.SlideIndex).ToList();
        var builder = new StringBuilder();
        var title = Html(deck.Title ?? "Slide deck");

        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"vi\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\" />");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine($"<title>{title} - Print</title>");
        builder.AppendLine("<style>");
        builder.AppendLine(@"
@import url('https://fonts.googleapis.com/css2?family=Lexend:wght@300..900&display=swap');
:root{color-scheme:dark;--text:#e5e7eb;--muted:#9ca3af;--border:#1f2937;--paper:#111827;--accent:#2458a6;--slide-font-family:'Lexend','Noto Sans',system-ui,-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;}
*{box-sizing:border-box;}
body{margin:0;background:#0f172a;color:var(--text);font-family:var(--slide-font-family);}
.deck-shell{max-width:1180px;margin:0 auto;padding:28px 18px 48px;font-family:inherit;}
.print-toolbar{display:flex;align-items:center;justify-content:space-between;gap:16px;margin-bottom:20px;color:var(--muted);font-family:inherit;}
.print-toolbar h1{margin:0;color:var(--text);font-size:24px;line-height:1.2;}
.print-toolbar p{margin:4px 0 0;line-height:1.5;}
.print-button{border:0;border-radius:8px;background:var(--accent);color:#fff;padding:10px 16px;font:inherit;font-weight:700;cursor:pointer;}
.slide-page{position:relative;width:100%;aspect-ratio:16/9;background:var(--paper);border:1px solid var(--border);box-shadow:0 18px 48px rgba(0,0,0,.3);margin:0 auto 24px;overflow:hidden;page-break-after:always;break-after:page;font-family:inherit;}
.slide-page:last-child{page-break-after:auto;break-after:auto;}
.slide-element{position:absolute;margin:0;overflow:hidden;white-space:pre-wrap;overflow-wrap:break-word;font-family:var(--slide-font-family);line-height:1.15;}
.slide-element.image img{display:block;width:100%;height:100%;object-fit:cover;}
.slide-element.shape{white-space:normal;}
.slide-element.line{height:0!important;overflow:visible;}
.slide-element.effect-soft-shadow{filter:drop-shadow(0 18px 28px rgba(15,23,42,.38));}
.slide-element.effect-neon-glow{filter:drop-shadow(0 0 12px rgba(56,189,248,.78)) drop-shadow(0 0 28px rgba(168,85,247,.42));}
.slide-element.effect-glass-frame{padding:10px;border:1px solid rgba(226,232,240,.46);border-radius:24px;background:rgba(255,255,255,.12);box-shadow:inset 0 1px 0 rgba(255,255,255,.2),0 18px 36px rgba(15,23,42,.28);}
.slide-element.effect-paper-cut{padding:8px;background:#f8fafc;box-shadow:10px 10px 0 rgba(15,23,42,.36);color:#17212d;}
.slide-element.effect-duotone.image img{filter:grayscale(1) contrast(1.12) sepia(.42) hue-rotate(148deg) saturate(1.85);}
.slide-element.effect-duotone.text{text-shadow:2px 2px 0 rgba(14,165,233,.5),-2px -2px 0 rgba(244,114,182,.38);}
@page{size:16in 9in;margin:0;}
@media print{
  html,body{width:100%;height:100%;background:#fff;}
  .deck-shell{max-width:none;padding:0;}
  .print-toolbar{display:none;}
  .slide-page{width:16in;height:9in;aspect-ratio:auto;margin:0;border:0;box-shadow:none;page-break-after:always;break-after:page;}
  .slide-page:last-child{page-break-after:auto;break-after:auto;}
}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main class=\"deck-shell\">");
        builder.AppendLine("<section class=\"print-toolbar\">");
        builder.AppendLine("<div>");
        builder.AppendLine($"<h1>{title}</h1>");
        if (!string.IsNullOrWhiteSpace(deck.Subtitle))
        {
            builder.AppendLine($"<p>{Html(deck.Subtitle!)}</p>");
        }
        builder.AppendLine("</div>");
        builder.AppendLine("<button class=\"print-button\" type=\"button\" onclick=\"window.print()\">Print / Save as PDF</button>");
        builder.AppendLine("</section>");

        foreach (var item in orderedItems)
        {
            builder.AppendLine($"<article class=\"slide-page type-{Html(item.SlideType.ToString().ToLowerInvariant())}\" aria-label=\"Slide {item.SlideIndex}\">");
            AppendEditorElements(builder, item);
            builder.AppendLine("</article>");
        }

        builder.AppendLine("</main>");
        builder.AppendLine("<script>window.addEventListener('load',function(){setTimeout(function(){window.print();},250);});</script>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    public byte[] BuildPptx(SlideDeck deck, IReadOnlyList<SlideItem> items)
    {
        using var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = document.AddPresentationPart();
            presentationPart.Presentation = new P.Presentation();

            var slideLayoutPart = CreateBlankLayout(presentationPart);
            var slideIdList = new P.SlideIdList();
            presentationPart.Presentation.Append(slideIdList);

            var nextSlideId = 256U;
            AddPptxSlide(
                presentationPart,
                slideLayoutPart,
                slideIdList,
                ref nextSlideId,
                deck.Title ?? "Slide deck",
                deck.Subtitle,
                new[] { $"Generated deck with {items.Count} slide item(s)." },
                null,
                "Title");

            foreach (var item in items.OrderBy(item => item.SlideIndex))
            {
                var bodyBlocks = item.GetBodyBlocks();
                AddPptxSlide(
                    presentationPart,
                    slideLayoutPart,
                    slideIdList,
                    ref nextSlideId,
                    item.Heading ?? $"Slide {item.SlideIndex}",
                    item.Subheading,
                    bodyBlocks,
                    item.SpeakerNotes,
                    item.SlideType.ToString(),
                    item.Goal,
                    item.SlideIndex);
            }

            presentationPart.Presentation.SlideSize = new P.SlideSize
            {
                Cx = (int)SlideWidth,
                Cy = (int)SlideHeight,
                Type = P.SlideSizeValues.Screen16x9
            };
            presentationPart.Presentation.NotesSize = new P.NotesSize
            {
                Cx = 6858000,
                Cy = 9144000
            };
            presentationPart.Presentation.Save();
        }

        return stream.ToArray();
    }

    private static void AppendEditorElements(StringBuilder builder, SlideItem item)
    {
        var editorState = item.GetEditorState();
        var canvasWidth = Math.Max(1, editorState.Canvas?.Width ?? 1280);
        var canvasHeight = Math.Max(1, editorState.Canvas?.Height ?? 720);
        var selectedImage = ResolveSelectedImage(item);

        foreach (var element in editorState.Elements
            .Where(element => element.Visible)
            .OrderBy(element => element.ZIndex))
        {
            var type = (element.Type ?? "text").Trim().ToLowerInvariant();
            var style = BuildElementStyle(element, canvasWidth, canvasHeight);
            if (type == "text")
            {
                var text = NormalizePrintableText(element.Text);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                builder.AppendLine(
                    $"<div class=\"slide-element text {Html(BuildEffectClass(element))}\" style=\"{Html(style)}\">{Html(text)}</div>");
                continue;
            }

            if (type == "image")
            {
                var src = FirstNonBlank(element.Src, element.Url, element.Base64, selectedImage?.LocalAssetUrl, selectedImage?.ThumbnailUrl);
                if (string.IsNullOrWhiteSpace(src))
                {
                    continue;
                }

                var alt = Html(selectedImage?.AltText ?? element.Role ?? "Slide image");
                builder.AppendLine(
                    $"<div class=\"slide-element image {Html(BuildEffectClass(element))}\" style=\"{Html(style)}\"><img src=\"{Html(src!)}\" alt=\"{alt}\" /></div>");
                continue;
            }

            if (IsShapeElement(type))
            {
                var shapeStyle = BuildShapeStyle(element, style, type);
                builder.AppendLine($"<div class=\"slide-element shape {Html(type)} {Html(BuildEffectClass(element))}\" style=\"{Html(shapeStyle)}\"></div>");
            }
        }
    }

    private static string BuildEffectClass(SlideElementState element)
    {
        var preset = (element.EffectPreset ?? "none").Trim().ToLowerInvariant();
        return preset is "soft-shadow" or "neon-glow" or "glass-frame" or "paper-cut" or "duotone"
            ? $"effect-{preset}"
            : string.Empty;
    }

    private static string BuildElementStyle(SlideElementState element, int canvasWidth, int canvasHeight)
    {
        var x = ToPercent(element.X, canvasWidth);
        var y = ToPercent(element.Y, canvasHeight);
        var width = ToPercent(element.Width, canvasWidth);
        var height = ToPercent(element.Height, canvasHeight);
        var color = NormalizeCssColor(element.Color, "#ffffff");
        var align = NormalizeTextAlign(element.Align ?? element.TextAlign);
        var opacity = element.Opacity is >= 0 and <= 1 ? $";opacity:{element.Opacity.Value:0.###}" : string.Empty;
        var rotation = element.Rotation.HasValue ? $";transform:rotate({element.Rotation.Value:0.###}deg)" : string.Empty;

        return $"left:{x:0.###}%;top:{y:0.###}%;width:{width:0.###}%;height:{height:0.###}%;z-index:{element.ZIndex};color:{color};font-size:{Math.Clamp(element.FontSize, 8, 160)}px;font-weight:{(element.Bold ? 700 : 400)};text-align:{align};{opacity}{rotation}";
    }

    private static string BuildShapeStyle(SlideElementState element, string baseStyle, string type)
    {
        var fill = NormalizeCssColor(element.FillColor, "transparent");
        var border = NormalizeCssColor(element.BorderColor, "#ffffff");
        var borderWidth = Math.Max(0, element.BorderWidth ?? 0);
        var radius = type == "roundedrectangle" ? "8px" : type == "ellipse" ? "50%" : "0";

        if (type == "line")
        {
            return $"{baseStyle};background:transparent;border-top:{Math.Max(1, borderWidth):0.###}px solid {border};";
        }

        return $"{baseStyle};background:{fill};border:{borderWidth:0.###}px solid {border};border-radius:{radius};";
    }

    private static SlideImageCandidate? ResolveSelectedImage(SlideItem item)
    {
        var candidates = item.GetImageCandidates();
        if (!string.IsNullOrWhiteSpace(item.SelectedImageKey))
        {
            var selected = candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, item.SelectedImageKey, StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                return selected;
            }
        }

        return candidates.FirstOrDefault(candidate => candidate.IsSelected)
            ?? candidates.FirstOrDefault();
    }

    private static bool IsShapeElement(string type)
        => type is "rectangle" or "roundedrectangle" or "ellipse" or "line";

    private static string? NormalizePrintableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return string.Equals(trimmed, "Empty text", StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }

    private static double ToPercent(double value, int total)
        => total <= 0 ? 0 : Math.Clamp(value / total * 100, 0, 100);

    private static string NormalizeTextAlign(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "left" : value.Trim().ToLowerInvariant();
        return normalized is "left" or "center" or "right" ? normalized : "left";
    }

    private static string NormalizeCssColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith("#", StringComparison.Ordinal) || string.Equals(trimmed, "transparent", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"#{trimmed}";
    }

    private static SlideLayoutPart CreateBlankLayout(PresentationPart presentationPart)
    {
        var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>();
        var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();

        slideLayoutPart.SlideLayout = new P.SlideLayout(CreateCommonSlideData("Blank layout"))
        {
            Type = P.SlideLayoutValues.Blank,
            Preserve = true
        };
        slideLayoutPart.SlideLayout.Save();

        slideMasterPart.SlideMaster = new P.SlideMaster(
            CreateCommonSlideData("Slide master"),
            new P.ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1,
                Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1,
                Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3,
                Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
            },
            new P.SlideLayoutIdList(new P.SlideLayoutId
            {
                Id = 1U,
                RelationshipId = slideMasterPart.GetIdOfPart(slideLayoutPart)
            }),
            new P.TextStyles(new P.TitleStyle(), new P.BodyStyle(), new P.OtherStyle()));
        slideMasterPart.SlideMaster.Save();

        presentationPart.Presentation.SlideMasterIdList = new P.SlideMasterIdList(new P.SlideMasterId
        {
            Id = 2147483648U,
            RelationshipId = presentationPart.GetIdOfPart(slideMasterPart)
        });

        return slideLayoutPart;
    }
    
    private static P.CommonSlideData CreateCommonSlideData(string name)
    {
        return new P.CommonSlideData(CreateShapeTree(name));
    }

    private static P.ShapeTree CreateShapeTree(string name)
    {
        return new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = name },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new A.TransformGroup(
                new A.Offset { X = 0, Y = 0 },
                new A.Extents { Cx = 0, Cy = 0 },
                new A.ChildOffset { X = 0, Y = 0 },
                new A.ChildExtents { Cx = 0, Cy = 0 })));
    }

    private static void AddPptxSlide(
        PresentationPart presentationPart,
        SlideLayoutPart slideLayoutPart,
        P.SlideIdList slideIdList,
        ref uint nextSlideId,
        string heading,
        string? subheading,
        IReadOnlyList<string> bodyBlocks,
        string? speakerNotes,
        string slideType,
        string? goal = null,
        int? slideIndex = null)
    {
        var slidePart = presentationPart.AddNewPart<SlidePart>();
        slidePart.AddPart(slideLayoutPart);

        var shapeTree = CreateShapeTree($"Slide {nextSlideId}");
        var isTitle = string.Equals(slideType, "Title", StringComparison.OrdinalIgnoreCase) && !slideIndex.HasValue;
        var shapeId = 2U;

        shapeTree.Append(CreateBackgroundShape(shapeId++, isTitle ? "FFF4E8" : "FFFFFF"));
        shapeTree.Append(CreateTextShape(
            shapeId++,
            "Meta",
            Inches(0.72),
            Inches(0.38),
            Inches(11.8),
            Inches(0.35),
            new[] { CreateParagraph(slideIndex.HasValue ? $"Slide {slideIndex} | {slideType}" : "Title slide", 11, true, "5B6B7C") }));
        shapeTree.Append(CreateTextShape(
            shapeId++,
            "Heading",
            Inches(0.72),
            isTitle ? Inches(2.35) : Inches(0.95),
            Inches(11.85),
            isTitle ? Inches(1.35) : Inches(1.15),
            new[] { CreateParagraph(Truncate(heading, 110), isTitle ? 42 : 30, true, "17212D") }));

        var currentY = isTitle ? Inches(3.75) : Inches(2.05);
        if (!string.IsNullOrWhiteSpace(subheading))
        {
            shapeTree.Append(CreateTextShape(
                shapeId++,
                "Subheading",
                Inches(0.74),
                currentY,
                Inches(11.35),
                Inches(0.72),
                new[] { CreateParagraph(Truncate(subheading!, 160), isTitle ? 18 : 16, false, "506074") }));
            currentY += Inches(0.7);
        }

        if (!string.IsNullOrWhiteSpace(goal))
        {
            shapeTree.Append(CreateTextShape(
                shapeId++,
                "Goal",
                Inches(0.74),
                currentY,
                Inches(11.2),
                Inches(0.45),
                new[] { CreateParagraph(Truncate(goal!, 170), 13, true, "1D4ED8") }));
            currentY += Inches(0.55);
        }

        var paragraphs = bodyBlocks.Any()
            ? bodyBlocks.Take(5).Select(block => CreateParagraph($"- {Truncate(block, 180)}", isTitle ? 18 : 17, false, "17212D")).ToList()
            : new List<A.Paragraph> { CreateParagraph("Dang cho noi dung...", 16, false, "5B6B7C") };

        shapeTree.Append(CreateTextShape(
            shapeId++,
            "Body",
            Inches(0.82),
            currentY + Inches(0.1),
            Inches(11.1),
            Inches(3.15),
            paragraphs));

        if (!string.IsNullOrWhiteSpace(speakerNotes))
        {
            shapeTree.Append(CreateTextShape(
                shapeId++,
                "Speaker notes",
                Inches(0.82),
                Inches(6.45),
                Inches(11.1),
                Inches(0.55),
                new[] { CreateParagraph($"Speaker notes: {Truncate(speakerNotes!, 220)}", 10, false, "5B6B7C") }));
        }

        slidePart.Slide = new P.Slide(new P.CommonSlideData(shapeTree), new P.ColorMapOverride(new A.MasterColorMapping()));
        slidePart.Slide.Save();

        slideIdList.Append(new P.SlideId
        {
            Id = nextSlideId++,
            RelationshipId = presentationPart.GetIdOfPart(slidePart)
        });
    }

    private static P.Shape CreateBackgroundShape(uint id, string color)
    {
        return new P.Shape(
            CreateNonVisualShapeProperties(id, "Background"),
            new P.ShapeProperties(
                new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = SlideWidth, Cy = SlideHeight }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.SolidFill(new A.RgbColorModelHex { Val = color })),
            new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph()));
    }

    private static P.Shape CreateTextShape(uint id, string name, long x, long y, long width, long height, IEnumerable<A.Paragraph> paragraphs)
    {
        var textBody = new P.TextBody(
            new A.BodyProperties { Wrap = A.TextWrappingValues.Square, Anchor = A.TextAnchoringTypeValues.Top },
            new A.ListStyle());
        textBody.Append(paragraphs);

        return new P.Shape(
            CreateNonVisualShapeProperties(id, name),
            new P.ShapeProperties(
                new A.Transform2D(new A.Offset { X = x, Y = y }, new A.Extents { Cx = width, Cy = height }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.NoFill()),
            textBody);
    }

    private static P.NonVisualShapeProperties CreateNonVisualShapeProperties(uint id, string name)
    {
        return new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = id, Name = name },
            new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
            new P.ApplicationNonVisualDrawingProperties());
    }

    private static A.Paragraph CreateParagraph(string text, int fontSize, bool bold, string color)
    {
        var runProperties = new A.RunProperties
        {
            Language = "vi-VN",
            FontSize = fontSize * 100,
            Bold = bold
        };
        runProperties.Append(new A.SolidFill(new A.RgbColorModelHex { Val = color }));
        runProperties.Append(new A.LatinFont { Typeface = SlideFontFamily });
        runProperties.Append(new A.EastAsianFont { Typeface = SlideFontFamily });

        return new A.Paragraph(
            new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Left },
            new A.Run(runProperties, new A.Text(text ?? string.Empty)));
    }

    private static long Inches(double value) => (long)Math.Round(value * EmuPerInch);

    private static void AppendBodyHtml(StringBuilder builder, IReadOnlyList<string> blocks, SlideItemType slideType)
    {
        builder.AppendLine("<div class=\"slide-body\">");
        if (!blocks.Any())
        {
            builder.AppendLine("<p>Dang cho noi dung...</p>");
        }
        else if (slideType == SlideItemType.Quote)
        {
            foreach (var block in blocks.Take(2))
            {
                builder.AppendLine($"<p>{Html(block)}</p>");
            }
        }
        else
        {
            builder.AppendLine("<ul>");
            foreach (var block in blocks)
            {
                builder.AppendLine($"<li>{Html(block)}</li>");
            }
            builder.AppendLine("</ul>");
        }
        builder.AppendLine("</div>");
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "...";
    }
}
