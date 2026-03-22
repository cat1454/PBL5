using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace ELearnGamePlatform.Services.AI;

public class SlideGeneratorService : ISlideGenerator
{
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<SlideGeneratorService> _logger;
    private const int ChunkSize = 6500;
    private const int ChunkOverlap = 250;
    private const int EvidenceChunkLimit = 3;
    private const int SlideRetryLimit = 1;
    private static readonly HashSet<string> SupportedThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "editorial-sunrise",
        "midnight-signal",
        "paper-mint",
        "cobalt-grid"
    };
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "va", "la", "cua", "cho", "voi", "trong", "tren", "duoc", "mot", "nhung", "cac", "khi", "neu",
        "thi", "tai", "theo", "ve", "den", "tu", "co", "khong", "nay", "do", "day", "sau", "truoc",
        "page", "from", "with", "that", "this", "have", "about", "their", "there", "would"
    };

    public SlideGeneratorService(IOllamaService ollamaService, ILogger<SlideGeneratorService> logger)
    {
        _ollamaService = ollamaService;
        _logger = logger;
    }

    public async Task<SlideOutlineResult> GenerateOutlineAsync(
        string content,
        ProcessedContent? processedContent,
        SlideDeckBrief? brief,
        int desiredSlideCount,
        IProgress<SlideGenerationProgressUpdate>? progress = null)
    {
        var normalized = NormalizeContent(content);
        var chunks = GetCoverageChunks(normalized, processedContent);
        var targetCount = Math.Clamp(desiredSlideCount, 5, 10);

        Report(
            progress,
            12,
            "coverage-map",
            processedContent?.CoverageMap.Any() == true
                ? "Dang tai su dung coverage map da luu de lap outline"
                : "Dang doc toan bo tai lieu de lap outline",
            "Coverage map",
            $"So phan doc duoc: {chunks.Count}, theme: {NormalizeThemeKey(brief?.ThemeKey)}");

        SlideOutlineDraft? currentDraft = null;
        var qualityIssues = new List<string>();

        for (var attempt = 0; attempt <= SlideRetryLimit; attempt++)
        {
            if (attempt == 0)
            {
                try
                {
                    var prompt = BuildOutlinePrompt(processedContent, brief, chunks, targetCount);
                    currentDraft = await _ollamaService.GenerateStructuredResponseAsync<SlideOutlineDraft>(
                        prompt,
                        "You are a presentation strategist. Build concise, grounded slide outlines.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error generating outline, retry/fallback will be used.");
                    qualityIssues = new List<string> { "AI khong tra ve outline hop le o luot dau." };
                }
            }

            if (currentDraft != null)
            {
                var outline = NormalizeOutlineResult(currentDraft, chunks, processedContent, brief, targetCount);
                outline = await PolishOutlineAsync(processedContent, brief, chunks, targetCount, outline);

                if (IsOutlineQualityAcceptable(outline, targetCount, out qualityIssues))
                {
                    return outline;
                }
            }

            if (attempt >= SlideRetryLimit)
            {
                break;
            }

            currentDraft = await RetryGenerateOutlineAsync(processedContent, brief, chunks, targetCount, qualityIssues);
        }

        return BuildFallbackOutline(processedContent, brief, chunks, targetCount);
    }

    public async Task<SlideContentResult> GenerateSlideAsync(
        string content,
        ProcessedContent? processedContent,
        SlideDeckBrief? brief,
        SlideOutlineSlide outlineSlide,
        int slideNumber,
        int totalSlides,
        IProgress<SlideGenerationProgressUpdate>? progress = null)
    {
        var chunks = GetCoverageChunks(NormalizeContent(content), processedContent);
        var evidence = SelectEvidenceChunks(chunks, outlineSlide);

        Report(progress, 20, "generate-slide", $"Dang sinh noi dung slide {slideNumber}/{totalSlides}", "Sinh slide");

        SlideContentDraft? currentDraft = null;
        var qualityIssues = new List<string>();

        for (var attempt = 0; attempt <= SlideRetryLimit; attempt++)
        {
            if (attempt == 0)
            {
                try
                {
                    var prompt = BuildSlidePrompt(processedContent, brief, outlineSlide, evidence);
                    currentDraft = await _ollamaService.GenerateStructuredResponseAsync<SlideContentDraft>(
                        prompt,
                        "You create concise grounded slides. Never invent facts outside allowed evidence.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error generating slide {SlideNumber}, retry/fallback will be used.", slideNumber);
                    qualityIssues = new List<string> { "AI khong tra ve noi dung slide hop le o luot dau." };
                }
            }

            if (currentDraft != null)
            {
                var result = NormalizeSlideContent(currentDraft, outlineSlide, brief, evidence);
                result = await PolishSlideContentAsync(brief, outlineSlide, evidence, result);

                if (IsSlideQualityAcceptable(result, outlineSlide.SlideType, out qualityIssues))
                {
                    ApplySlideVerifierMetadata(result, outlineSlide.SlideType, evidence, usedFallback: false);
                    return result;
                }
            }

            if (attempt >= SlideRetryLimit)
            {
                break;
            }

            currentDraft = await RetryGenerateSlideContentAsync(processedContent, brief, outlineSlide, evidence, qualityIssues);
        }

        var fallback = BuildFallbackSlideContent(outlineSlide, brief, evidence);
        ApplySlideVerifierMetadata(fallback, outlineSlide.SlideType, evidence, usedFallback: true);
        return fallback;
    }

    public string RenderDeckHtml(SlideDeck deck, IReadOnlyList<SlideItem> items)
    {
        var themeCss = BuildThemeCss(deck.ThemeKey);
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"vi\"><head><meta charset=\"utf-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        html.AppendLine($"<title>{Html(deck.Title ?? "Slide deck")}</title>");
        html.AppendLine("<style>");
        html.AppendLine($"{themeCss} body{{margin:0;font-family:Georgia,'Times New Roman',serif;background:var(--deck-bg);color:var(--deck-text);}} .page{{width:min(1080px,calc(100vw - 32px));margin:0 auto;padding:32px 0 64px;}} .hero{{padding:40px 8px 12px;}} .hero h1{{margin:0;font-size:clamp(2.4rem,6vw,4.9rem);line-height:.94;letter-spacing:-.05em;}} .hero p{{max-width:780px;color:var(--deck-muted);line-height:1.8;}} .slides{{display:grid;gap:22px;margin-top:24px;}} .slide{{background:var(--card-bg);border:1px solid var(--card-border);border-radius:30px;padding:30px;box-shadow:0 18px 48px rgba(15,23,42,.12);page-break-inside:avoid;}} .slide-meta{{display:flex;justify-content:space-between;font:600 .86rem Arial,sans-serif;text-transform:uppercase;letter-spacing:.08em;color:var(--deck-soft);}} .slide h2{{margin:10px 0 0;font-size:clamp(1.7rem,4.5vw,3rem);line-height:1.05;letter-spacing:-.03em;}} .slide p.sub{{margin:0;color:var(--deck-muted);line-height:1.7;}} .goal{{display:inline-block;margin-top:12px;padding:9px 14px;border-radius:999px;background:var(--goal-bg);color:var(--goal-text);font:500 .92rem Arial,sans-serif;}} .body{{margin-top:18px;font-family:Arial,sans-serif;}} .body ul{{margin:0;padding-left:20px;display:grid;gap:10px;}} .body li,.body p,.notes p{{line-height:1.7;}} .notes{{border-top:1px solid var(--notes-border);margin-top:16px;padding-top:14px;color:var(--deck-soft);font:.94rem Arial,sans-serif;}} .slide-title{{min-height:420px;display:grid;align-content:end;background:var(--title-bg);}} .slide-title h2{{font-size:clamp(2.8rem,7vw,5rem);}} .slide-sectiondivider{{background:var(--divider-bg);color:var(--divider-text);}} .slide-sectiondivider p.sub,.slide-sectiondivider .slide-meta,.slide-sectiondivider .notes{{color:var(--divider-muted);}} .slide-highlight{{background:var(--highlight-bg);}} .slide-quote .body p{{font-size:1.16rem;font-style:italic;}} .slide-stat .body li{{font-weight:700;}} @media print{{body{{background:#fff}}.page{{width:100%;padding:0}}.hero{{display:none}}.slide{{box-shadow:none;margin-bottom:18px}}}}");
        html.AppendLine("</style></head><body><div class=\"page\">");
        html.AppendLine($"<section class=\"hero\"><h1>{Html(deck.Title ?? "Slide deck")}</h1>{(string.IsNullOrWhiteSpace(deck.Subtitle) ? string.Empty : $"<p>{Html(deck.Subtitle!)}</p>")}</section>");
        html.AppendLine("<section class=\"slides\">");

        foreach (var item in items.OrderBy(item => item.SlideIndex))
        {
            html.AppendLine($"<article class=\"slide slide-{item.SlideType.ToString().ToLowerInvariant()}\">");
            html.AppendLine("<div class=\"slide-meta\">");
            html.AppendLine($"<span>Slide {item.SlideIndex}</span><span>{Html(item.SlideType.ToString())}</span>");
            html.AppendLine("</div>");
            html.AppendLine($"<h2>{Html(item.Heading ?? $"Slide {item.SlideIndex}")}</h2>");
            if (!string.IsNullOrWhiteSpace(item.Subheading))
            {
                html.AppendLine($"<p class=\"sub\">{Html(item.Subheading!)}</p>");
            }
            if (!string.IsNullOrWhiteSpace(item.Goal))
            {
                html.AppendLine($"<div class=\"goal\">{Html(item.Goal!)}</div>");
            }
            html.AppendLine("<div class=\"body\">");
            AppendBodyHtml(html, GetBodyBlocks(item.BodyJson), item.SlideType);
            html.AppendLine("</div>");
            if (!string.IsNullOrWhiteSpace(item.SpeakerNotes))
            {
                html.AppendLine($"<div class=\"notes\"><p>{Html(item.SpeakerNotes!)}</p></div>");
            }
            html.AppendLine("</article>");
        }

        html.AppendLine("</section></div></body></html>");
        return html.ToString();
    }

    private static string BuildOutlinePrompt(ProcessedContent? processedContent, SlideDeckBrief? brief, List<DocumentChunk> chunks, int targetCount)
        => $@"You are creating a presentation outline from an educational document.

Deck brief:
{BuildBriefBlock(brief)}

Document analysis:
{BuildAnalyzedContentBlock(processedContent)}

Coverage map:
{BuildCoverageMapBlock(chunks)}

Requirements:
1. Return one JSON object only.
2. Write visible text in Vietnamese.
3. Create exactly {targetCount} slides.
4. Slide 1 must be Title.
5. Include one SectionDivider in the early deck.
6. Use a Gamma-like narrative rhythm: open strong, establish structure, reveal insights progressively, end with memorable takeaways.
7. Use a varied mix of slideType values chosen from Title, SectionDivider, Content, Quote, Highlight, Stat.
8. Each slide needs heading, optional subheading, short goal, and 1-3 preferredChunkIds.
9. preferredChunkIds must come exactly from the coverage map.
10. Cover early, middle, and late parts of the document.
11. Headings must feel polished and presentation-ready, not like raw chapter names.
12. Subtitle and goals must fit the brief.

Return JSON:
{{
  ""title"": ""ten deck"",
  ""subtitle"": ""mo ta ngan"",
  ""themeKey"": ""editorial-sunrise"",
  ""slides"": [
    {{
      ""slideIndex"": 1,
      ""slideType"": ""Title"",
      ""heading"": ""tieu de slide"",
      ""subheading"": ""phu de"",
      ""goal"": ""muc tieu ngan"",
      ""preferredChunkIds"": [""C01""]
    }}
  ]
}}";

    private static string BuildSlidePrompt(ProcessedContent? processedContent, SlideDeckBrief? brief, SlideOutlineSlide outlineSlide, List<DocumentChunk> evidence)
        => $@"You are generating one presentation slide.

Deck brief:
{BuildBriefBlock(brief)}

Document analysis:
{BuildAnalyzedContentBlock(processedContent)}

Slide brief:
- slideType: {outlineSlide.SlideType}
- heading: {outlineSlide.Heading}
- subheading: {outlineSlide.Subheading}
- goal: {outlineSlide.Goal}
- preferredChunkIds: {string.Join(", ", outlineSlide.PreferredChunkIds)}

Allowed evidence only:
{BuildEvidenceBlock(evidence)}

Requirements:
1. Return one JSON object only.
2. Write visible text in Vietnamese.
3. Keep content concise and grounded in the evidence.
4. Match the style of a polished modern presentation, not an academic wall of text.
5. Adapt structure to the slideType.
6. bodyBlocks must contain 1-5 short blocks.
7. speakerNotes should be 2-4 short sentences.
8. For Title slides, bodyBlocks should feel like a sharp framing summary.
9. For SectionDivider slides, bodyBlocks should signal the next section.
10. For Quote slides, make 1-2 impactful lines.
11. For Stat slides, make each block feel like a key metric or standout fact.
12. For Highlight slides, make the content memorable and takeaway-driven.

Return JSON:
{{
  ""heading"": ""tieu de slide"",
  ""subheading"": ""phu de"",
  ""goal"": ""muc tieu ngan"",
  ""bodyBlocks"": [""bullet 1"", ""bullet 2""],
  ""speakerNotes"": ""ghi chu trinh bay"",
  ""accentTone"": ""warm""
}}";

    private static SlideOutlineResult NormalizeOutlineResult(SlideOutlineDraft? draft, List<DocumentChunk> chunks, ProcessedContent? processedContent, SlideDeckBrief? brief, int targetCount)
    {
        if (draft?.Slides == null || draft.Slides.Count == 0)
        {
            return BuildFallbackOutline(processedContent, brief, chunks, targetCount);
        }

        var slides = new List<SlideOutlineSlide>();
        foreach (var raw in draft.Slides.OrderBy(slide => slide.SlideIndex))
        {
            var heading = NormalizeLine(raw.Heading, 160);
            if (string.IsNullOrWhiteSpace(heading))
            {
                continue;
            }

            slides.Add(new SlideOutlineSlide
            {
                SlideIndex = slides.Count + 1,
                SlideType = ParseSlideType(raw.SlideType, slides.Count == 0),
                Heading = heading,
                Subheading = NormalizeLine(raw.Subheading, 200),
                Goal = NormalizeLine(raw.Goal, 180) ?? "Lam ro y chinh cua slide nay",
                PreferredChunkIds = NormalizePreferredChunkIds(raw.PreferredChunkIds, chunks, slides.Count)
            });
        }

        if (!slides.Any())
        {
            return BuildFallbackOutline(processedContent, brief, chunks, targetCount);
        }

        ApplyNarrativeRhythm(slides);

        if (slides.Count < targetCount)
        {
            var fallbackSlides = BuildFallbackOutline(processedContent, brief, chunks, targetCount).Slides;
            foreach (var fallbackSlide in fallbackSlides)
            {
                if (slides.Count >= targetCount)
                {
                    break;
                }

                if (slides.Any(existing => string.Equals(existing.Heading, fallbackSlide.Heading, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                slides.Add(new SlideOutlineSlide
                {
                    SlideIndex = slides.Count + 1,
                    SlideType = fallbackSlide.SlideType,
                    Heading = fallbackSlide.Heading,
                    Subheading = fallbackSlide.Subheading,
                    Goal = fallbackSlide.Goal,
                    PreferredChunkIds = fallbackSlide.PreferredChunkIds
                });
            }
        }

        return new SlideOutlineResult
        {
            Title = NormalizeLine(draft.Title, 160) ?? processedContent?.MainTopics.FirstOrDefault() ?? slides[0].Heading,
            Subtitle = NormalizeLine(draft.Subtitle, 260) ?? brief?.NarrativeGoal ?? processedContent?.Summary ?? "Bo slide duoc sinh tu tai lieu upload.",
            ThemeKey = NormalizeThemeKey(string.IsNullOrWhiteSpace(draft.ThemeKey) ? brief?.ThemeKey : draft.ThemeKey),
            Brief = NormalizeBrief(brief),
            Slides = slides.Take(targetCount).ToList()
        };
    }

    private static SlideContentResult NormalizeSlideContent(SlideContentDraft? draft, SlideOutlineSlide outlineSlide, SlideDeckBrief? brief, List<DocumentChunk> evidence)
    {
        var blocks = draft?.BodyBlocks?
            .Select(block => NormalizeLine(block, 220))
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Cast<string>()
            .ToList()
            ?? new List<string>();

        if (!blocks.Any())
        {
            return BuildFallbackSlideContent(outlineSlide, brief, evidence);
        }

        return new SlideContentResult
        {
            Heading = NormalizeLine(draft?.Heading, 160) ?? outlineSlide.Heading,
            Subheading = NormalizeLine(draft?.Subheading, 220) ?? outlineSlide.Subheading,
            Goal = NormalizeLine(draft?.Goal, 180) ?? outlineSlide.Goal,
            BodyBlocks = NormalizeBodyBlocksForSlideType(outlineSlide.SlideType, blocks),
            SpeakerNotes = NormalizeLine(draft?.SpeakerNotes, 520) ?? BuildSpeakerNotes(outlineSlide, evidence),
            AccentTone = NormalizeAccentTone(draft?.AccentTone, brief, outlineSlide.SlideType)
        };
    }

    private async Task<SlideOutlineResult> PolishOutlineAsync(
        ProcessedContent? processedContent,
        SlideDeckBrief? brief,
        List<DocumentChunk> chunks,
        int targetCount,
        SlideOutlineResult outline)
    {
        try
        {
            var prompt = $@"Polish the learner-facing presentation outline below.

Deck brief:
{BuildBriefBlock(brief)}

Document analysis:
{BuildAnalyzedContentBlock(processedContent)}

Coverage map:
{BuildCoverageMapBlock(chunks)}

Current outline:
{BuildOutlineSnapshot(outline)}

Requirements:
1. Keep the same narrative direction and keep slide count at exactly {targetCount}.
2. Keep preferredChunkIds grounded in the same document coverage map.
3. Rewrite only for cleaner, sharper, more presentation-ready Vietnamese.
4. Remove OCR artifacts, raw chapter-name wording, duplicated ideas, and machine-like phrasing.
5. Slide 1 must remain Title and at least one early slide must remain SectionDivider.
6. Do not invent facts outside the analyzed content and coverage map.

Return JSON only:
{BuildOutlineExample(targetCount)}";

            var polished = await _ollamaService.GenerateStructuredResponseAsync<SlideOutlineDraft>(
                prompt,
                "You are a Vietnamese presentation editor. Polish outlines without inventing new facts.",
                OllamaModelProfile.Generation);

            return polished == null
                ? outline
                : NormalizeOutlineResult(polished, chunks, processedContent, brief, targetCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error polishing slide outline.");
            return outline;
        }
    }

    private async Task<SlideContentResult> PolishSlideContentAsync(
        SlideDeckBrief? brief,
        SlideOutlineSlide outlineSlide,
        List<DocumentChunk> evidence,
        SlideContentResult content)
    {
        try
        {
            var prompt = $@"Polish the learner-facing slide below.

Deck brief:
{BuildBriefBlock(brief)}

Slide outline:
- slideType: {outlineSlide.SlideType}
- heading: {outlineSlide.Heading}
- subheading: {outlineSlide.Subheading}
- goal: {outlineSlide.Goal}
- preferredChunkIds: {string.Join(", ", outlineSlide.PreferredChunkIds)}

Allowed evidence only:
{BuildEvidenceBlock(evidence)}

Current slide:
{BuildSlideSnapshot(content)}

Requirements:
1. Keep the same factual meaning and stay within the allowed evidence.
2. Write natural, concise, presentation-ready Vietnamese.
3. Remove OCR artifacts, prompt-like wording, placeholders, and duplicated ideas.
4. Preserve the slideType structure and keep 1-5 short bodyBlocks.
5. speakerNotes must be clear, calm, and learner-facing.

Return JSON only:
{BuildSlideContentExample()}";

            var polished = await _ollamaService.GenerateStructuredResponseAsync<SlideContentDraft>(
                prompt,
                "You are a senior Vietnamese presentation editor. Polish slides without changing facts.",
                OllamaModelProfile.Generation);

            return ApplySlidePolishDraft(content, polished, outlineSlide, brief, evidence);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error polishing slide content for {Heading}", outlineSlide.Heading);
            return content;
        }
    }

    private async Task<SlideOutlineDraft?> RetryGenerateOutlineAsync(
        ProcessedContent? processedContent,
        SlideDeckBrief? brief,
        List<DocumentChunk> chunks,
        int targetCount,
        IReadOnlyList<string> issues)
    {
        try
        {
            var prompt = $@"Retry the presentation outline generation.

Deck brief:
{BuildBriefBlock(brief)}

Document analysis:
{BuildAnalyzedContentBlock(processedContent)}

Coverage map:
{BuildCoverageMapBlock(chunks)}

Previous attempt issues:
- {string.Join("\n- ", issues.Where(issue => !string.IsNullOrWhiteSpace(issue)).DefaultIfEmpty("Outline chua dat quality gate"))}

Requirements:
1. Return exactly {targetCount} slides.
2. Keep the deck grounded in the coverage map.
3. Make headings and goals clean, modern, and learner-friendly in Vietnamese.
4. Avoid OCR artifacts, repeated headings, raw chapter labels, and template wording.
5. Include Title first, one early SectionDivider, and a varied narrative rhythm.

Return JSON only:
{BuildOutlineExample(targetCount)}";

            return await _ollamaService.GenerateStructuredResponseAsync<SlideOutlineDraft>(
                prompt,
                "You are retrying a grounded presentation outline. Return strict JSON only.",
                OllamaModelProfile.Generation);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrying outline generation.");
            return null;
        }
    }

    private async Task<SlideContentDraft?> RetryGenerateSlideContentAsync(
        ProcessedContent? processedContent,
        SlideDeckBrief? brief,
        SlideOutlineSlide outlineSlide,
        List<DocumentChunk> evidence,
        IReadOnlyList<string> issues)
    {
        try
        {
            var prompt = $@"Retry one grounded slide.

Deck brief:
{BuildBriefBlock(brief)}

Document analysis:
{BuildAnalyzedContentBlock(processedContent)}

Slide outline:
- slideType: {outlineSlide.SlideType}
- heading: {outlineSlide.Heading}
- subheading: {outlineSlide.Subheading}
- goal: {outlineSlide.Goal}
- preferredChunkIds: {string.Join(", ", outlineSlide.PreferredChunkIds)}

Allowed evidence only:
{BuildEvidenceBlock(evidence)}

Previous attempt issues:
- {string.Join("\n- ", issues.Where(issue => !string.IsNullOrWhiteSpace(issue)).DefaultIfEmpty("Slide chua dat quality gate"))}

Requirements:
1. Stay inside the allowed evidence only.
2. Produce polished, concise Vietnamese for a premium presentation.
3. Remove placeholders, OCR artifacts, raw wording, and duplicate blocks.
4. Keep 1-5 short bodyBlocks and clear speaker notes.

Return JSON only:
{BuildSlideContentExample()}";

            return await _ollamaService.GenerateStructuredResponseAsync<SlideContentDraft>(
                prompt,
                "You are retrying a grounded presentation slide. Return strict JSON only.",
                OllamaModelProfile.Generation);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrying slide content for {Heading}", outlineSlide.Heading);
            return null;
        }
    }

    private static SlideContentResult ApplySlidePolishDraft(
        SlideContentResult current,
        SlideContentDraft? draft,
        SlideOutlineSlide outlineSlide,
        SlideDeckBrief? brief,
        List<DocumentChunk> evidence)
    {
        if (draft == null)
        {
            return current;
        }

        var merged = new SlideContentDraft
        {
            Heading = draft.Heading ?? current.Heading,
            Subheading = draft.Subheading ?? current.Subheading,
            Goal = draft.Goal ?? current.Goal,
            BodyBlocks = draft.BodyBlocks?.Any() == true ? draft.BodyBlocks : current.BodyBlocks,
            SpeakerNotes = draft.SpeakerNotes ?? current.SpeakerNotes,
            AccentTone = draft.AccentTone ?? current.AccentTone
        };

        return NormalizeSlideContent(merged, outlineSlide, brief, evidence);
    }

    private static bool IsOutlineQualityAcceptable(
        SlideOutlineResult outline,
        int targetCount,
        out List<string> issues)
    {
        issues = new List<string>();

        if (string.IsNullOrWhiteSpace(outline.Title) || TextCleanupUtility.HasNoisyArtifacts(outline.Title))
        {
            issues.Add("Tieu de deck chua sach hoac dang rong.");
        }

        if (!string.IsNullOrWhiteSpace(outline.Subtitle) && TextCleanupUtility.HasNoisyArtifacts(outline.Subtitle))
        {
            issues.Add("Phu de deck con artifact.");
        }

        if (outline.Slides.Count != targetCount)
        {
            issues.Add("So slide chua dung theo yeu cau.");
        }

        if (!outline.Slides.Any())
        {
            issues.Add("Outline khong co slide nao.");
            return false;
        }

        if (outline.Slides[0].SlideType != SlideItemType.Title)
        {
            issues.Add("Slide dau tien chua la Title.");
        }

        if (!outline.Slides.Skip(1).Take(Math.Min(3, Math.Max(0, outline.Slides.Count - 1))).Any(slide => slide.SlideType == SlideItemType.SectionDivider))
        {
            issues.Add("Chua co SectionDivider som trong deck.");
        }

        if (outline.Slides.Select(slide => slide.Heading).Distinct(StringComparer.OrdinalIgnoreCase).Count() < Math.Max(2, outline.Slides.Count - 1))
        {
            issues.Add("Heading slide bi trung qua nhieu.");
        }

        foreach (var slide in outline.Slides)
        {
            if (string.IsNullOrWhiteSpace(slide.Heading) || slide.Heading.Length < 6 || TextCleanupUtility.HasNoisyArtifacts(slide.Heading))
            {
                issues.Add($"Heading slide {slide.SlideIndex} chua dat chat luong.");
            }

            if (!string.IsNullOrWhiteSpace(slide.Goal) && TextCleanupUtility.HasNoisyArtifacts(slide.Goal))
            {
                issues.Add($"Goal slide {slide.SlideIndex} con artifact.");
            }

            if (slide.PreferredChunkIds.Count == 0)
            {
                issues.Add($"Slide {slide.SlideIndex} chua co preferredChunkIds.");
            }
        }

        return issues.Count == 0;
    }

    private static bool IsSlideQualityAcceptable(
        SlideContentResult content,
        SlideItemType slideType,
        out List<string> issues)
    {
        issues = new List<string>();

        if (string.IsNullOrWhiteSpace(content.Heading) || content.Heading.Length < 6 || TextCleanupUtility.HasNoisyArtifacts(content.Heading))
        {
            issues.Add("Heading slide chua sach hoac qua ngan.");
        }

        if (!string.IsNullOrWhiteSpace(content.Subheading) && TextCleanupUtility.HasNoisyArtifacts(content.Subheading))
        {
            issues.Add("Subheading con artifact.");
        }

        if (!string.IsNullOrWhiteSpace(content.Goal) && TextCleanupUtility.HasNoisyArtifacts(content.Goal))
        {
            issues.Add("Goal con artifact.");
        }

        if (!content.BodyBlocks.Any() || content.BodyBlocks.Count > 5)
        {
            issues.Add("So body block khong hop le.");
        }
        else
        {
            if (content.BodyBlocks.Any(block => string.IsNullOrWhiteSpace(block) || block.Length < 6 || TextCleanupUtility.HasNoisyArtifacts(block)))
            {
                issues.Add("Body block con artifact hoac qua ngan.");
            }

            if (content.BodyBlocks.Select(block => block.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != content.BodyBlocks.Count)
            {
                issues.Add("Body block bi trung nhau.");
            }
        }

        if (!string.IsNullOrWhiteSpace(content.SpeakerNotes) && TextCleanupUtility.HasNoisyArtifacts(content.SpeakerNotes))
        {
            issues.Add("Speaker notes con artifact.");
        }

        switch (slideType)
        {
            case SlideItemType.Title:
            case SlideItemType.SectionDivider:
                if (content.BodyBlocks.Count > 2)
                {
                    issues.Add("Title/SectionDivider dang qua dai.");
                }
                break;
            case SlideItemType.Quote:
                if (content.BodyBlocks.Count > 2)
                {
                    issues.Add("Quote slide nen ngan hon.");
                }
                break;
            case SlideItemType.Stat:
                if (content.BodyBlocks.All(block => !block.Any(char.IsDigit)))
                {
                    issues.Add("Stat slide chua co chi tiet noi bat dang metric/fact.");
                }
                break;
        }

        return issues.Count == 0;
    }

    private static void ApplySlideVerifierMetadata(
        SlideContentResult content,
        SlideItemType slideType,
        IReadOnlyCollection<DocumentChunk> evidence,
        bool usedFallback)
    {
        var score = 100;
        var warnings = new List<string>();

        void AddWarning(string message, int penalty)
        {
            if (warnings.Any(existing => string.Equals(existing, message, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            warnings.Add(message);
            score -= penalty;
        }

        if (usedFallback)
        {
            AddWarning("Slide nay dang dung duong fallback vi AI chua tra ve noi dung grounded dat yeu cau.", 30);
        }

        if (string.IsNullOrWhiteSpace(content.Heading))
        {
            AddWarning("Heading slide dang rong.", 35);
        }
        else
        {
            if (content.Heading.Length < 10 || content.Heading.Length > 120)
            {
                AddWarning("Heading slide co do dai chua toi uu.", 8);
            }

            if (TextCleanupUtility.HasNoisyArtifacts(content.Heading))
            {
                AddWarning("Heading slide con dau hieu artifact hoac wording may.", 28);
            }
        }

        if (!string.IsNullOrWhiteSpace(content.Subheading) && TextCleanupUtility.HasNoisyArtifacts(content.Subheading))
        {
            AddWarning("Subheading con artifact.", 14);
        }

        if (!string.IsNullOrWhiteSpace(content.Goal) && TextCleanupUtility.HasNoisyArtifacts(content.Goal))
        {
            AddWarning("Goal slide con artifact.", 14);
        }

        if (!content.BodyBlocks.Any())
        {
            AddWarning("Slide chua co body block.", 35);
        }
        else
        {
            if (content.BodyBlocks.Count == 1)
            {
                AddWarning("Slide moi co mot body block, co the chua du do phu.", 8);
            }

            if (content.BodyBlocks.Any(block => block.Length < 12))
            {
                AddWarning("Mot vai body block qua ngan.", 8);
            }

            if (content.BodyBlocks.Any(block => TextCleanupUtility.HasNoisyArtifacts(block)))
            {
                AddWarning("Mot vai body block con artifact.", 20);
            }

            if (content.BodyBlocks.Select(block => block.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != content.BodyBlocks.Count)
            {
                AddWarning("Body block bi trung nhau.", 14);
            }
        }

        if (evidence.Count < 2)
        {
            AddWarning("Slide dang dua tren it evidence chunk.", 5);
        }

        if (string.IsNullOrWhiteSpace(content.SpeakerNotes))
        {
            AddWarning("Speaker notes dang rong.", 8);
        }
        else
        {
            if (content.SpeakerNotes.Length < 40)
            {
                AddWarning("Speaker notes kha ngan.", 6);
            }

            if (TextCleanupUtility.HasNoisyArtifacts(content.SpeakerNotes))
            {
                AddWarning("Speaker notes con artifact.", 16);
            }
        }

        switch (slideType)
        {
            case SlideItemType.Title:
            case SlideItemType.SectionDivider:
                if (content.BodyBlocks.Count > 2)
                {
                    AddWarning("Title/SectionDivider dang mang qua nhieu body block.", 8);
                }
                break;
            case SlideItemType.Stat:
                if (content.BodyBlocks.All(block => !block.Any(char.IsDigit)))
                {
                    AddWarning("Stat slide chua co metric/fact noi bat ro rang.", 12);
                }
                break;
            case SlideItemType.Quote:
                if (content.BodyBlocks.Count > 2)
                {
                    AddWarning("Quote slide nen co it dong hon de tao diem nhan.", 8);
                }
                break;
        }

        content.VerifierScore = Math.Clamp(score, 0, 100);
        content.VerifierIssues = warnings;
    }

    private static string BuildOutlineSnapshot(SlideOutlineResult outline)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Title: {outline.Title}");
        builder.AppendLine($"Subtitle: {outline.Subtitle}");
        builder.AppendLine($"ThemeKey: {outline.ThemeKey}");
        builder.AppendLine("Slides:");

        foreach (var slide in outline.Slides.OrderBy(slide => slide.SlideIndex))
        {
            builder.AppendLine($"- #{slide.SlideIndex} | {slide.SlideType} | {slide.Heading}");
            builder.AppendLine($"  Subheading: {slide.Subheading}");
            builder.AppendLine($"  Goal: {slide.Goal}");
            builder.AppendLine($"  PreferredChunkIds: {string.Join(", ", slide.PreferredChunkIds)}");
        }

        return builder.ToString().Trim();
    }

    private static string BuildSlideSnapshot(SlideContentResult content)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Heading: {content.Heading}");
        builder.AppendLine($"Subheading: {content.Subheading}");
        builder.AppendLine($"Goal: {content.Goal}");
        builder.AppendLine("Body blocks:");
        foreach (var block in content.BodyBlocks)
        {
            builder.AppendLine($"- {block}");
        }
        builder.AppendLine($"Speaker notes: {content.SpeakerNotes}");
        builder.AppendLine($"Accent tone: {content.AccentTone}");
        return builder.ToString().Trim();
    }

    private static string BuildOutlineExample(int targetCount)
        => $@"{{
  ""title"": ""Tieu de deck ngan, ro, presentation-ready"",
  ""subtitle"": ""Mo ta ngan, tu nhien, de doc"",
  ""themeKey"": ""editorial-sunrise"",
  ""slides"": [
    {{
      ""slideIndex"": 1,
      ""slideType"": ""Title"",
      ""heading"": ""Mo ra boi canh chinh"",
      ""subheading"": ""Tom tat cuc ngan"",
      ""goal"": ""Dat ky vong cho nguoi hoc"",
      ""preferredChunkIds"": [""C01""]
    }}
  ]
}}";

    private static string BuildSlideContentExample()
        => @"{
  ""heading"": ""Tieu de slide ro nghia, gon, dep"",
  ""subheading"": ""Dong phu de ngan gon"",
  ""goal"": ""Y nghia cua slide nay"",
  ""bodyBlocks"": [""Bullet ngan 1"", ""Bullet ngan 2""],
  ""speakerNotes"": ""Goi y cach trinh bay ngan gon, de hieu."",
  ""accentTone"": ""warm""
}";

    private static SlideOutlineResult BuildFallbackOutline(ProcessedContent? processedContent, SlideDeckBrief? brief, List<DocumentChunk> chunks, int targetCount)
    {
        if (!chunks.Any())
        {
            chunks.Add(new DocumentChunk
            {
                ChunkNumber = 1,
                ChunkId = "C01",
                Zone = "giua",
                Label = "Tong quan tai lieu",
                Summary = processedContent?.Summary ?? "Tai lieu chua co du lieu de lap outline.",
                KeyFacts = processedContent?.KeyPoints.Take(3).ToList() ?? new List<string>(),
                EvidenceExcerpt = processedContent?.Summary ?? "Noi dung se duoc cap nhat sau.",
                SearchTokens = TokenizeForSearch(processedContent?.Summary)
            });
        }

        var selected = SelectOutlineChunks(chunks, targetCount);
        var slides = new List<SlideOutlineSlide>
        {
            new()
            {
                SlideIndex = 1,
                SlideType = SlideItemType.Title,
                Heading = processedContent?.MainTopics.FirstOrDefault() ?? "Bo slide tu dong",
                Subheading = NormalizeLine(processedContent?.Summary, 220),
                Goal = "Mo boi canh va pham vi cua tai lieu",
                PreferredChunkIds = new List<string> { selected[0].ChunkId }
            }
        };

        for (var selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++)
        {
            var chunk = selected[selectedIndex];
            slides.Add(new SlideOutlineSlide
            {
                SlideIndex = slides.Count + 1,
                SlideType = GetFallbackSlideType(slides.Count, targetCount, selectedIndex),
                Heading = chunk.Label,
                Subheading = NormalizeLine(chunk.Summary, 180),
                Goal = $"Lam ro noi dung phan {chunk.Zone}",
                PreferredChunkIds = new List<string> { chunk.ChunkId }
            });
        }
        ApplyNarrativeRhythm(slides);

        return new SlideOutlineResult
        {
            Title = processedContent?.MainTopics.FirstOrDefault() ?? "Bo slide tu tai lieu",
            Subtitle = NormalizeLine(brief?.NarrativeGoal, 260) ?? NormalizeLine(processedContent?.Summary, 260) ?? "Outline du phong duoc tao tu summary va coverage map.",
            ThemeKey = NormalizeThemeKey(brief?.ThemeKey),
            Brief = NormalizeBrief(brief),
            Slides = slides.Take(targetCount).ToList()
        };
    }

    private static SlideContentResult BuildFallbackSlideContent(SlideOutlineSlide outlineSlide, SlideDeckBrief? brief, List<DocumentChunk> evidence)
    {
        var evidenceBlocks = evidence
            .SelectMany(chunk => chunk.KeyFacts.Any() ? chunk.KeyFacts : new List<string> { chunk.Summary })
            .Select(block => NormalizeLine(block, 220))
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .Cast<string>()
            .ToList();

        var blocks = NormalizeBodyBlocksForSlideType(outlineSlide.SlideType, evidenceBlocks, outlineSlide);

        if (!blocks.Any())
        {
            blocks = NormalizeBodyBlocksForSlideType(
                outlineSlide.SlideType,
                new List<string>
                {
                    outlineSlide.Goal,
                    outlineSlide.Subheading ?? outlineSlide.Heading
                },
                outlineSlide);
        }

        return new SlideContentResult
        {
            Heading = outlineSlide.Heading,
            Subheading = outlineSlide.Subheading,
            Goal = outlineSlide.Goal,
            BodyBlocks = blocks,
            SpeakerNotes = BuildSpeakerNotes(outlineSlide, evidence),
            AccentTone = NormalizeAccentTone(null, brief, outlineSlide.SlideType)
        };
    }

    private static List<DocumentChunk> GetCoverageChunks(string content, ProcessedContent? processedContent)
    {
        if (processedContent?.CoverageMap.Any() == true)
        {
            return processedContent.CoverageMap
                .OrderBy(chunk => chunk.ChunkNumber)
                .Select(chunk => new DocumentChunk
                {
                    ChunkNumber = chunk.ChunkNumber,
                    ChunkId = chunk.ChunkId,
                    Zone = chunk.Zone,
                    Label = chunk.Label,
                    Summary = chunk.Summary,
                    KeyFacts = chunk.KeyFacts,
                    EvidenceExcerpt = chunk.EvidenceExcerpt,
                    SearchTokens = DocumentCoverageMapBuilder.BuildSearchTokens(chunk)
                })
                .ToList();
        }

        return BuildCoverageChunks(content);
    }

    private static List<DocumentChunk> BuildCoverageChunks(string content)
    {
        var coverageMap = DocumentCoverageMapBuilder.Build(content, ChunkSize, ChunkOverlap);
        var chunks = new List<DocumentChunk>(coverageMap.Count);

        for (var index = 0; index < coverageMap.Count; index++)
        {
            var coverageChunk = coverageMap[index];
            chunks.Add(new DocumentChunk
            {
                ChunkNumber = coverageChunk.ChunkNumber,
                ChunkId = coverageChunk.ChunkId,
                Zone = coverageChunk.Zone,
                Label = coverageChunk.Label,
                Summary = coverageChunk.Summary,
                KeyFacts = coverageChunk.KeyFacts,
                EvidenceExcerpt = coverageChunk.EvidenceExcerpt,
                SearchTokens = DocumentCoverageMapBuilder.BuildSearchTokens(coverageChunk)
            });
        }

        return chunks;
    }

    private static string BuildAnalyzedContentBlock(ProcessedContent? processedContent)
        => processedContent == null
            ? "- No precomputed analysis."
            : $"- Language: {processedContent.Language}\n- Main topics: {string.Join(", ", processedContent.MainTopics.Take(8))}\n- Key points: {string.Join(" | ", processedContent.KeyPoints.Take(10))}\n- Summary: {processedContent.Summary}";

    private static string BuildBriefBlock(SlideDeckBrief? brief)
    {
        var normalized = NormalizeBrief(brief);
        return $"- Theme: {normalized.ThemeKey}\n- Audience: {normalized.Audience}\n- Tone: {normalized.Tone}\n- Narrative goal: {normalized.NarrativeGoal}\n- Language style: {normalized.LanguageStyle}\n- Theme direction: {DescribeTheme(normalized.ThemeKey)}";
    }

    private static string BuildCoverageMapBlock(IEnumerable<DocumentChunk> chunks)
        => string.Join(Environment.NewLine, chunks.Select(chunk => $"- {chunk.ChunkId} | zone={chunk.Zone} | label={chunk.Label} | summary={chunk.Summary}"));

    private static string BuildEvidenceBlock(IEnumerable<DocumentChunk> chunks)
        => string.Join(Environment.NewLine, chunks.Select(chunk => $"- {chunk.ChunkId} | {chunk.Label} | {chunk.EvidenceExcerpt}"));

    private static SlideDeckBrief NormalizeBrief(SlideDeckBrief? brief)
    {
        return new SlideDeckBrief
        {
            ThemeKey = NormalizeThemeKey(brief?.ThemeKey),
            Audience = NormalizeLine(brief?.Audience, 120) ?? "Sinh vien va nguoi hoc",
            Tone = NormalizeLine(brief?.Tone, 120) ?? "Ro rang, hien dai, de nho",
            NarrativeGoal = NormalizeLine(brief?.NarrativeGoal, 220) ?? "Giup nguoi doc hieu nhanh va ghi nho cac y chinh",
            LanguageStyle = NormalizeLine(brief?.LanguageStyle, 140) ?? "Tieng Viet don gian, chuyen nghiep"
        };

#pragma warning disable CS0162
        return new SlideDeckBrief
        {
            ThemeKey = NormalizeThemeKey(brief?.ThemeKey),
            Audience = NormalizeLine(brief?.Audience, 120) ?? "Sinh vien va nguoi hoc",
            Tone = NormalizeLine(brief?.Tone, 120) ?? "Rõ ràng, hiện đại, dễ nhớ",
            NarrativeGoal = NormalizeLine(brief?.NarrativeGoal, 220) ?? "Giup nguoi doc hieu nhanh va ghi nho cac y chinh",
            LanguageStyle = NormalizeLine(brief?.LanguageStyle, 140) ?? "Tieng Viet don gian, chuyen nghiep"
        };
#pragma warning restore CS0162
    }

    private static List<DocumentChunk> SelectEvidenceChunks(List<DocumentChunk> chunks, SlideOutlineSlide outlineSlide)
    {
        if (!chunks.Any())
        {
            return new List<DocumentChunk>();
        }

        var preferred = new HashSet<string>(outlineSlide.PreferredChunkIds, StringComparer.OrdinalIgnoreCase);
        var queryTokens = TokenizeForSearch($"{outlineSlide.Heading} {outlineSlide.Subheading} {outlineSlide.Goal}");

        return chunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = (preferred.Contains(chunk.ChunkId) ? 50 : 0)
                    + queryTokens.Intersect(chunk.SearchTokens, StringComparer.OrdinalIgnoreCase).Count() * 4
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Chunk.ChunkNumber)
            .Select(item => item.Chunk)
            .Take(EvidenceChunkLimit)
            .ToList();
    }

    private static List<DocumentChunk> SelectOutlineChunks(List<DocumentChunk> chunks, int targetCount)
    {
        if (chunks.Count <= Math.Max(1, targetCount - 1))
        {
            return chunks;
        }

        var result = new List<DocumentChunk>();
        var step = Math.Max(1d, (chunks.Count - 1d) / Math.Max(1, targetCount - 2));
        for (var index = 0; index < targetCount - 1; index++)
        {
            var chunkIndex = Math.Min(chunks.Count - 1, (int)Math.Round(index * step));
            var chunk = chunks[chunkIndex];
            if (result.All(existing => existing.ChunkId != chunk.ChunkId))
            {
                result.Add(chunk);
            }
        }
        return result;
    }

    private static List<string> NormalizePreferredChunkIds(List<string>? preferredChunkIds, List<DocumentChunk> chunks, int fallbackIndex)
    {
        var valid = chunks.Select(chunk => chunk.ChunkId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = preferredChunkIds?
            .Where(id => !string.IsNullOrWhiteSpace(id) && valid.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList()
            ?? new List<string>();

        if (!normalized.Any() && chunks.Any())
        {
            normalized.Add(chunks[Math.Min(chunks.Count - 1, fallbackIndex % chunks.Count)].ChunkId);
        }

        return normalized;
    }

    private static SlideItemType ParseSlideType(string? raw, bool forceTitle)
    {
        if (forceTitle)
        {
            return SlideItemType.Title;
        }

        return raw?.Trim().ToLowerInvariant() switch
        {
            "title" => SlideItemType.Title,
            "sectiondivider" => SlideItemType.SectionDivider,
            "section-divider" => SlideItemType.SectionDivider,
            "quote" => SlideItemType.Quote,
            "highlight" => SlideItemType.Highlight,
            "stat" => SlideItemType.Stat,
            _ => SlideItemType.Content
        };
    }

    private static void ApplyNarrativeRhythm(List<SlideOutlineSlide> slides)
    {
        if (!slides.Any())
        {
            return;
        }

        slides[0].SlideType = SlideItemType.Title;

        if (slides.Count > 1)
        {
            slides[1].SlideType = SlideItemType.SectionDivider;
        }

        if (slides.Count >= 4 && slides.All(slide => slide.SlideType != SlideItemType.Highlight))
        {
            slides[^2].SlideType = SlideItemType.Highlight;
        }

        if (slides.Count >= 5 && slides.All(slide => slide.SlideType != SlideItemType.Quote))
        {
            slides[slides.Count / 2].SlideType = SlideItemType.Quote;
        }

        if (slides.Count >= 6 && slides.All(slide => slide.SlideType != SlideItemType.Stat))
        {
            slides[Math.Min(slides.Count - 1, slides.Count / 2 + 1)].SlideType = SlideItemType.Stat;
        }

        for (var index = 0; index < slides.Count; index++)
        {
            slides[index].SlideIndex = index + 1;
        }
    }

    private static SlideItemType GetFallbackSlideType(int currentSlideCount, int targetCount, int selectedIndex)
    {
        if (currentSlideCount == 1)
        {
            return SlideItemType.SectionDivider;
        }

        if (selectedIndex == Math.Max(1, targetCount / 2))
        {
            return SlideItemType.Quote;
        }

        if (currentSlideCount >= targetCount - 2)
        {
            return SlideItemType.Highlight;
        }

        return selectedIndex % 3 == 2 ? SlideItemType.Stat : SlideItemType.Content;
    }

    private static List<string> NormalizeBodyBlocksForSlideType(SlideItemType slideType, List<string> blocks, SlideOutlineSlide? outlineSlide = null)
    {
        var cleaned = blocks
            .Select(block => NormalizeLine(block, 220))
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .Cast<string>()
            .ToList();

        return slideType switch
        {
            SlideItemType.Title => cleaned.Take(2).ToList(),
            SlideItemType.SectionDivider => cleaned.Take(2).ToList(),
            SlideItemType.Quote => cleaned
                .Take(2)
                .Select(block => block.StartsWith("\"", StringComparison.Ordinal) ? block : $"\"{block}\"")
                .ToList(),
            SlideItemType.Stat => cleaned
                .Take(3)
                .Select(block => block.Any(char.IsDigit) ? block : $"Diem noi bat: {block}")
                .ToList(),
            SlideItemType.Highlight => cleaned.Take(3).ToList(),
            _ => cleaned.Take(5).ToList()
        };
    }

    private static string NormalizeAccentTone(string? accentTone, SlideDeckBrief? brief, SlideItemType slideType)
    {
        var normalized = NormalizeLine(accentTone, 80);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        if (!string.IsNullOrWhiteSpace(brief?.Tone))
        {
            return brief.Tone;
        }

        return slideType == SlideItemType.SectionDivider ? "sharp" : "warm";
    }

    private static string NormalizeThemeKey(string? value)
    {
        var token = NormalizeToken(value);
        if (string.IsNullOrWhiteSpace(token))
        {
            return "editorial-sunrise";
        }

        return SupportedThemes.Contains(token) ? token : "editorial-sunrise";
    }

    private static string DescribeTheme(string themeKey)
        => NormalizeThemeKey(themeKey) switch
        {
            "midnight-signal" => "dark editorial, strong contrast, suitable for executive or strategic decks",
            "paper-mint" => "airy and soft, good for teaching and explanatory content",
            "cobalt-grid" => "structured, technical, crisp, useful for systems or process decks",
            _ => "warm editorial, premium, clean, and approachable"
        };

    private static string BuildThemeCss(string? themeKey)
        => NormalizeThemeKey(themeKey) switch
        {
            "midnight-signal" => ":root{--deck-bg:linear-gradient(180deg,#09111c,#121d2b);--deck-text:#f8fafc;--deck-muted:rgba(248,250,252,.78);--deck-soft:rgba(248,250,252,.68);--card-bg:rgba(15,23,42,.78);--card-border:rgba(148,163,184,.22);--goal-bg:rgba(96,165,250,.16);--goal-text:#bfdbfe;--notes-border:rgba(148,163,184,.2);--title-bg:linear-gradient(180deg,rgba(30,41,59,.86),rgba(15,23,42,.92));--divider-bg:linear-gradient(135deg,#1d4ed8,#0f172a);--divider-text:#eff6ff;--divider-muted:rgba(239,246,255,.78);--highlight-bg:rgba(30,41,59,.9);}",
            "paper-mint" => ":root{--deck-bg:linear-gradient(180deg,#f5fff9,#eef7ff);--deck-text:#173038;--deck-muted:#52717b;--deck-soft:#6d8790;--card-bg:rgba(255,255,255,.94);--card-border:rgba(23,48,56,.08);--goal-bg:rgba(16,185,129,.12);--goal-text:#047857;--notes-border:rgba(23,48,56,.1);--title-bg:linear-gradient(180deg,#effcf6,#ffffff);--divider-bg:linear-gradient(135deg,#0f766e,#164e63);--divider-text:#ecfeff;--divider-muted:rgba(236,254,255,.8);--highlight-bg:rgba(240,253,250,.92);}",
            "cobalt-grid" => ":root{--deck-bg:linear-gradient(180deg,#eef4ff,#f8fbff);--deck-text:#13233b;--deck-muted:#4d6480;--deck-soft:#657b95;--card-bg:rgba(255,255,255,.96);--card-border:rgba(19,35,59,.08);--goal-bg:rgba(37,99,235,.12);--goal-text:#1d4ed8;--notes-border:rgba(19,35,59,.1);--title-bg:linear-gradient(180deg,#edf3ff,#ffffff);--divider-bg:linear-gradient(135deg,#1d4ed8,#0f172a);--divider-text:#eff6ff;--divider-muted:rgba(239,246,255,.78);--highlight-bg:rgba(239,246,255,.92);}",
            _ => ":root{--deck-bg:linear-gradient(180deg,#f8efe3,#f3f6fb);--deck-text:#17212d;--deck-muted:#506074;--deck-soft:#5c6d80;--card-bg:rgba(255,255,255,.9);--card-border:rgba(23,33,45,.1);--goal-bg:rgba(214,111,61,.12);--goal-text:#8b451d;--notes-border:rgba(23,33,45,.1);--title-bg:linear-gradient(180deg,#fff4e8,#fff);--divider-bg:linear-gradient(135deg,#203f62,#18212d);--divider-text:#f5f7fb;--divider-muted:rgba(245,247,251,.82);--highlight-bg:rgba(255,247,237,.88);}"
        };

    private static string ResolveCoverageZone(int chunkNumber, int totalChunks)
    {
        if (totalChunks <= 2)
        {
            return chunkNumber == 1 ? "dau" : "cuoi";
        }
        var ratio = chunkNumber / (double)Math.Max(1, totalChunks);
        return ratio <= 0.34d ? "dau" : ratio <= 0.67d ? "giua" : "cuoi";
    }

    private static string BuildChunkLabel(string text, int chunkNumber, int totalChunks)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => Regex.Replace(item, @"\s+", " ").Trim())
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item) && !item.StartsWith("[Page", StringComparison.OrdinalIgnoreCase) && item.Length >= 12);
        return Truncate(line ?? $"Phan {chunkNumber}/{totalChunks}", 90);
    }

    private static string BuildChunkSummary(string text, List<string> keyFacts)
        => keyFacts.Any() ? Truncate(string.Join(" ", keyFacts.Take(2)), 220) : Truncate(Regex.Replace(text, @"\s+", " ").Trim(), 220);

    private static string BuildEvidenceExcerpt(string text, List<string> keyFacts)
        => keyFacts.Any() ? Truncate(string.Join(" ", keyFacts.Take(3)), 520) : Truncate(Regex.Replace(text, @"\s+", " ").Trim(), 520);

    private static List<string> ExtractHighSignalSentences(string text, int maxCount)
        => Regex.Split(text, @"(?<=[\.\?\!])\s+|\n+")
            .Select(sentence => Regex.Replace(sentence, @"\s+", " ").Trim())
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence) && !sentence.StartsWith("[Page", StringComparison.OrdinalIgnoreCase) && sentence.Length >= 18)
            .Select(sentence => new { Sentence = sentence, Score = ScoreSentence(sentence) })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Sentence.Length)
            .Select(item => Truncate(item.Sentence, 200))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToList();

    private static int ScoreSentence(string sentence)
    {
        var score = 0;
        var wordCount = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount is >= 6 and <= 28) score += 6;
        if (sentence.Any(char.IsDigit)) score += 4;
        if (sentence.Contains(':', StringComparison.Ordinal)) score += 3;
        if (sentence.Contains("la ", StringComparison.OrdinalIgnoreCase) || sentence.Contains("bao gom", StringComparison.OrdinalIgnoreCase) || sentence.Contains("buoc", StringComparison.OrdinalIgnoreCase)) score += 5;
        return score;
    }

    private static string NormalizeContent(string content)
        => string.IsNullOrWhiteSpace(content) ? string.Empty : TextCleanupUtility.NormalizeForAi(content, preserveLineBreaks: true);

    private static string? NormalizeLine(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = TextCleanupUtility.NormalizeForDisplay(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : Truncate(normalized, maxLength);
    }

    private static HashSet<string> TokenizeForSearch(string? value)
    {
        var normalized = NormalizeToken(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return normalized
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3 && !StopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is '-' or '_' or ' ' or '/' or '|')
            {
                builder.Append('-');
            }
        }

        var collapsed = builder.ToString();
        while (collapsed.Contains("--", StringComparison.Ordinal))
        {
            collapsed = collapsed.Replace("--", "-", StringComparison.Ordinal);
        }

        return collapsed.Trim('-');
    }

    private static string BuildSpeakerNotes(SlideOutlineSlide outlineSlide, List<DocumentChunk> evidence)
    {
        var refs = evidence.Any()
            ? "Mo rong bang cac chi tiet nam trong nhung doan noi dung lien quan cua tai lieu."
            : "Mo rong y nay bang noi dung goc cua tai lieu.";
        return $"Mo dau bang muc tieu: {outlineSlide.Goal}. Nhan vao 2-3 y tren slide. {refs}";
    }

    private static void AppendBodyHtml(StringBuilder builder, IReadOnlyList<string> bodyBlocks, SlideItemType slideType)
    {
        if (!bodyBlocks.Any())
        {
            builder.AppendLine("<p>Dang cho noi dung...</p>");
            return;
        }

        if (slideType == SlideItemType.Quote)
        {
            foreach (var block in bodyBlocks.Take(2))
            {
                builder.AppendLine($"<p>{Html(block)}</p>");
            }
            return;
        }

        builder.AppendLine("<ul>");
        foreach (var block in bodyBlocks)
        {
            builder.AppendLine($"<li>{Html(block)}</li>");
        }
        builder.AppendLine("</ul>");
    }

    private static List<string> GetBodyBlocks(string? bodyJson)
    {
        if (string.IsNullOrWhiteSpace(bodyJson))
        {
            return new List<string>();
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(bodyJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "...";

    private static void Report(
        IProgress<SlideGenerationProgressUpdate>? progress,
        int percent,
        string stage,
        string message,
        string? stageLabel = null,
        string? detail = null,
        int? current = null,
        int? total = null,
        string? unitLabel = null)
    {
        progress?.Report(new SlideGenerationProgressUpdate
        {
            Percent = Math.Clamp(percent, 0, 100),
            Stage = stage,
            StageLabel = stageLabel,
            Message = message,
            Detail = detail,
            Current = current,
            Total = total,
            UnitLabel = unitLabel
        });
    }

    private sealed class DocumentChunk
    {
        public int ChunkNumber { get; init; }
        public string ChunkId { get; init; } = string.Empty;
        public string Zone { get; init; } = "giua";
        public string Label { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public List<string> KeyFacts { get; init; } = new();
        public string EvidenceExcerpt { get; init; } = string.Empty;
        public HashSet<string> SearchTokens { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SlideOutlineDraft
    {
        public string? Title { get; set; }
        public string? Subtitle { get; set; }
        public string? ThemeKey { get; set; }
        public List<SlideOutlineSlideDraft>? Slides { get; set; }
    }

    private sealed class SlideOutlineSlideDraft
    {
        public int SlideIndex { get; set; }
        public string? SlideType { get; set; }
        public string? Heading { get; set; }
        public string? Subheading { get; set; }
        public string? Goal { get; set; }
        public List<string>? PreferredChunkIds { get; set; }
    }

    private sealed class SlideContentDraft
    {
        public string? Heading { get; set; }
        public string? Subheading { get; set; }
        public string? Goal { get; set; }
        public List<string>? BodyBlocks { get; set; }
        public string? SpeakerNotes { get; set; }
        public string? AccentTone { get; set; }
    }
}
