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
    private const int ChunkSize = 2200;
    private const int ChunkOverlap = 320;
    private const int EvidenceChunkLimit = 3;
    private const int PromptCoverageChunkLimit = 8;
    private const int PromptKeyFactLimit = 2;
    private const int SlideRetryLimit = 1;
    private const int SlideAutoRepairLimit = 1;
    private const int SlideRepairThreshold = 85;
    private const int SlideCompletionThreshold = 76;
    private const int PreferredEvidenceTeachabilityThreshold = 50;
    private const int MinimumFallbackEvidenceTeachabilityThreshold = 45;
    private static readonly Regex CjkTextPattern = new(@"[\u3400-\u9FFF\uF900-\uFAFF]", RegexOptions.Compiled);
    private static readonly Regex GuidLikePattern = new(@"\b[0-9a-f]{8}(?:-[0-9a-f]{4}){2,4}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PageMarkerPattern = new(@"\[?\s*page\s+\d+\s*\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> SupportedThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "editorial-sunrise",
        "midnight-signal",
        "paper-mint",
        "cobalt-grid"
    };
    private static readonly string[] GenericSlidePhrases =
    {
        "general content",
        "tổng hợp các ý chính",
        "tóm tắt nội dung chính",
        "tóm tắt nội dung",
        "nội dung chính của tài liệu",
        "nâng cao hiệu quả học tập",
        "nâng cao chất lượng học tập",
        "lam ro noi dung phan",
        "bo slide tu dong",
        "bo slide tu tai lieu",
        "dang cho noi dung",
        "noi dung se duoc cap nhat",
        "phan dau",
        "phan giua",
        "phan cuoi"
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
        var sectionPlans = await GenerateSectionPlansAsync(chunks, progress);
        var targetCount = Math.Clamp(desiredSlideCount, 5, 18);

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
                    var prompt = BuildOutlinePrompt(processedContent, brief, sectionPlans, targetCount);
                    currentDraft = await _ollamaService.GenerateStructuredResponseAsync<SlideOutlineDraft>(
                        prompt,
                        "You are a Vietnamese lesson designer. Build grounded, learner-friendly slide outlines.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error generating outline, retry/fallback will be used.");
                    qualityIssues = new List<string> { "AI khong tra ve outline hop le o luot dau." };
                }
            }

            if (currentDraft != null)
            {
                var outline = NormalizeOutlineResult(currentDraft, chunks, sectionPlans, processedContent, brief, targetCount);
                outline = await PolishOutlineAsync(processedContent, brief, chunks, sectionPlans, targetCount, outline);

                if (IsOutlineQualityAcceptable(outline, targetCount, out qualityIssues))
                {
                    return outline;
                }
            }

            if (attempt >= SlideRetryLimit)
            {
                break;
            }

            currentDraft = await RetryGenerateOutlineAsync(processedContent, brief, sectionPlans, targetCount, qualityIssues);
        }

        return BuildFallbackOutline(processedContent, brief, chunks, sectionPlans, targetCount);
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
        var sectionPlans = BuildSectionPlans(chunks);
        var evidence = SelectEvidenceChunks(chunks, outlineSlide);

        Report(progress, 20, "generate-slide", $"Đang sinh nội dung slide {slideNumber}/{totalSlides}", "Sinh slide");

        SlideContentDraft? currentDraft = null;
        var qualityIssues = new List<string>();

        for (var attempt = 0; attempt <= SlideRetryLimit; attempt++)
        {
            if (attempt == 0)
            {
                try
                {
                    var prompt = BuildSlidePrompt(processedContent, brief, outlineSlide, evidence, sectionPlans);
                    currentDraft = await _ollamaService.GenerateStructuredResponseAsync<SlideContentDraft>(
                        prompt,
                        "You create concise grounded slides. Never invent facts outside allowed evidence.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error generating slide {SlideNumber}, retry/fallback will be used.", slideNumber);
                    qualityIssues = new List<string> { "AI không trả về nội dung slide hợp lệ ở lượt đầu." };
                }
            }

            if (currentDraft != null)
            {
                var result = NormalizeSlideContent(currentDraft, outlineSlide, brief, evidence);
                result = await PolishSlideContentAsync(brief, outlineSlide, evidence, sectionPlans, result);

                if (IsSlideQualityAcceptable(result, outlineSlide.SlideType, evidence, out qualityIssues))
                {
                    await ApplySlideVerifierMetadataAsync(result, outlineSlide.SlideType, evidence, usedFallback: result.UsedFallback);
                    result = await AutoRepairSlideIfNeededAsync(processedContent, brief, outlineSlide, evidence, result);
                    result.SuggestedStatus = DetermineSuggestedSlideStatus(result, evidence);
                    return result.SuggestedStatus == SlideItemStatus.Completed
                        ? result
                        : ConvertToReviewRequiredSlideContent(result, outlineSlide);
                }
            }

            if (attempt >= SlideRetryLimit)
            {
                break;
            }

            currentDraft = await RetryGenerateSlideContentAsync(processedContent, brief, outlineSlide, evidence, sectionPlans, qualityIssues);
        }

        var fallback = BuildFallbackSlideContent(outlineSlide, brief, evidence);
        await ApplySlideVerifierMetadataAsync(fallback, outlineSlide.SlideType, evidence, usedFallback: true);
        fallback = await AutoRepairSlideIfNeededAsync(processedContent, brief, outlineSlide, evidence, fallback);
        fallback.SuggestedStatus = DetermineSuggestedSlideStatus(fallback, evidence);
        return ConvertToReviewRequiredSlideContent(fallback, outlineSlide);
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
        => $@"You are creating a short lesson deck from an educational document.

Deck brief:
{BuildBriefBlock(brief)}

Document analysis:
{BuildAnalyzedContentBlock(processedContent)}

Coverage map:
{BuildCoverageMapBlock(chunks)}

Requirements:
1. Return one JSON object only.
2. Write visible text in Vietnamese with proper diacritics.
3. Create exactly {targetCount} slides.
4. Slide 1 must be Title.
5. Include one SectionDivider in the early deck.
6. Treat the deck as a lesson flow for learners, not a document summary.
7. Give every slide a clear teaching role through its goal: hook, concept, explanation, example, comparison, takeaway, or review.
8. Use a Gamma-like lesson rhythm: open with a question/problem, build concepts step by step, add concrete evidence, end with takeaways or review.
9. Use a varied mix of slideType values chosen from Title, SectionDivider, Content, Quote, Highlight, Stat.
10. Each slide needs heading, optional subheading, short goal, and 1-3 preferredChunkIds.
11. preferredChunkIds must come exactly from the coverage map.
12. Cover early, middle, and late parts of the document.
13. Headings must be a teachable message, not raw chapter names, file names, or ""Tóm tắt nội dung chính"".
14. Avoid generic claims such as ""nâng cao hiệu quả học tập"" unless the coverage map directly supports them.
15. Do not copy OCR artifacts, CJK text, broken file names, or prompt-like wording into visible text.
16. When clear main sections exist, prefer giving each major section at least one slide through preferredChunkIds.

Return JSON:
{{
  ""title"": ""tên deck"",
  ""subtitle"": ""mô tả ngắn"",
  ""themeKey"": ""editorial-sunrise"",
  ""slides"": [
    {{
      ""slideIndex"": 1,
      ""slideType"": ""Title"",
      ""heading"": ""tiêu đề slide"",
      ""subheading"": ""phụ đề"",
      ""goal"": ""mục tiêu ngắn"",
      ""preferredChunkIds"": [""C01""]
    }}
  ]
}}";

    private static string BuildSlidePrompt(ProcessedContent? processedContent, SlideDeckBrief? brief, SlideOutlineSlide outlineSlide, List<DocumentChunk> evidence)
        => $@"You are generating one learner-facing presentation slide.

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
2. Write visible text in Vietnamese with proper diacritics.
3. Keep content concise and grounded in the evidence.
4. Match the style of a clear teacher explaining a lesson, not an academic wall of text.
5. Adapt structure to the slideType and the teaching role implied by the goal.
6. bodyBlocks must contain 2-4 short blocks for normal content slides; Title, SectionDivider, Quote may use 1-2.
7. Every body block should connect to the slide goal and include at least one concrete detail, term, event, actor, cause, contrast, or fact from the evidence.
8. speakerNotes should be 2-4 short sentences in a teacher's voice: explain what to say, why it matters, and how to transition.
9. For Title slides, bodyBlocks should frame the lesson with a question or problem.
10. For SectionDivider slides, bodyBlocks should preview what learners will understand next.
11. For Quote slides, make 1-2 impactful lines grounded in evidence.
12. For Stat slides, make each block feel like a key metric or standout fact; if there is no number, make it a concrete highlighted fact.
13. For Highlight slides, make the content memorable and takeaway-driven.
14. Avoid generic lines such as ""Tóm tắt nội dung chính của tài liệu"", ""nâng cao hiệu quả học tập"", or ""Làm rõ nội dung phần..."".
15. Do not include OCR artifacts, CJK text, broken file names, source file paths, or placeholder wording.

Return JSON:
{{
  ""heading"": ""tiêu đề slide"",
  ""subheading"": ""phụ đề"",
  ""goal"": ""mục tiêu ngắn"",
  ""bodyBlocks"": [""ý chính 1"", ""ý chính 2""],
  ""speakerNotes"": ""ghi chú trình bày"",
  ""accentTone"": ""warm""
}}";

    private static string BuildOutlinePrompt(ProcessedContent? processedContent, SlideDeckBrief? brief, List<SlideSectionPlan> sectionPlans, int targetCount)
        => $@"You are creating a short lesson deck from section summaries of an educational document.

Deck brief:
{BuildBriefBlock(brief)}

Document analysis:
{BuildAnalyzedContentBlock(processedContent)}

Section summaries:
{BuildSectionPlanBlock(sectionPlans)}

Requirements:
1. Return one JSON object only.
2. Write visible text in Vietnamese with proper diacritics.
3. Create exactly {targetCount} slides.
4. Slide 1 must be Title.
5. Include one SectionDivider in the early deck.
6. Each slide needs heading, optional subheading, short goal, keyMessage, and 1-3 preferredChunkIds.
7. preferredChunkIds must come exactly from the section summaries.
8. Cover major sections and keep a lesson flow for learners.
9. Headings and keyMessage must be concrete, not generic.
10. Respect the selected section scope only. Do not imply chapters, references, or appendices outside the supplied section summaries.
11. If mode=lecture, make the deck feel like a chapter-based lesson: chapter opening, learning objective, major subsections in order, key events or timeline when relevant, synthesis, and review.
12. If mode=summary, prioritize concise synthesis and retention.
13. If mode=exam-review, prioritize high-yield facts, comparisons, and review cues.
14. If mode=timeline, emphasize chronology, turning points, and period transitions.

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
      ""keyMessage"": ""mot y chinh ro rang"",
      ""preferredChunkIds"": [""C01""]
    }}
  ]
}}";

    private static string BuildSlidePrompt(ProcessedContent? processedContent, SlideDeckBrief? brief, SlideOutlineSlide outlineSlide, List<DocumentChunk> evidence, List<SlideSectionPlan> sectionPlans)
        => $@"You are a system creating grounded study slides from source sections.

Deck brief:
{BuildBriefBlock(brief)}

Document analysis:
{BuildAnalyzedContentBlock(processedContent)}

Slide brief:
- slideType: {outlineSlide.SlideType}
- heading: {outlineSlide.Heading}
- subheading: {outlineSlide.Subheading}
- goal: {outlineSlide.Goal}
- keyMessage: {outlineSlide.KeyMessage}
- preferredChunkIds: {string.Join(", ", outlineSlide.PreferredChunkIds)}

SOURCE_TEXT:
{BuildEvidenceBlock(evidence)}

Relevant sections:
{BuildRelevantSectionPlanBlock(sectionPlans, outlineSlide.PreferredChunkIds)}

Requirements:
- Use only information from SOURCE_TEXT.
- Do not add outside knowledge.
- Do not write generic filler.
- If SOURCE_TEXT is insufficient, write ""không đủ dữ kiện"".
- Return JSON with: title, keyMessage, bullets, evidenceFromText, speakerNotes.
- Bullets must be short and concrete.
- Keep the content inside the selected section scope and preserve the local teaching sequence of that chapter/section.
- For lecture mode, explain like a teacher guiding learners through a chapter, not like a generic summary.

Return JSON:
{{
  ""title"": ""tieu de slide"",
  ""keyMessage"": ""mot y chinh"",
  ""bullets"": [""y cu the 1"", ""y cu the 2"", ""y cu the 3""],
  ""evidenceFromText"": ""can cu ngan tu SOURCE_TEXT"",
  ""speakerNotes"": ""ghi chu trinh bay ngan""
}}";

    private static SlideOutlineResult NormalizeOutlineResult(SlideOutlineDraft? draft, List<DocumentChunk> chunks, List<SlideSectionPlan> sectionPlans, ProcessedContent? processedContent, SlideDeckBrief? brief, int targetCount)
    {
        var outline = NormalizeOutlineResult(draft, chunks, processedContent, brief, targetCount);
        ApplySectionPlanKeyMessages(outline, sectionPlans);
        return outline;
    }

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
                Goal = NormalizeLine(raw.Goal, 180) ?? BuildLessonGoal(slides.Count, heading),
                KeyMessage = NormalizeLine(raw.KeyMessage, 220),
                PreferredChunkIds = NormalizePreferredChunkIds(raw.PreferredChunkIds, chunks, slides.Count)
            });
        }

        if (!slides.Any())
        {
            return BuildFallbackOutline(processedContent, brief, chunks, targetCount);
        }

        ApplyNarrativeRhythm(slides);
        slides = RebalanceSlidesForPrimarySections(slides, chunks);

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
                    KeyMessage = fallbackSlide.KeyMessage,
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
        var sourceBlocks = draft?.Bullets?.Any() == true ? draft.Bullets : draft?.BodyBlocks;
        var blocks = sourceBlocks?
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
            Heading = NormalizeLine(draft?.Title ?? draft?.Heading, 160) ?? outlineSlide.Heading,
            Subheading = NormalizeLine(draft?.Subheading, 220) ?? outlineSlide.Subheading,
            Goal = NormalizeLine(draft?.KeyMessage ?? draft?.Goal, 180) ?? outlineSlide.KeyMessage ?? outlineSlide.Goal,
            KeyMessage = NormalizeLine(draft?.KeyMessage, 220) ?? outlineSlide.KeyMessage ?? outlineSlide.Goal,
            BodyBlocks = NormalizeBodyBlocksForSlideType(outlineSlide.SlideType, blocks, outlineSlide),
            EvidenceFromText = NormalizeLine(draft?.EvidenceFromText, 320) ?? evidence.FirstOrDefault()?.EvidenceExcerpt,
            SpeakerNotes = NormalizeLine(draft?.SpeakerNotes, 520) ?? BuildSpeakerNotes(outlineSlide, evidence),
            AccentTone = NormalizeAccentTone(draft?.AccentTone, brief, outlineSlide.SlideType),
            SuggestedStatus = SlideItemStatus.Completed,
            UsedFallback = false
        };
    }

    private async Task<SlideOutlineResult> PolishOutlineAsync(
        ProcessedContent? processedContent,
        SlideDeckBrief? brief,
        List<DocumentChunk> chunks,
        List<SlideSectionPlan> sectionPlans,
        int targetCount,
        SlideOutlineResult outline)
    {
        var polished = await PolishOutlineAsync(processedContent, brief, chunks, targetCount, outline);
        ApplySectionPlanKeyMessages(polished, sectionPlans);
        return polished;
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
4. Strengthen the lesson flow: each slide should have a teaching role such as hook, concept, explanation, example, comparison, takeaway, or review.
5. Remove OCR artifacts, raw chapter-name wording, duplicated ideas, generic claims, and machine-like phrasing.
6. Slide 1 must remain Title and at least one early slide must remain SectionDivider.
7. Do not invent facts outside the analyzed content and coverage map.

Return JSON only:
{BuildOutlineExample(targetCount)}";

            var polished = await _ollamaService.GenerateStructuredResponseAsync<SlideOutlineDraft>(
                prompt,
                "You are a Vietnamese lesson editor. Polish outlines without inventing new facts.",
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
        List<SlideSectionPlan> sectionPlans,
        SlideContentResult content)
    {
        var polished = await PolishSlideContentAsync(brief, outlineSlide, evidence, content);
        if (string.IsNullOrWhiteSpace(polished.KeyMessage))
        {
            polished.KeyMessage = outlineSlide.KeyMessage
                ?? BuildRelevantSectionPlanBlock(sectionPlans, outlineSlide.PreferredChunkIds)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
        }

        return polished;
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
3. Make the slide feel like a useful teaching moment: concrete, explanatory, and connected to the goal.
4. Remove OCR artifacts, prompt-like wording, placeholders, generic lines, and duplicated ideas.
5. Preserve the slideType structure and keep 2-4 short bodyBlocks for normal content slides.
6. speakerNotes must sound like a teacher explaining the slide and transitioning to the next idea.

Return JSON only:
{BuildSlideContentExample()}";

            var polished = await _ollamaService.GenerateStructuredResponseAsync<SlideContentDraft>(
                prompt,
                "You are a senior Vietnamese lesson editor. Polish slides without changing facts.",
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
        List<SlideSectionPlan> sectionPlans,
        int targetCount,
        IReadOnlyList<string> issues)
    {
        try
        {
            var prompt = $@"Retry the presentation outline generation from section summaries.

Deck brief:
{BuildBriefBlock(brief)}

Document analysis:
{BuildAnalyzedContentBlock(processedContent)}

Section summaries:
{BuildSectionPlanBlock(sectionPlans)}

Previous attempt issues:
- {string.Join("\n- ", issues.Where(issue => !string.IsNullOrWhiteSpace(issue)).DefaultIfEmpty("Outline chua dat quality gate"))}

Requirements:
1. Return exactly {targetCount} slides.
2. Keep the deck grounded in the listed sections only.
3. Make headings, goals, and keyMessage clean and specific in Vietnamese.
4. Include Title first, one early SectionDivider, and a varied lesson rhythm.

Return JSON only:
{BuildOutlineExample(targetCount)}";

            return await _ollamaService.GenerateStructuredResponseAsync<SlideOutlineDraft>(
                prompt,
                "You are retrying a grounded Vietnamese lesson outline. Return strict JSON only.",
                OllamaModelProfile.Generation);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrying outline generation from section summaries.");
            return null;
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
4. Assign a clear teaching role to every slide: hook, concept, explanation, example, comparison, takeaway, or review.
5. Avoid OCR artifacts, repeated headings, raw chapter labels, generic claims, and template wording.
6. Include Title first, one early SectionDivider, and a varied lesson rhythm.

Return JSON only:
{BuildOutlineExample(targetCount)}";

            return await _ollamaService.GenerateStructuredResponseAsync<SlideOutlineDraft>(
                prompt,
                "You are retrying a grounded Vietnamese lesson outline. Return strict JSON only.",
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
        List<SlideSectionPlan> sectionPlans,
        IReadOnlyList<string> issues)
    {
        try
        {
            var prompt = $@"Retry one grounded slide from source text only.

Deck brief:
{BuildBriefBlock(brief)}

Document analysis:
{BuildAnalyzedContentBlock(processedContent)}

Slide outline:
- slideType: {outlineSlide.SlideType}
- heading: {outlineSlide.Heading}
- goal: {outlineSlide.Goal}
- keyMessage: {outlineSlide.KeyMessage}
- preferredChunkIds: {string.Join(", ", outlineSlide.PreferredChunkIds)}

SOURCE_TEXT:
{BuildEvidenceBlock(evidence)}

Relevant sections:
{BuildRelevantSectionPlanBlock(sectionPlans, outlineSlide.PreferredChunkIds)}

Previous attempt issues:
- {string.Join("\n- ", issues.Where(issue => !string.IsNullOrWhiteSpace(issue)).DefaultIfEmpty("Slide chua dat quality gate"))}

Requirements:
1. Use only SOURCE_TEXT.
2. Do not add outside knowledge.
3. Rewrite generic or unsupported bullets to be more specific.
4. Return JSON only using title, keyMessage, bullets, evidenceFromText, speakerNotes.

Return JSON only:
{BuildSlideContentExample()}";

            return await _ollamaService.GenerateStructuredResponseAsync<SlideContentDraft>(
                prompt,
                "You are retrying a grounded Vietnamese lesson slide. Return strict JSON only.",
                OllamaModelProfile.Generation);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrying grounded slide content for {Heading}", outlineSlide.Heading);
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
2. Produce polished, concise Vietnamese for a clear lesson.
3. Remove placeholders, OCR artifacts, CJK text, raw wording, generic claims, and duplicate blocks.
4. Keep 2-4 short bodyBlocks for normal content slides and clear teacher-style speaker notes.
5. Each visible block must include a concrete term, actor, event, contrast, cause, or fact supported by the evidence.

Return JSON only:
{BuildSlideContentExample()}";

            return await _ollamaService.GenerateStructuredResponseAsync<SlideContentDraft>(
                prompt,
                "You are retrying a grounded Vietnamese lesson slide. Return strict JSON only.",
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
            Heading = draft.Title ?? draft.Heading ?? current.Heading,
            Subheading = draft.Subheading ?? current.Subheading,
            Goal = draft.KeyMessage ?? draft.Goal ?? current.Goal,
            KeyMessage = draft.KeyMessage ?? current.KeyMessage,
            BodyBlocks = draft.Bullets?.Any() == true ? draft.Bullets : draft.BodyBlocks?.Any() == true ? draft.BodyBlocks : current.BodyBlocks,
            EvidenceFromText = draft.EvidenceFromText ?? current.EvidenceFromText,
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
            issues.Add("Tiêu đề deck chưa sạch hoặc đang rỗng.");
        }
        else if (LooksGenericForLesson(outline.Title) || ContainsCjkText(outline.Title))
        {
            issues.Add("Tiêu đề deck con chung chung hoặc có artifact lạ.");
        }

        if (!string.IsNullOrWhiteSpace(outline.Subtitle) && TextCleanupUtility.HasNoisyArtifacts(outline.Subtitle))
        {
            issues.Add("Phụ đề deck con artifact.");
        }
        else if (!string.IsNullOrWhiteSpace(outline.Subtitle) && LooksGenericForLesson(outline.Subtitle))
        {
            issues.Add("Phụ đề deck con chung chung/template.");
        }

        if (outline.Slides.Count != targetCount)
        {
            issues.Add("Số slide chưa đúng theo yêu cầu.");
        }

        if (!outline.Slides.Any())
        {
            issues.Add("Outline không có slide nào.");
            return false;
        }

        if (outline.Slides[0].SlideType != SlideItemType.Title)
        {
            issues.Add("Slide đầu tiên chưa là Title.");
        }

        if (!outline.Slides.Skip(1).Take(Math.Min(3, Math.Max(0, outline.Slides.Count - 1))).Any(slide => slide.SlideType == SlideItemType.SectionDivider))
        {
            issues.Add("Chưa có SectionDivider nào trong deck.");
        }

        if (outline.Slides.Select(slide => NormalizeToken(slide.Heading)).Distinct(StringComparer.OrdinalIgnoreCase).Count() < Math.Max(2, outline.Slides.Count - 1))
        {
            issues.Add("Heading slide bị trùng quá nhiều.");
        }

        foreach (var slide in outline.Slides)
        {
            if (string.IsNullOrWhiteSpace(slide.Heading) || slide.Heading.Length < 6 || TextCleanupUtility.HasNoisyArtifacts(slide.Heading))
            {
                issues.Add($"Heading slide {slide.SlideIndex} chưa đạt chất lượng.");
            }
            else if (LooksGenericForLesson(slide.Heading) || ContainsCjkText(slide.Heading))
            {
                issues.Add($"Heading slide {slide.SlideIndex} còn chung chung hoặc có artifact lạ.");
            }

            if (!string.IsNullOrWhiteSpace(slide.Goal) && TextCleanupUtility.HasNoisyArtifacts(slide.Goal))
            {
                issues.Add($"Goal slide {slide.SlideIndex} con artifact.");
            }
            else if (!string.IsNullOrWhiteSpace(slide.Goal) && LooksGenericForLesson(slide.Goal))
            {
                issues.Add($"Goal slide {slide.SlideIndex} còn chung chung/template.");
            }

            if (slide.PreferredChunkIds.Count == 0)
            {
                issues.Add($"Slide {slide.SlideIndex} chưa có preferredChunkIds.");
            }
        }

        return issues.Count == 0;
    }

    private static bool IsSlideQualityAcceptable(
        SlideContentResult content,
        SlideItemType slideType,
        IReadOnlyCollection<DocumentChunk> evidence,
        out List<string> issues)
    {
        issues = new List<string>();

        if (string.IsNullOrWhiteSpace(content.Heading) || content.Heading.Length < 6 || TextCleanupUtility.HasNoisyArtifacts(content.Heading))
        {
            issues.Add("Heading slide chưa sạch hoặc quá ngắn.");
        }
        else if (LooksGenericForLesson(content.Heading))
        {
            issues.Add("Heading slide còn chung chung/template.");
        }

        if (HasBadVisibleSlideArtifacts(content))
        {
            issues.Add("Noi dung slide dang chua filename, page marker, hoac artifact khong nen hien thi.");
        }

        if (LooksLikeAuthorListOnly(content.Heading, content.Subheading, content.BodyBlocks))
        {
            issues.Add("Nội dung slide nghiêng về danh sách tác giả thay vì kiến thức học.");
        }

        if (!string.IsNullOrWhiteSpace(content.Subheading) && TextCleanupUtility.HasNoisyArtifacts(content.Subheading))
        {
            issues.Add("Subheading con artifact.");
        }
        else if (!string.IsNullOrWhiteSpace(content.Subheading) && LooksGenericForLesson(content.Subheading))
        {
            issues.Add("Subheading còn chung chung/template.");
        }

        if (!string.IsNullOrWhiteSpace(content.Goal) && TextCleanupUtility.HasNoisyArtifacts(content.Goal))
        {
            issues.Add("Goal con artifact.");
        }
        else if (!string.IsNullOrWhiteSpace(content.Goal) && LooksGenericForLesson(content.Goal))
        {
            issues.Add("Goal còn chung chung/template.");
        }

        if (!content.BodyBlocks.Any() || content.BodyBlocks.Count > 5)
        {
            issues.Add("Số body block không hợp lệ.");
        }
        else
        {
            if (content.BodyBlocks.Any(block => string.IsNullOrWhiteSpace(block) || block.Length < 6 || TextCleanupUtility.HasNoisyArtifacts(block)))
            {
                issues.Add("Body block con artifact hoặc quá ngắn.");
            }

            if (content.BodyBlocks.Any(LooksGenericForLesson))
            {
                issues.Add("Body block còn chung chung/template.");
            }

            if (content.BodyBlocks.Select(block => block.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != content.BodyBlocks.Count)
            {
                issues.Add("Body block bị trùng nhau.");
            }
        }

        if (ContainsCjkText(content.Heading)
            || ContainsCjkText(content.Subheading)
            || ContainsCjkText(content.Goal)
            || content.BodyBlocks.Any(ContainsCjkText))
        {
            issues.Add("Nội dung hiển thị còn ký tự CJK/OCR lạ.");
        }

        if (!HasEvidenceSpecificity(content, evidence))
        {
            issues.Add("Slide chưa có chi tiết cụ thể được neo vào evidence.");
        }

        if (evidence.Any() && evidence.Average(chunk => chunk.TeachabilityScore) < 45)
        {
            issues.Add("Evidence đang có teachability thấp, cần chọn lại chunk dạy học rõ hơn.");
        }

        if (evidence.Any(chunk => chunk.Classification is ChunkClassifications.FrontMatter or ChunkClassifications.TableOfContents or ChunkClassifications.Reference or ChunkClassifications.Appendix or ChunkClassifications.Noise))
        {
            issues.Add("Evidence đang chứa phần không phù hợp cho slide bài học.");
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

    private async Task ApplySlideVerifierMetadataAsync(
        SlideContentResult content,
        SlideItemType slideType,
        IReadOnlyCollection<DocumentChunk> evidence,
        bool usedFallback)
    {
        ApplyLocalSlideVerifierMetadata(content, slideType, evidence, usedFallback);

        var aiReview = await VerifySlideWithAiAsync(content, slideType, evidence);
        if (aiReview == null)
        {
            return;
        }

        var mergedIssues = content.VerifierIssues
            .Concat(aiReview.Issues ?? new List<string>())
            .Where(issue => !string.IsNullOrWhiteSpace(issue))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (aiReview.Score.HasValue)
        {
            content.VerifierScore = content.VerifierScore.HasValue
                ? Math.Min(content.VerifierScore.Value, aiReview.Score.Value)
                : aiReview.Score.Value;
        }

        if (aiReview.IsGrounded == false)
        {
            mergedIssues.Insert(0, "Verifier AI đánh dấu slide này chưa đủ grounded theo evidence được cung cấp.");
        }

        if (aiReview.RewrittenBullets?.Any() == true)
        {
            content.BodyBlocks = NormalizeBodyBlocksForSlideType(
                slideType,
                aiReview.RewrittenBullets
                    .Select(bullet => NormalizeLine(bullet, 220))
                    .Where(bullet => !string.IsNullOrWhiteSpace(bullet))
                    .Cast<string>()
                    .ToList());
        }

        content.VerifierIssues = mergedIssues;
    }

    private async Task<SlideContentResult> AutoRepairSlideIfNeededAsync(
        ProcessedContent? processedContent,
        SlideDeckBrief? brief,
        SlideOutlineSlide outlineSlide,
        List<DocumentChunk> evidence,
        SlideContentResult currentContent)
    {
        var bestContent = currentContent;

        for (var attempt = 0; attempt < SlideAutoRepairLimit; attempt++)
        {
            if (!NeedsSlideAutoRepair(bestContent))
            {
                break;
            }

            var repairIssues = bestContent.VerifierIssues
                .Concat(new[]
                {
                    "Auto-repair: hay sửa slide thành bài giảng rõ hơn, grounded hơn, cụ thể hơn, và bỏ generic/template."
                })
                .Where(issue => !string.IsNullOrWhiteSpace(issue))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();

            var repairedDraft = await RetryGenerateSlideContentAsync(processedContent, brief, outlineSlide, evidence, repairIssues);
            if (repairedDraft == null)
            {
                break;
            }

            var repairedContent = NormalizeSlideContent(repairedDraft, outlineSlide, brief, evidence);
            repairedContent = await PolishSlideContentAsync(brief, outlineSlide, evidence, repairedContent);
            if (!IsSlideQualityAcceptable(repairedContent, outlineSlide.SlideType, evidence, out _))
            {
                break;
            }

            await ApplySlideVerifierMetadataAsync(repairedContent, outlineSlide.SlideType, evidence, usedFallback: repairedContent.UsedFallback);

            if (!ShouldPreferRepairedSlide(bestContent, repairedContent))
            {
                break;
            }

            _logger.LogInformation(
                "Slide auto-repair improved heading {Heading} score from {OldScore} to {NewScore}",
                outlineSlide.Heading,
                bestContent.VerifierScore,
                repairedContent.VerifierScore);

            bestContent = repairedContent;
        }

        return bestContent;
    }

    private static bool NeedsSlideAutoRepair(SlideContentResult content)
    {
        var score = content.VerifierScore ?? 0;
        if (score < SlideRepairThreshold)
        {
            return true;
        }

        return content.VerifierIssues.Any(issue =>
            issue.Contains("grounded", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("artifact", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("dang rong", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("fallback", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("trung", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("generic", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("template", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("chung chung", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("CJK", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("chi tiet cu the", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldPreferRepairedSlide(SlideContentResult current, SlideContentResult repaired)
    {
        var currentScore = current.VerifierScore ?? 0;
        var repairedScore = repaired.VerifierScore ?? 0;

        if (repairedScore > currentScore)
        {
            return true;
        }

        if (repairedScore == currentScore)
        {
            return repaired.VerifierIssues.Count < current.VerifierIssues.Count;
        }

        return false;
    }

    private static bool LooksGenericForLesson(string? value)
    {
        var token = NormalizeToken(value);
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        return GenericSlidePhrases.Any(phrase => token.Contains(NormalizeToken(phrase), StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsCjkText(string? value)
        => !string.IsNullOrWhiteSpace(value) && CjkTextPattern.IsMatch(value);

    private static bool HasEvidenceSpecificity(SlideContentResult content, IReadOnlyCollection<DocumentChunk> evidence)
    {
        if (evidence.Count == 0)
        {
            return true;
        }

        var visibleText = string.Join(
            " ",
            new[] { content.Heading, content.Subheading, content.Goal, content.KeyMessage, content.EvidenceFromText }
                .Concat(content.BodyBlocks)
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        if (visibleText.Any(char.IsDigit))
        {
            return true;
        }

        var visibleTokens = TokenizeForSearch(visibleText);
        if (visibleTokens.Count == 0)
        {
            return false;
        }

        var evidenceTokens = evidence
            .SelectMany(chunk => TokenizeForSearch($"{chunk.Label} {chunk.Summary} {string.Join(" ", chunk.KeyFacts)} {chunk.EvidenceExcerpt}"))
            .Where(token => token.Length >= 4)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return visibleTokens.Intersect(evidenceTokens, StringComparer.OrdinalIgnoreCase).Take(3).Count() >= 3;
    }

    private static void ApplyLocalSlideVerifierMetadata(
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
            AddWarning("Slide này đang dùng đường fallback vì AI chưa trả về nội dung grounded theo yêu cầu.", 30);
        }

        if (HasBadVisibleSlideArtifacts(content))
        {
            AddWarning("Noi dung hien thi con filename, page marker, hoac artifact khong nen xuat hien.", 24);
        }

        if (string.IsNullOrWhiteSpace(content.Heading))
        {
            AddWarning("Heading slide đang rỗng.", 35);
        }
        else
        {
            if (content.Heading.Length < 10 || content.Heading.Length > 120)
            {
                AddWarning("Heading slide có độ dài chưa tối ưu.", 8);
            }

            if (TextCleanupUtility.HasNoisyArtifacts(content.Heading))
            {
                AddWarning("Heading slide còn dấu hiệu artifact hoặc wording may.", 28);
            }

            if (LooksGenericForLesson(content.Heading))
            {
                AddWarning("Heading slide còn chung chung/template.", 14);
            }
        }

        if (!string.IsNullOrWhiteSpace(content.Subheading) && TextCleanupUtility.HasNoisyArtifacts(content.Subheading))
        {
            AddWarning("Subheading con artifact.", 14);
        }
        else if (!string.IsNullOrWhiteSpace(content.Subheading) && LooksGenericForLesson(content.Subheading))
        {
            AddWarning("Subheading con chung chung/template.", 8);
        }

        if (!string.IsNullOrWhiteSpace(content.Goal) && TextCleanupUtility.HasNoisyArtifacts(content.Goal))
        {
            AddWarning("Goal slide con artifact.", 14);
        }
        else if (!string.IsNullOrWhiteSpace(content.Goal) && LooksGenericForLesson(content.Goal))
        {
            AddWarning("Goal slide con chung chung/template.", 8);
        }

        if (string.IsNullOrWhiteSpace(content.KeyMessage))
        {
            AddWarning("Key message dang rong.", 14);
        }
        else if (LooksGenericForLesson(content.KeyMessage))
        {
            AddWarning("Key message con chung chung/template.", 10);
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
                AddWarning("Mot vai body block còn artifact.", 20);
            }

            if (content.BodyBlocks.Any(LooksGenericForLesson))
            {
                AddWarning("Mot vai body block còn chung chung/template.", 14);
            }

            if (content.BodyBlocks.Select(block => block.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != content.BodyBlocks.Count)
            {
                AddWarning("Body block bị trùng nhau.", 14);
            }
        }

        if (ContainsCjkText(content.Heading)
            || ContainsCjkText(content.Subheading)
            || ContainsCjkText(content.Goal)
            || content.BodyBlocks.Any(ContainsCjkText))
        {
            AddWarning("Nội dung hiển thị còn ký tự CJK/OCR lạ.", 24);
        }

        if (!HasEvidenceSpecificity(content, evidence))
        {
            AddWarning("Slide chưa có chi tiết cụ thể được neo vào evidence.", 16);
        }

        if (evidence.Any() && evidence.Average(chunk => chunk.TeachabilityScore) < 45)
        {
            AddWarning("Evidence có teachability thấp, cần ưu tiên chunk có chất lượng dạy học cao hơn.", 14);
        }

        if (evidence.Any(chunk => chunk.Classification is ChunkClassifications.FrontMatter or ChunkClassifications.TableOfContents or ChunkClassifications.Reference or ChunkClassifications.Appendix or ChunkClassifications.Noise))
        {
            AddWarning("Evidence đang chứa front matter/reference/noise, không phù hợp làm nội dung dạy học.", 24);
        }

        if (evidence.Count < 2)
        {
            AddWarning("Slide đang dựa trên ít evidence chunk.", 5);
        }

        if (string.IsNullOrWhiteSpace(content.SpeakerNotes))
        {
            AddWarning("Speaker notes đang rỗng.", 8);
        }
        else
        {
            if (content.SpeakerNotes.Length < 40)
            {
                AddWarning("Speaker notes khá ngắn.", 6);
            }

            if (TextCleanupUtility.HasNoisyArtifacts(content.SpeakerNotes))
            {
                AddWarning("Speaker notes còn artifact.", 16);
            }
        }

        switch (slideType)
        {
            case SlideItemType.Title:
            case SlideItemType.SectionDivider:
                if (content.BodyBlocks.Count > 2)
                {
                    AddWarning("Title/SectionDivider đang mang quá nhiều body block.", 8);
                }
                break;
            case SlideItemType.Stat:
                if (content.BodyBlocks.All(block => !block.Any(char.IsDigit)))
                {
                    AddWarning("Stat slide chưa có metric/fact nổi bật rõ ràng.", 12);
                }
                break;
            case SlideItemType.Quote:
                if (content.BodyBlocks.Count > 2)
                {
                    AddWarning("Quote slide nên có ít dòng hơn để tạo điểm nhấn.", 8);
                }
                break;
        }

        content.VerifierScore = Math.Clamp(score, 0, 100);
        content.VerifierIssues = warnings;
        content.EvidenceDebug = BuildEvidenceDebugMetadata(evidence);
    }

    private static SlideEvidenceDebugMetadata BuildEvidenceDebugMetadata(IReadOnlyCollection<DocumentChunk> evidence)
    {
        return new SlideEvidenceDebugMetadata
        {
            SelectedChunks = evidence
                .Select(chunk => new SlideEvidenceDebugChunk
                {
                    ChunkId = chunk.ChunkId,
                    Classification = chunk.Classification,
                    TeachabilityScore = chunk.TeachabilityScore,
                    ReasonSelected = !string.IsNullOrWhiteSpace(chunk.SelectionReason)
                        ? chunk.SelectionReason
                        : $"Selected as {chunk.Classification} with teachability score {chunk.TeachabilityScore}."
                })
                .ToList()
        };
    }

    private static bool LooksLikeFileName(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && Regex.IsMatch(value, @"\.(pdf|docx|pptx|ppt|txt|xlsx?)\b", RegexOptions.IgnoreCase);

    private static bool LooksLikeUuidFragment(string? value)
        => !string.IsNullOrWhiteSpace(value) && GuidLikePattern.IsMatch(value);

    private static bool LooksLikePageMarker(string? value)
        => !string.IsNullOrWhiteSpace(value) && PageMarkerPattern.IsMatch(value);

    private static bool HasBadVisibleSlideArtifacts(SlideContentResult content)
    {
        var visibleValues = new[] { content.Heading, content.Subheading, content.Goal }
            .Concat(content.BodyBlocks)
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return visibleValues.Any(value =>
            LooksLikeFileName(value)
            || LooksLikeUuidFragment(value)
            || LooksLikePageMarker(value));
    }

    private static bool LooksLikeAuthorListOnly(string? heading, string? subheading, IReadOnlyCollection<string> bodyBlocks)
    {
        var text = string.Join(" ", new[] { heading, subheading }.Where(value => !string.IsNullOrWhiteSpace(value)).Concat(bodyBlocks));
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!Regex.IsMatch(text, @"\b(tac gia|tác giả|author|biên soạn|chủ biên)\b", RegexOptions.IgnoreCase))
        {
            return false;
        }

        var wordCount = Regex.Matches(text, @"\b\p{L}{2,}\b").Count;
        var commaCount = text.Count(ch => ch == ',');
        return wordCount <= 45 && commaCount >= 3;
    }

    private async Task<SlideAiVerificationResult?> VerifySlideWithAiAsync(
        SlideContentResult content,
        SlideItemType slideType,
        IReadOnlyCollection<DocumentChunk> evidence)
    {
        try
        {
            var prompt = $@"Review the grounded slide content below.

Allowed evidence only:
{BuildSlideVerifierEvidenceBlock(evidence)}

Slide payload:
- slideType: {slideType}
- heading: {content.Heading}
- subheading: {content.Subheading}
- goal: {content.Goal}
- keyMessage: {content.KeyMessage}
- bodyBlocks:
{BuildSlideVerifierBodyBlock(content.BodyBlocks)}
- evidenceFromText: {content.EvidenceFromText}
- speakerNotes: {content.SpeakerNotes}

Requirements:
1. Use only the evidence above.
2. Penalize unsupported statements, duplicated ideas, OCR artifacts, CJK text, weak clarity, missing concrete evidence detail, or presentation wording that is too generic.
3. invalidBullets should list the bullets that are unsupported or too generic.
4. rewrittenBullets should rewrite only those invalid bullets to be more specific without adding outside knowledge.
3. Score from 0 to 100.
4. issues must be short Vietnamese bullets, maximum 5 items.
5. isGrounded is true only when the visible slide content is supported by the evidence.

Return JSON only:
{{
  ""score"": 86,
  ""issues"": [""Y thu hai con hoi chung chung""],
  ""isValid"": false,
  ""invalidBullets"": [""bullet can sua""],
  ""rewrittenBullets"": [""bullet da viet lai cu the hon""],
  ""isGrounded"": true
}}";

            return await _ollamaService.GenerateStructuredResponseAsync<SlideAiVerificationResult>(
                prompt,
                "You are a strict presentation verifier. Use only the supplied evidence, never invent facts, and return concise JSON only.",
                OllamaModelProfile.Verification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI slide verifier failed. Keeping local verifier result only.");
            return null;
        }
    }

    private static string BuildSlideVerifierEvidenceBlock(IReadOnlyCollection<DocumentChunk> evidence)
    {
        if (evidence.Count == 0)
        {
            return "- Khong co evidence chunk nao.";
        }

        return string.Join(Environment.NewLine, evidence.Select(chunk =>
            $"- {chunk.ChunkId} | zone={chunk.Zone} | class={chunk.Classification} | teachability={chunk.TeachabilityScore} | reason={chunk.SelectionReason} | label={chunk.Label} | summary={chunk.Summary} | excerpt={chunk.EvidenceExcerpt}"));
    }

    private static string BuildSlideVerifierBodyBlock(IReadOnlyCollection<string> bodyBlocks)
    {
        if (bodyBlocks.Count == 0)
        {
            return "- Khong co body block";
        }

        return string.Join(Environment.NewLine, bodyBlocks.Select(block => $"- {block}"));
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
            builder.AppendLine($"  KeyMessage: {slide.KeyMessage}");
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
        builder.AppendLine($"KeyMessage: {content.KeyMessage}");
        builder.AppendLine("Body blocks:");
        foreach (var block in content.BodyBlocks)
        {
            builder.AppendLine($"- {block}");
        }
        builder.AppendLine($"EvidenceFromText: {content.EvidenceFromText}");
        builder.AppendLine($"Speaker notes: {content.SpeakerNotes}");
        builder.AppendLine($"Accent tone: {content.AccentTone}");
        return builder.ToString().Trim();
    }

    private static string BuildOutlineExample(int targetCount)
        => $@"{{
  ""title"": ""Bài học ngắn với thông điệp rõ"",
  ""subtitle"": ""Người học sẽ đi từ bối cảnh đến điểm cần ghi nhớ"",
  ""themeKey"": ""editorial-sunrise"",
  ""slides"": [
    {{
      ""slideIndex"": 1,
      ""slideType"": ""Title"",
      ""heading"": ""Vì sao chủ đề này đáng để học?"",
      ""subheading"": ""Mở vấn đề bằng một chi tiết cụ thể từ tài liệu"",
      ""goal"": ""hook: Làm người học thấy lý do cần theo dõi bài này"",
      ""preferredChunkIds"": [""C01""]
    }}
  ]
}}";

    private static string BuildSlideContentExample()
        => @"{
  ""heading"": ""Một thông điệp chính để người học ghi nhớ"",
  ""subheading"": ""Giải thích ngắn gọn bối cảnh của thông điệp"",
  ""goal"": ""explanation: Làm rõ vì sao ý này quan trọng"",
  ""bodyBlocks"": [""Chi tiết cụ thể từ evidence"", ""Ý nghĩa của chi tiết đó với bài học""],
  ""speakerNotes"": ""Mở đầu bằng câu hỏi ngắn. Giải thích chi tiết trong bullet đầu, sau đó nói vì sao nó quan trọng và chuyển sang ý tiếp theo."",
  ""accentTone"": ""warm""
}";

    private static string BuildLessonTitle(ProcessedContent? processedContent, SlideDeckBrief? brief)
    {
        var topic = NormalizeLine(processedContent?.MainTopics.FirstOrDefault(), 90);
        if (!string.IsNullOrWhiteSpace(topic) && !LooksGenericForLesson(topic) && !ContainsCjkText(topic))
        {
            return $"Hiểu nhanh: {topic}";
        }

        var goal = NormalizeLine(brief?.NarrativeGoal, 100);
        return !string.IsNullOrWhiteSpace(goal) && !LooksGenericForLesson(goal) && !ContainsCjkText(goal)
            ? $"Bài học: {goal}"
            : "Bài học trong tài liệu này";
    }

    private static string? BuildLessonSubtitle(ProcessedContent? processedContent, SlideDeckBrief? brief)
    {
        var goal = NormalizeLine(brief?.NarrativeGoal, 180);
        if (!string.IsNullOrWhiteSpace(goal) && !LooksGenericForLesson(goal) && !ContainsCjkText(goal))
        {
            return goal;
        }

        var summary = NormalizeLine(processedContent?.Summary, 180);
        return !string.IsNullOrWhiteSpace(summary) && !LooksGenericForLesson(summary) && !ContainsCjkText(summary)
            ? $"Người học sẽ nắm được: {summary}"
            : "Đi từ bối cảnh, ý chính đến điểm cần ghi nhớ.";
    }

    private static string BuildFallbackLessonHeading(DocumentChunk chunk, int selectedIndex, int targetCount)
    {
        var anchor = NormalizeLine(chunk.NormalizedHeading, 90)
            ?? NormalizeLine(chunk.Label, 90)
            ?? $"phan {selectedIndex + 1}";
        if (ContainsCjkText(anchor) || LooksGenericForLesson(anchor))
        {
            anchor = "ý chính từ tài liệu";
        }
        var role = GetLessonRole(selectedIndex, targetCount);

        return role switch
        {
            "concept" => $"Khái niệm cần nắm: {anchor}",
            "explanation" => $"Vì sao {anchor} quan trọng?",
            "example" => $"Nhìn từ ví dụ: {anchor}",
            "comparison" => $"So sánh để hiểu rõ: {anchor}",
            "review" => $"Từ {anchor}, cần nhớ điều gì?",
            _ => $"Mở vấn đề: {anchor}"
        };
    }

    private static string BuildFallbackLessonSubheading(DocumentChunk chunk, int selectedIndex)
    {
        var summary = NormalizeLine(chunk.Summary, 150);
        if (!string.IsNullOrWhiteSpace(summary) && !LooksGenericForLesson(summary) && !ContainsCjkText(summary))
        {
            return selectedIndex == 0
                ? $"Đặt nền cho bài học: {summary}"
                : $"Ý cần giải thích: {summary}";
        }

        return "Đưa người học từ ý chính đến điểm cần ghi nhớ.";
    }

    private static string BuildFallbackLessonGoal(DocumentChunk chunk, int selectedIndex, int targetCount)
    {
        var role = GetLessonRole(selectedIndex, targetCount);
        var anchor = NormalizeLine(chunk.Label, 70) ?? chunk.ChunkId;
        if (ContainsCjkText(anchor) || LooksGenericForLesson(anchor))
        {
            anchor = "ý chính từ tài liệu";
        }

        return role switch
        {
            "concept" => $"concept: Làm rõ khái niệm hoặc ý chính trong {anchor}",
            "explanation" => $"explanation: Giải thích nguyên nhân, bối cảnh hoặc tác động của {anchor}",
            "example" => $"example: Đưa chi tiết cụ thể để người học hình dung {anchor}",
            "comparison" => $"comparison: Đặt {anchor} trong tương quan để thấy điểm khác biệt",
            "review" => $"review: Chốt lại điều người học cần nhớ từ {anchor}",
            _ => $"hook: Mở vấn đề và tạo lý do để học {anchor}"
        };
    }

    private static string BuildLessonGoal(int slideIndex, string heading)
    {
        var role = GetLessonRole(slideIndex, Math.Max(5, slideIndex + 2));
        var anchor = NormalizeLine(heading, 80) ?? "ý chính";
        return $"{role}: Giúp người học hiểu {anchor}";
    }

    private static string GetLessonRole(int zeroBasedIndex, int totalSlides)
    {
        if (zeroBasedIndex <= 0)
        {
            return "hook";
        }

        if (zeroBasedIndex == 1)
        {
            return "concept";
        }

        if (zeroBasedIndex >= Math.Max(2, totalSlides - 2))
        {
            return "review";
        }

        return (zeroBasedIndex % 3) switch
        {
            0 => "explanation",
            1 => "example",
            _ => "comparison"
        };
    }

    private static SlideOutlineResult BuildFallbackOutline(ProcessedContent? processedContent, SlideDeckBrief? brief, List<DocumentChunk> chunks, List<SlideSectionPlan> sectionPlans, int targetCount)
    {
        var outline = BuildFallbackOutline(processedContent, brief, chunks, targetCount);
        ApplySectionPlanKeyMessages(outline, sectionPlans);
        return outline;
    }

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
                Heading = BuildLessonTitle(processedContent, brief),
                Subheading = BuildLessonSubtitle(processedContent, brief),
                KeyMessage = "Nguoi hoc can nhin thay cau hoi trung tam cua bai hoc ngay tu slide dau.",
                Goal = "hook: Đặt câu hỏi mở đầu để người học thấy vì sao bài này đáng học",
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
                Heading = BuildFallbackLessonHeading(chunk, selectedIndex, targetCount),
                Subheading = BuildFallbackLessonSubheading(chunk, selectedIndex),
                KeyMessage = NormalizeLine(chunk.Summary, 220) ?? NormalizeLine(chunk.Label, 160),
                Goal = BuildFallbackLessonGoal(chunk, selectedIndex, targetCount),
                PreferredChunkIds = new List<string> { chunk.ChunkId }
            });
        }
        ApplyNarrativeRhythm(slides);
        slides = RebalanceSlidesForPrimarySections(slides, chunks);

        return new SlideOutlineResult
        {
            Title = BuildLessonTitle(processedContent, brief),
            Subtitle = BuildLessonSubtitle(processedContent, brief) ?? "Bài giảng ngắn được tạo từ các ý chính trong tài liệu.",
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
            .Where(block => !LooksGenericForLesson(block) && !ContainsCjkText(block))
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
                    outlineSlide.KeyMessage ?? outlineSlide.Heading
                },
                outlineSlide);
        }

        return new SlideContentResult
        {
            Heading = outlineSlide.Heading,
            Subheading = outlineSlide.Subheading,
            Goal = outlineSlide.Goal,
            KeyMessage = outlineSlide.KeyMessage ?? outlineSlide.Goal,
            BodyBlocks = blocks,
            EvidenceFromText = NormalizeLine(evidence.FirstOrDefault()?.EvidenceExcerpt, 320)
                ?? NormalizeLine(evidence.FirstOrDefault()?.Summary, 220)
                ?? "Khong du du kien grounded",
            SpeakerNotes = BuildSpeakerNotes(outlineSlide, evidence),
            AccentTone = NormalizeAccentTone(null, brief, outlineSlide.SlideType),
            SuggestedStatus = SlideItemStatus.NeedsReview,
            UsedFallback = true
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
                    HeadingKind = chunk.HeadingKind,
                    HeadingLevel = chunk.HeadingLevel,
                    HeadingMarker = chunk.HeadingMarker,
                    HeadingText = chunk.HeadingText,
                    NormalizedHeading = chunk.NormalizedHeading,
                    HeadingPath = chunk.HeadingPath,
                    ParentHeadingPath = chunk.ParentHeadingPath,
                    SectionKey = chunk.SectionKey,
                    IsPrimarySection = chunk.IsPrimarySection,
                    Classification = chunk.Classification,
                    TeachabilityScore = chunk.TeachabilityScore,
                    SelectionReason = chunk.SelectionReason,
                    Summary = chunk.Summary,
                    KeyFacts = chunk.KeyFacts,
                    EvidenceExcerpt = chunk.EvidenceExcerpt,
                    SearchTokens = DocumentCoverageMapBuilder.BuildSearchTokens(chunk)
                })
                .Where(chunk => IsAllowedSlideEvidenceClassification(chunk.Classification) && chunk.TeachabilityScore >= 34)
                .ToList();
        }

        if (processedContent != null)
        {
            return BuildCoverageChunksFromProcessedContent(processedContent);
        }

        return BuildCoverageChunks(content);
    }

    private static List<DocumentChunk> BuildCoverageChunksFromProcessedContent(ProcessedContent processedContent)
    {
        var chunks = new List<DocumentChunk>();
        var index = 1;

        foreach (var point in processedContent.KeyPoints.Where(value => !string.IsNullOrWhiteSpace(value)).Take(8))
        {
            var normalizedPoint = NormalizeLine(point, 220);
            if (string.IsNullOrWhiteSpace(normalizedPoint))
            {
                continue;
            }

            chunks.Add(new DocumentChunk
            {
                ChunkNumber = index,
                ChunkId = $"K{index:00}",
                Zone = "giua",
                Label = normalizedPoint,
                HeadingText = processedContent.Title,
                NormalizedHeading = processedContent.DocumentType,
                SectionKey = $"processed-{index:00}",
                IsPrimarySection = index <= 3,
                Classification = ChunkClassifications.LessonContent,
                TeachabilityScore = 60,
                SelectionReason = "Selected from processed key points because stored coverage map was unavailable.",
                Summary = normalizedPoint,
                KeyFacts = new List<string> { normalizedPoint },
                EvidenceExcerpt = normalizedPoint,
                SearchTokens = DocumentCoverageMapBuilder.BuildSearchTokens(normalizedPoint)
            });

            index++;
        }

        if (!chunks.Any() && !string.IsNullOrWhiteSpace(processedContent.Summary))
        {
            var summary = NormalizeLine(processedContent.Summary, 220) ?? string.Empty;
            chunks.Add(new DocumentChunk
            {
                ChunkNumber = 1,
                ChunkId = "K01",
                Zone = "giua",
                Label = summary,
                HeadingText = processedContent.Title,
                NormalizedHeading = processedContent.DocumentType,
                SectionKey = "processed-summary",
                IsPrimarySection = true,
                Classification = ChunkClassifications.LessonContent,
                TeachabilityScore = 55,
                SelectionReason = "Selected from processed summary because key points were unavailable.",
                Summary = summary,
                KeyFacts = new List<string> { summary },
                EvidenceExcerpt = summary,
                SearchTokens = DocumentCoverageMapBuilder.BuildSearchTokens(summary)
            });
        }

        return chunks;
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
                HeadingKind = coverageChunk.HeadingKind,
                HeadingLevel = coverageChunk.HeadingLevel,
                HeadingMarker = coverageChunk.HeadingMarker,
                HeadingText = coverageChunk.HeadingText,
                NormalizedHeading = coverageChunk.NormalizedHeading,
                HeadingPath = coverageChunk.HeadingPath,
                ParentHeadingPath = coverageChunk.ParentHeadingPath,
                SectionKey = coverageChunk.SectionKey,
                IsPrimarySection = coverageChunk.IsPrimarySection,
                Classification = coverageChunk.Classification,
                TeachabilityScore = coverageChunk.TeachabilityScore,
                SelectionReason = coverageChunk.SelectionReason,
                Summary = coverageChunk.Summary,
                KeyFacts = coverageChunk.KeyFacts,
                EvidenceExcerpt = coverageChunk.EvidenceExcerpt,
                SearchTokens = DocumentCoverageMapBuilder.BuildSearchTokens(coverageChunk)
            });
        }

        return chunks;
    }

    private static string BuildAnalyzedContentBlock(ProcessedContent? processedContent)
    {
        if (processedContent == null)
        {
            return "- No precomputed analysis. Rely on coverage map and evidence only.";
        }

        var summary = NormalizeLine(processedContent.Summary, 280) ?? "Khong co tom tat san.";
        var topics = processedContent.MainTopics
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Take(5);
        var keyPoints = processedContent.KeyPoints
            .Where(point => !string.IsNullOrWhiteSpace(point))
            .Select(point => NormalizeLine(point, 110))
            .Where(point => !string.IsNullOrWhiteSpace(point))
            .Take(6);

        return $"- Language: {processedContent.Language}\n- Document type: {processedContent.DocumentType}\n- Title: {processedContent.Title}\n- Main content start page: {processedContent.MainContentStartPage}\n- Main topics: {string.Join(", ", topics)}\n- Key points: {string.Join(" | ", keyPoints)}\n- Summary: {summary}";
    }

    private static string BuildBriefBlock(SlideDeckBrief? brief)
    {
        var normalized = NormalizeBrief(brief);
        var selectedSections = normalized.SelectedSectionHeadings.Any()
            ? string.Join(" | ", normalized.SelectedSectionHeadings.Take(6))
            : string.Join(" | ", normalized.SelectedSectionIds.Take(6));
        return $"- Theme: {normalized.ThemeKey}\n- Audience: {normalized.Audience}\n- Tone: {normalized.Tone}\n- Narrative goal: {normalized.NarrativeGoal}\n- Language style: {normalized.LanguageStyle}\n- Mode: {normalized.Mode}\n- Scope policy: {normalized.ScopePolicy}\n- Selected sections: {(string.IsNullOrWhiteSpace(selectedSections) ? "current filtered scope" : selectedSections)}\n- Lesson direction: turn the source into a clear mini-lesson with hook, explanation, evidence, and takeaway\n- Theme direction: {DescribeTheme(normalized.ThemeKey)}";
    }

    private async Task<List<SlideSectionPlan>> GenerateSectionPlansAsync(
        List<DocumentChunk> chunks,
        IProgress<SlideGenerationProgressUpdate>? progress)
    {
        var plans = BuildSectionPlans(chunks);

        for (var index = 0; index < plans.Count; index++)
        {
            var current = plans[index];
            try
            {
                var prompt = $@"Summarize this section for grounded slide planning.

SOURCE_TEXT:
{current.EvidenceExcerpt}

Requirements:
- Use only SOURCE_TEXT.
- Do not invent outside knowledge.
- Return summary, keyIdeas, and learningSignificance in concise Vietnamese.

Return JSON:
{{
  ""summary"": ""tóm tắt ngắn"",
  ""keyIdeas"": [""ý 1"", ""ý 2"", ""ý 3""],
  ""learningSignificance"": ""ý nghĩa học tập ngắn""
}}";

                var draft = await _ollamaService.GenerateStructuredResponseAsync<SlideSectionSummaryDraft>(
                    prompt,
                    "You summarize one source section for slide planning. Use only the supplied source.",
                    OllamaModelProfile.Analysis);

                if (draft != null)
                {
                    current.Summary = NormalizeLine(draft.Summary, 220) ?? current.Summary;
                    current.KeyIdeas = (draft.KeyIdeas ?? current.KeyIdeas)
                        .Select(value => NormalizeLine(value, 180))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Take(4)
                        .Cast<string>()
                        .ToList();
                    current.LearningSignificance = NormalizeLine(draft.LearningSignificance, 220) ?? current.LearningSignificance;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not summarize slide section {SectionId}", current.SectionId);
            }

            Report(progress, MapProgress(12, 22, index + 1, plans.Count), "section-summaries", $"Dang tom tat section {index + 1}/{plans.Count}", "Section summaries");
            
        }

        return plans;
    }
private static int MapProgress(int startPercent, int endPercent, int currentStep, int totalSteps)
{
    var safeStart = Math.Clamp(startPercent, 0, 100);
    var safeEnd = Math.Clamp(endPercent, safeStart, 100);
    var safeTotal = Math.Max(1, totalSteps);
    var safeStep = Math.Clamp(currentStep, 0, safeTotal);

    var range = safeEnd - safeStart;
    return safeStart + (range * safeStep / safeTotal);
}
    private static List<SlideSectionPlan> BuildSectionPlans(List<DocumentChunk> chunks)
        => chunks
            .GroupBy(chunk => !string.IsNullOrWhiteSpace(chunk.SectionKey) ? chunk.SectionKey : chunk.HeadingPath ?? chunk.ChunkId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group.OrderBy(chunk => chunk.ChunkNumber).ToList();
                var first = ordered[0];
                var keyIdeas = ordered
                    .SelectMany(chunk => chunk.KeyFacts)
                    .Select(fact => NormalizeLine(fact, 180))
                    .Where(fact => !string.IsNullOrWhiteSpace(fact))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .Cast<string>()
                    .ToList();

                return new SlideSectionPlan
                {
                    SectionId = first.SectionKey ?? first.HeadingPath ?? first.ChunkId,
                    HeadingPath = first.HeadingPath,
                    HeadingText = first.HeadingText ?? first.NormalizedHeading ?? first.Label,
                    Summary = NormalizeLine(first.Summary, 220) ?? NormalizeLine(first.Label, 180) ?? first.ChunkId,
                    KeyIdeas = keyIdeas,
                    LearningSignificance = keyIdeas.FirstOrDefault() ?? NormalizeLine(first.Summary, 180) ?? "không đủ dữ kiện",
                    EvidenceExcerpt = string.Join("\n", ordered.Select(chunk => chunk.EvidenceExcerpt).Where(text => !string.IsNullOrWhiteSpace(text)).Take(3)),
                    SourceChunkIds = ordered.Select(chunk => chunk.ChunkId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    IsPrimarySection = ordered.Any(chunk => chunk.IsPrimarySection)
                };
            })
            .OrderBy(plan => plan.SourceChunkIds.FirstOrDefault())
            .ToList();

    private static string BuildSectionPlanBlock(IEnumerable<SlideSectionPlan> sectionPlans)
        => string.Join(
            Environment.NewLine,
            sectionPlans.Take(PromptCoverageChunkLimit).Select(plan =>
                $"- {string.Join(", ", plan.SourceChunkIds)} | primary={plan.IsPrimarySection} | heading={NormalizeLine(plan.HeadingText, 80) ?? plan.SectionId} | summary={NormalizeLine(plan.Summary, 160) ?? "khong co"} | ideas={string.Join(" | ", plan.KeyIdeas.Take(3))} | significance={NormalizeLine(plan.LearningSignificance, 120) ?? "khong co"}"));

    private static string BuildRelevantSectionPlanBlock(IEnumerable<SlideSectionPlan> sectionPlans, IEnumerable<string> preferredChunkIds)
    {
        var preferred = preferredChunkIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = sectionPlans
            .Where(plan => plan.SourceChunkIds.Any(preferred.Contains))
            .Take(3)
            .ToList();

        return selected.Any()
            ? BuildSectionPlanBlock(selected)
            : "- Khong co section summary trung khop.";
    }

    private static void ApplySectionPlanKeyMessages(SlideOutlineResult outline, IReadOnlyCollection<SlideSectionPlan> sectionPlans)
    {
        foreach (var slide in outline.Slides)
        {
            if (!string.IsNullOrWhiteSpace(slide.KeyMessage))
            {
                continue;
            }

            var plan = sectionPlans.FirstOrDefault(candidate => candidate.SourceChunkIds.Any(slide.PreferredChunkIds.Contains));
            slide.KeyMessage = plan?.LearningSignificance ?? plan?.Summary ?? slide.Goal;
        }
    }

    private static string BuildCoverageMapBlock(IEnumerable<DocumentChunk> chunks)
        => string.Join(
            Environment.NewLine,
            CompactPromptChunks(chunks, PromptCoverageChunkLimit)
                .Select(chunk => $"- {chunk.ChunkId} | zone={chunk.Zone} | class={chunk.Classification} | teachability={chunk.TeachabilityScore} | heading={BuildHeadingMeta(chunk)} | label={NormalizeLine(chunk.Label, 60) ?? chunk.ChunkId} | summary={NormalizeLine(chunk.Summary, 140) ?? "Khong co summary"}"));

    private static string BuildEvidenceBlock(IEnumerable<DocumentChunk> chunks)
        => string.Join(
            Environment.NewLine,
            chunks.Select(chunk =>
            {
                var facts = chunk.KeyFacts
                    .Where(fact => !string.IsNullOrWhiteSpace(fact))
                    .Select(fact => NormalizeLine(fact, 120))
                    .Where(fact => !string.IsNullOrWhiteSpace(fact))
                    .Take(PromptKeyFactLimit)
                    .ToList();

                var evidence = facts.Any()
                    ? string.Join(" | ", facts)
                    : NormalizeLine(chunk.EvidenceExcerpt, 220) ?? NormalizeLine(chunk.Summary, 160) ?? "Khong co evidence.";

                return $"- {chunk.ChunkId} | class={chunk.Classification} | teachability={chunk.TeachabilityScore} | heading={BuildHeadingMeta(chunk)} | label={NormalizeLine(chunk.Label, 60) ?? chunk.ChunkId} | {evidence}";
            }));

    private static string BuildHeadingMeta(DocumentChunk chunk)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(chunk.HeadingKind))
        {
            parts.Add(chunk.HeadingKind!);
        }

        if (chunk.HeadingLevel.HasValue)
        {
            parts.Add($"L{chunk.HeadingLevel.Value}");
        }

        if (!string.IsNullOrWhiteSpace(chunk.HeadingMarker))
        {
            parts.Add(chunk.HeadingMarker!);
        }

        if (!string.IsNullOrWhiteSpace(chunk.NormalizedHeading))
        {
            parts.Add(NormalizeLine(chunk.NormalizedHeading, 80)!);
        }
        else if (!string.IsNullOrWhiteSpace(chunk.HeadingText))
        {
            parts.Add(NormalizeLine(chunk.HeadingText, 80)!);
        }

        return parts.Any() ? string.Join(" / ", parts) : "none";
    }

    private static SlideDeckBrief NormalizeBrief(SlideDeckBrief? brief)
    {
        return new SlideDeckBrief
        {
            ThemeKey = NormalizeThemeKey(brief?.ThemeKey),
            Audience = NormalizeLine(brief?.Audience, 120) ?? "Sinh vien va nguoi hoc",
            Tone = NormalizeLine(brief?.Tone, 120) ?? "Ro rang, hien dai, de nho",
            NarrativeGoal = NormalizeLine(brief?.NarrativeGoal, 220) ?? "Giup nguoi doc hieu nhanh va ghi nho cac y chinh",
            LanguageStyle = NormalizeLine(brief?.LanguageStyle, 140) ?? "Tieng Viet don gian, chuyen nghiep",
            Mode = string.IsNullOrWhiteSpace(brief?.Mode) ? "lecture" : brief.Mode.Trim().ToLowerInvariant(),
            ScopePolicy = string.IsNullOrWhiteSpace(brief?.ScopePolicy) ? "selected-sections-only" : brief.ScopePolicy.Trim(),
            SelectedSectionIds = brief?.SelectedSectionIds?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>(),
            SelectedSectionHeadings = brief?.SelectedSectionHeadings?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => NormalizeLine(value, 120) ?? value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>()
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

        var includeExercise = ShouldUseExerciseEvidence(outlineSlide);
        var allowedChunks = chunks
            .Where(chunk => IsAllowedSlideEvidenceClassification(chunk.Classification, includeExercise))
            .Where(chunk => chunk.TeachabilityScore >= PreferredEvidenceTeachabilityThreshold)
            .ToList();

        if (!allowedChunks.Any())
        {
            allowedChunks = chunks
                .Where(chunk => IsAllowedSlideEvidenceClassification(chunk.Classification, includeExercise))
                .Where(chunk => chunk.TeachabilityScore >= MinimumFallbackEvidenceTeachabilityThreshold)
                .OrderByDescending(chunk => chunk.TeachabilityScore)
                .Take(EvidenceChunkLimit)
                .ToList();
        }

        if (!allowedChunks.Any())
        {
            return new List<DocumentChunk>();
        }

        var preferred = new HashSet<string>(outlineSlide.PreferredChunkIds, StringComparer.OrdinalIgnoreCase);
        var queryTokens = TokenizeForSearch($"{outlineSlide.Heading} {outlineSlide.Subheading} {outlineSlide.Goal} {outlineSlide.KeyMessage}");
        var preferredSections = allowedChunks
            .Where(chunk => preferred.Contains(chunk.ChunkId))
            .Select(chunk => chunk.SectionKey ?? chunk.HeadingPath)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return allowedChunks
            .Select(chunk => new
            {
                Chunk = chunk,
                Score = (preferred.Contains(chunk.ChunkId) ? 50 : 0)
                    + (!string.IsNullOrWhiteSpace(chunk.SectionKey) && preferredSections.Contains(chunk.SectionKey) ? 24 : 0)
                    + (!string.IsNullOrWhiteSpace(chunk.HeadingPath) && preferredSections.Contains(chunk.HeadingPath) ? 18 : 0)
                    + chunk.TeachabilityScore
                    + queryTokens.Intersect(chunk.SearchTokens, StringComparer.OrdinalIgnoreCase).Count() * 4
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Chunk.ChunkNumber)
            .Select(item => item.Chunk)
            .Take(EvidenceChunkLimit)
            .ToList();
    }

    private static SlideItemStatus DetermineSuggestedSlideStatus(
        SlideContentResult content,
        IReadOnlyCollection<DocumentChunk> evidence)
    {
        if (!content.BodyBlocks.Any())
        {
            return SlideItemStatus.Failed;
        }

        if (!evidence.Any())
        {
            return SlideItemStatus.Failed;
        }

        if (evidence.All(chunk => chunk.Classification is ChunkClassifications.FrontMatter or ChunkClassifications.TableOfContents or ChunkClassifications.Reference or ChunkClassifications.Appendix or ChunkClassifications.Noise))
        {
            return SlideItemStatus.Failed;
        }

        if (content.UsedFallback || HasBadVisibleSlideArtifacts(content))
        {
            return SlideItemStatus.NeedsReview;
        }

        var score = content.VerifierScore ?? 0;
        if (score < SlideCompletionThreshold)
        {
            return score <= 0 ? SlideItemStatus.Failed : SlideItemStatus.NeedsReview;
        }

        return SlideItemStatus.Completed;
    }

    private static SlideContentResult ConvertToReviewRequiredSlideContent(
        SlideContentResult source,
        SlideOutlineSlide outlineSlide)
    {
        if (source.SuggestedStatus == SlideItemStatus.Completed)
        {
            return source;
        }

        source.Heading ??= outlineSlide.Heading;
        source.Subheading ??= outlineSlide.Subheading;
        source.Goal ??= outlineSlide.Goal;
        source.KeyMessage ??= outlineSlide.KeyMessage ?? outlineSlide.Goal;

        var cleanedBlocks = source.BodyBlocks
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .Select(block => NormalizeLine(block, 180))
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .Cast<string>()
            .ToList();

        if (!cleanedBlocks.Any())
        {
            cleanedBlocks = new List<string>
            {
                outlineSlide.Goal,
                outlineSlide.KeyMessage ?? outlineSlide.Heading
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        }

        source.BodyBlocks = NormalizeBodyBlocksForSlideType(outlineSlide.SlideType, cleanedBlocks, outlineSlide);
        source.SpeakerNotes = NormalizeLine(source.SpeakerNotes, 320)
            ?? BuildSpeakerNotes(outlineSlide, new List<DocumentChunk>());
        return source;
    }

    private static bool ShouldUseExerciseEvidence(SlideOutlineSlide outlineSlide)
    {
        var cue = $"{outlineSlide.Heading} {outlineSlide.Subheading} {outlineSlide.Goal} {outlineSlide.KeyMessage}";
        return Regex.IsMatch(cue, @"\b(review|quiz|on tap|ôn tập|cau hoi|câu hỏi|kiem tra|kiểm tra|luyen tap|luyện tập|exercise)\b", RegexOptions.IgnoreCase);
    }

    private static bool IsAllowedSlideEvidenceClassification(string? classification, bool includeExercise = false)
    {
        if (string.IsNullOrWhiteSpace(classification))
        {
            return true;
        }

        if (classification == ChunkClassifications.LessonContent || classification == ChunkClassifications.Example)
        {
            return true;
        }

        return includeExercise && classification == ChunkClassifications.Exercise;
    }

    private static List<DocumentChunk> SelectOutlineChunks(List<DocumentChunk> chunks, int targetCount)
    {
        if (chunks.Count <= Math.Max(1, targetCount - 1))
        {
            return chunks;
        }

        var result = new List<DocumentChunk>();
        foreach (var chunk in GetPrimarySectionChunks(chunks).Take(Math.Max(0, targetCount - 1)))
        {
            if (result.All(existing => existing.ChunkId != chunk.ChunkId))
            {
                result.Add(chunk);
            }
        }

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

    private static List<DocumentChunk> CompactPromptChunks(IEnumerable<DocumentChunk> chunks, int limit)
    {
        var ordered = chunks
            .OrderBy(chunk => chunk.ChunkNumber)
            .ToList();

        if (ordered.Count <= limit)
        {
            return ordered;
        }

        var result = new List<DocumentChunk>();
        var step = Math.Max(1d, (ordered.Count - 1d) / Math.Max(1, limit - 1));

        for (var index = 0; index < limit; index++)
        {
            var chunkIndex = Math.Min(ordered.Count - 1, (int)Math.Round(index * step));
            var chunk = ordered[chunkIndex];
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
            normalized.Add(SelectPreferredChunkForIndex(chunks, fallbackIndex).ChunkId);
        }

        return normalized;
    }

    private static List<SlideOutlineSlide> RebalanceSlidesForPrimarySections(List<SlideOutlineSlide> slides, List<DocumentChunk> chunks)
    {
        if (slides.Count <= 1 || !chunks.Any())
        {
            return slides;
        }

        var primarySections = GetPrimarySectionChunks(chunks);
        if (!primarySections.Any())
        {
            return slides;
        }

        var coveredChunkIds = slides
            .SelectMany(slide => slide.PreferredChunkIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nonTitleSlides = slides
            .Where(slide => slide.SlideType != SlideItemType.Title)
            .ToList();

        var slideIndex = 0;
        foreach (var section in primarySections)
        {
            if (coveredChunkIds.Contains(section.ChunkId) || !nonTitleSlides.Any())
            {
                continue;
            }

            var targetSlide = nonTitleSlides[slideIndex % nonTitleSlides.Count];
            targetSlide.PreferredChunkIds = NormalizePreferredChunkIds(
                new List<string> { section.ChunkId }.Concat(targetSlide.PreferredChunkIds).ToList(),
                chunks,
                slideIndex);
            coveredChunkIds.Add(section.ChunkId);
            slideIndex++;
        }

        return slides;
    }

    private static List<DocumentChunk> GetPrimarySectionChunks(List<DocumentChunk> chunks)
        => chunks
            .Where(IsPrimarySectionChunk)
            .GroupBy(GetSectionCoverageKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(chunk => chunk.ChunkNumber).First())
            .OrderBy(chunk => chunk.ChunkNumber)
            .ToList();

    private static DocumentChunk SelectPreferredChunkForIndex(List<DocumentChunk> chunks, int index)
    {
        var primarySections = GetPrimarySectionChunks(chunks);
        if (primarySections.Any())
        {
            return primarySections[index % primarySections.Count];
        }

        return chunks[Math.Min(chunks.Count - 1, index % chunks.Count)];
    }

    private static bool IsPrimarySectionChunk(DocumentChunk chunk)
    {
        if (chunk.IsPrimarySection)
        {
            return true;
        }

        if (chunk.HeadingLevel.HasValue && chunk.HeadingLevel.Value <= 2)
        {
            return true;
        }

        return chunk.HeadingKind is "chuong" or "chapter" or "unit" or "phan" or "section";
    }

    private static string GetSectionCoverageKey(DocumentChunk chunk)
        => !string.IsNullOrWhiteSpace(chunk.SectionKey)
            ? chunk.SectionKey!
            : !string.IsNullOrWhiteSpace(chunk.HeadingPath)
            ? chunk.HeadingPath!
            : !string.IsNullOrWhiteSpace(chunk.NormalizedHeading)
                ? chunk.NormalizedHeading!
                : chunk.ChunkId;

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
            _ => cleaned.Take(4).ToList()
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
        => keyFacts.Any() ? Truncate(string.Join(" ", keyFacts.Take(2)), 420) : Truncate(Regex.Replace(text, @"\s+", " ").Trim(), 420);

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
        var concreteCue = evidence
            .SelectMany(chunk => chunk.KeyFacts.Any() ? chunk.KeyFacts : new List<string> { chunk.Summary })
            .Select(fact => NormalizeLine(fact, 140))
            .FirstOrDefault(fact => !string.IsNullOrWhiteSpace(fact) && !LooksGenericForLesson(fact) && !ContainsCjkText(fact));

        var evidenceSentence = !string.IsNullOrWhiteSpace(concreteCue)
            ? $"Dùng chi tiết này để giải thích: {concreteCue}."
            : "Nếu thiếu ví dụ cụ thể, hãy quay lại đoạn tài liệu gốc để neo ý cho người học.";

        return $"Mở đầu bằng câu hỏi gắn với mục tiêu: {outlineSlide.Goal}. Giải thích từng ý như đang giảng trên lớp, tránh đọc lại bullet. {evidenceSentence} Kết slide bằng một câu chốt để nối sang ý tiếp theo.";
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
        public string? HeadingKind { get; init; }
        public int? HeadingLevel { get; init; }
        public string? HeadingMarker { get; init; }
        public string? HeadingText { get; init; }
        public string? NormalizedHeading { get; init; }
        public string? HeadingPath { get; init; }
        public string? ParentHeadingPath { get; init; }
        public string? SectionKey { get; init; }
        public bool IsPrimarySection { get; init; }
        public string Classification { get; init; } = ChunkClassifications.LessonContent;
        public int TeachabilityScore { get; init; } = 50;
        public string SelectionReason { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public List<string> KeyFacts { get; init; } = new();
        public string EvidenceExcerpt { get; init; } = string.Empty;
        public HashSet<string> SearchTokens { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SlideSectionPlan
    {
        public string SectionId { get; set; } = string.Empty;
        public string? HeadingPath { get; set; }
        public string HeadingText { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<string> KeyIdeas { get; set; } = new();
        public string LearningSignificance { get; set; } = string.Empty;
        public string EvidenceExcerpt { get; set; } = string.Empty;
        public List<string> SourceChunkIds { get; set; } = new();
        public bool IsPrimarySection { get; set; }
    }

    private sealed class SlideSectionSummaryDraft
    {
        public string? Summary { get; set; }
        public List<string>? KeyIdeas { get; set; }
        public string? LearningSignificance { get; set; }
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
        public string? KeyMessage { get; set; }
        public List<string>? PreferredChunkIds { get; set; }
    }

    private sealed class SlideContentDraft
    {
        public string? Title { get; set; }
        public string? Heading { get; set; }
        public string? Subheading { get; set; }
        public string? Goal { get; set; }
        public string? KeyMessage { get; set; }
        public List<string>? BodyBlocks { get; set; }
        public List<string>? Bullets { get; set; }
        public string? EvidenceFromText { get; set; }
        public string? SpeakerNotes { get; set; }
        public string? AccentTone { get; set; }
    }

    private sealed class SlideAiVerificationResult
    {
        public int? Score { get; set; }
        public List<string>? Issues { get; set; }
        public bool? IsValid { get; set; }
        public List<string>? InvalidBullets { get; set; }
        public List<string>? RewrittenBullets { get; set; }
        public bool? IsGrounded { get; set; }
    }
}
