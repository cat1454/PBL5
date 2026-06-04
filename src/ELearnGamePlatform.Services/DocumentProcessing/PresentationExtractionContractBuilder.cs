using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Models;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public static class PresentationExtractionContractBuilder
{
    private static readonly Regex WordRegex = new(@"\b[\p{L}\p{N}]+\b", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(@"(?<!\w)[-+]?\d+(?:[.,]\d+)?\s*(?:%|k|m|b|tr|ty|ty|trieu|million|billion)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LearningObjectiveRegex = new(@"\b(?:objective|goal|learning outcome|muc\s*tieu|ket\s*qua\s*hoc|sau\s*khi\s*hoc)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DefinitionRegex = new(@"\b(?:is defined as|means|refers to|definition|la\s+gi|duoc\s+dinh\s+nghia|co\s+nghia\s+la)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExampleRegex = new(@"\b(?:example|for example|case study|vi\s*du|chang\s*han)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ClaimRegex = new(@"\b(?:therefore|shows that|indicates|key finding|conclusion|cho\s*thay|ket\s*luan|vi\s*vay)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ComparisonRegex = new(@"\b(?:versus|compared with|difference|similar|compare|contrast|so\s*sanh|khac\s+nhau|giong\s+nhau)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static PresentationExtractionContract Build(
        IReadOnlyList<PageUnderstandingResult> pages,
        IReadOnlyList<DocumentRegion> regions,
        DocumentQualityScoreResult quality)
    {
        var meaningfulRegions = regions
            .Where(region => !string.IsNullOrWhiteSpace(region.Text) || !string.IsNullOrWhiteSpace(region.Description))
            .OrderBy(region => region.PageNumber)
            .ToList();
        var sections = BuildSectionPlan(pages, meaningfulRegions);
        var visuals = BuildVisualOpportunities(meaningfulRegions, sections);
        var charts = BuildChartCandidates(meaningfulRegions, sections);
        var affordances = BuildSlideAffordances(pages, meaningfulRegions, sections, visuals, charts);
        var grounding = BuildSourceGrounding(pages, meaningfulRegions, sections);
        var reviewHints = BuildUxReviewHints(pages, meaningfulRegions, charts, quality, sections);
        var warnings = BuildWarnings(meaningfulRegions, charts, quality);

        return new PresentationExtractionContract
        {
            SourceSummary = BuildSourceSummary(pages, meaningfulRegions),
            AudienceProfile = BuildAudienceProfile(pages, meaningfulRegions),
            PresentationFlow = BuildPresentationFlow(sections, affordances),
            SectionPlan = sections,
            SlideAffordances = affordances,
            SourceGrounding = grounding,
            VisualOpportunities = visuals,
            ChartCandidates = charts,
            UxReviewHints = reviewHints,
            ImageIntent = new PresentationImageIntent
            {
                ImageRendering = "vector-illustration",
                ImagePalette = "academic-blue",
                PreferredVisualRoles = visuals
                    .Select(visual => visual.VisualRole)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList()
            },
            QualityMetrics = new PresentationQualityMetrics
            {
                SectionCount = sections.Count,
                VisualOpportunityCount = visuals.Count,
                ChartCandidateCount = charts.Count,
                ReviewOnlyEvidenceCount = meaningfulRegions.Count(region => region.NeedsReview)
                    + charts.Count(chart => chart.NeedsReview),
                UxReviewHintCount = reviewHints.Count,
                DenseSectionCount = affordances.Count(affordance => string.Equals(affordance.Density, "high", StringComparison.OrdinalIgnoreCase)),
                AverageSlideabilityScore = affordances.Count == 0 ? 0d : Math.Round(affordances.Average(affordance => affordance.SlideabilityScore), 3),
                ExtractionConfidence = quality.Confidence
            },
            Warnings = warnings
        };
    }

    private static PresentationAudienceProfile BuildAudienceProfile(
        IReadOnlyList<PageUnderstandingResult> pages,
        IReadOnlyList<DocumentRegion> regions)
    {
        var allText = string.Join(" ", pages.Select(page => page.Text).Concat(regions.Select(region => region.Text)));
        var wordCount = WordRegex.Matches(allText).Count;
        var averageWordsPerPage = pages.Count == 0 ? wordCount : wordCount / Math.Max(1, pages.Count);
        var jargon = ExtractJargonTerms(allText);
        var prerequisites = regions
            .Where(region => region.RegionType is DocumentRegionTypes.Title or DocumentRegionTypes.Text)
            .SelectMany(region => ExtractConceptCandidates(region.Text))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        return new PresentationAudienceProfile
        {
            Level = jargon.Count >= 8 || averageWordsPerPage > 220 ? "intermediate" : "introductory",
            PrerequisiteConcepts = prerequisites,
            JargonTerms = jargon,
            ReadingDifficulty = ResolveReadingDifficulty(averageWordsPerPage, jargon.Count, regions.Count(region => region.NeedsReview))
        };
    }

    private static PresentationFlowPlan BuildPresentationFlow(
        IReadOnlyList<PresentationSectionPlan> sections,
        IReadOnlyList<PresentationSlideAffordance> affordances)
    {
        var first = sections.FirstOrDefault();
        var denseSections = affordances
            .Where(affordance => string.Equals(affordance.Density, "high", StringComparison.OrdinalIgnoreCase))
            .Select(affordance => affordance.SectionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new PresentationFlowPlan
        {
            SuggestedOpening = first == null
                ? "Open with the document source summary and the main learning question."
                : $"Open with {first.Heading} and state the main learning question.",
            TransitionPoints = sections
                .Skip(1)
                .Take(6)
                .Select(section => $"Move from {section.Heading} into {section.TeachingRole}.")
                .ToList(),
            RecapPoints = sections
                .TakeLast(Math.Min(3, sections.Count))
                .Select(section => $"Recap {section.Heading}.")
                .ToList(),
            SectionToSlideMap = sections
                .Select((section, index) => new PresentationSectionSlideMap
                {
                    SectionId = section.SectionId,
                    Heading = section.Heading,
                    SuggestedSlideIndex = index + 1,
                    SuggestedRole = denseSections.Contains(section.SectionId) ? "split-or-summarize" : section.TeachingRole
                })
                .ToList()
        };
    }

    private static List<PresentationSectionPlan> BuildSectionPlan(
        IReadOnlyList<PageUnderstandingResult> pages,
        IReadOnlyList<DocumentRegion> regions)
    {
        var titleRegions = regions
            .Where(region => region.RegionType == DocumentRegionTypes.Title)
            .OrderBy(region => region.PageNumber)
            .ToList();
        var plans = new List<PresentationSectionPlan>();

        if (titleRegions.Count > 0)
        {
            foreach (var title in titleRegions.Take(18))
            {
                var pageRegions = regions.Where(region => region.PageNumber == title.PageNumber).ToList();
                plans.Add(new PresentationSectionPlan
                {
                    SectionId = $"page-{title.PageNumber:000}",
                    Heading = Normalize(title.Text, 120) ?? $"Page {title.PageNumber}",
                    StartPage = title.PageNumber,
                    EndPage = title.PageNumber,
                    Rhythm = ResolveRhythm(pageRegions),
                    TeachingRole = ResolveTeachingRole(pageRegions),
                    PreferredChunkIds = new List<string> { $"P{title.PageNumber:000}" },
                    EvidenceSummary = Normalize(BuildPageEvidenceSummary(pageRegions), 260) ?? string.Empty
                });
            }
        }

        if (plans.Count == 0)
        {
            foreach (var page in pages.OrderBy(page => page.PageNumber).Take(18))
            {
                var pageRegions = regions.Where(region => region.PageNumber == page.PageNumber).ToList();
                plans.Add(new PresentationSectionPlan
                {
                    SectionId = $"page-{page.PageNumber:000}",
                    Heading = Normalize(pageRegions.FirstOrDefault(region => region.RegionType == DocumentRegionTypes.Text)?.Text, 120)
                        ?? $"Page {page.PageNumber}",
                    StartPage = page.PageNumber,
                    EndPage = page.PageNumber,
                    Rhythm = ResolveRhythm(pageRegions),
                    TeachingRole = ResolveTeachingRole(pageRegions),
                    PreferredChunkIds = new List<string> { $"P{page.PageNumber:000}" },
                    EvidenceSummary = Normalize(page.Text, 260) ?? string.Empty
                });
            }
        }

        return plans;
    }

    private static List<PresentationVisualOpportunity> BuildVisualOpportunities(
        IReadOnlyList<DocumentRegion> regions,
        IReadOnlyList<PresentationSectionPlan> sections)
        => regions
            .Where(region => region.RegionType is DocumentRegionTypes.FigureCandidate
                or DocumentRegionTypes.DiagramCandidate
                or DocumentRegionTypes.ProcessCandidate
                or DocumentRegionTypes.ChartCandidate)
            .OrderBy(region => region.PageNumber)
            .Take(24)
            .Select(region => new PresentationVisualOpportunity
            {
                PageNumber = region.PageNumber,
                SectionId = ResolveSectionId(sections, region.PageNumber),
                VisualRole = ResolveVisualRole(region),
                EvidenceText = Normalize(region.Description ?? region.Text, 360) ?? string.Empty,
                ImageRendering = "vector-illustration",
                ImagePalette = "academic-blue",
                NeedsReview = region.NeedsReview,
                ReviewReason = region.NeedsReview ? string.Join("; ", region.ReviewTags ?? new List<string>()) : null
            })
            .ToList();

    private static List<PresentationChartCandidate> BuildChartCandidates(
        IReadOnlyList<DocumentRegion> regions,
        IReadOnlyList<PresentationSectionPlan> sections)
        => regions
            .Where(region => region.RegionType == DocumentRegionTypes.ChartCandidate)
            .OrderBy(region => region.PageNumber)
            .Take(16)
            .Select(region =>
            {
                var tags = region.ReviewTags ?? new List<string>();
                var hasExplicitScale = !tags.Contains("ScaleMissing", StringComparer.OrdinalIgnoreCase);
                var hasNumericSeries = NumberRegex.Matches(region.RawText ?? region.Text).Count >= 4;
                var needsReview = region.NeedsReview || !hasExplicitScale || !hasNumericSeries;
                return new PresentationChartCandidate
                {
                    PageNumber = region.PageNumber,
                    SectionId = ResolveSectionId(sections, region.PageNumber),
                    ChartType = ResolveChartType(region.Text),
                    EvidenceText = Normalize(region.RawText ?? region.Text, 420) ?? string.Empty,
                    HasExplicitScale = hasExplicitScale,
                    HasNumericSeries = hasNumericSeries,
                    NeedsReview = needsReview,
                    ReviewReason = needsReview
                        ? BuildChartReviewReason(hasExplicitScale, hasNumericSeries, tags)
                        : null
                };
            })
            .ToList();

    private static List<PresentationSlideAffordance> BuildSlideAffordances(
        IReadOnlyList<PageUnderstandingResult> pages,
        IReadOnlyList<DocumentRegion> regions,
        IReadOnlyList<PresentationSectionPlan> sections,
        IReadOnlyList<PresentationVisualOpportunity> visuals,
        IReadOnlyList<PresentationChartCandidate> charts)
        => sections
            .Take(24)
            .Select(section =>
            {
                var pageRegions = regions.Where(region => section.StartPage <= region.PageNumber && section.EndPage >= region.PageNumber).ToList();
                var text = string.Join(" ", pageRegions.Select(region => region.RawText ?? region.Text));
                var pageText = pages.FirstOrDefault(page => page.PageNumber == section.StartPage)?.Text ?? text;
                var wordCount = WordRegex.Matches(pageText).Count;
                var visual = visuals.FirstOrDefault(item => string.Equals(item.SectionId, section.SectionId, StringComparison.OrdinalIgnoreCase));
                var chart = charts.FirstOrDefault(item => string.Equals(item.SectionId, section.SectionId, StringComparison.OrdinalIgnoreCase));
                var hasComparison = pageRegions.Any(region => ComparisonRegex.IsMatch(region.Text));
                var hasExample = pageRegions.Any(region => ExampleRegex.IsMatch(region.Text));
                var density = ResolveDensity(wordCount, pageRegions.Count);
                var layout = ResolveSuggestedLayout(pageRegions, chart, visual, hasComparison);

                return new PresentationSlideAffordance
                {
                    SectionId = section.SectionId,
                    PageNumber = section.StartPage,
                    SuggestedLayout = layout,
                    Rhythm = density == "high" ? "dense" : section.Rhythm,
                    VisualRole = visual?.VisualRole ?? (chart == null ? "none" : "diagram"),
                    ChartIntent = chart?.ChartType,
                    Density = density,
                    SlideabilityScore = ResolveSlideabilityScore(section, pageRegions, wordCount, chart),
                    SuggestedQuickActions = BuildQuickActions(density, layout, chart, hasExample)
                };
            })
            .ToList();

    private static List<PresentationSourceGrounding> BuildSourceGrounding(
        IReadOnlyList<PageUnderstandingResult> pages,
        IReadOnlyList<DocumentRegion> regions,
        IReadOnlyList<PresentationSectionPlan> sections)
        => sections
            .Take(24)
            .Select(section =>
            {
                var start = section.StartPage ?? 1;
                var end = section.EndPage ?? start;
                var pageNumbers = Enumerable.Range(start, Math.Max(1, end - start + 1)).Distinct().ToList();
                var pageConfidence = pages
                    .Where(page => pageNumbers.Contains(page.PageNumber))
                    .Select(page => page.Confidence)
                    .DefaultIfEmpty(0d)
                    .Average();
                var sectionRegions = regions.Where(region => pageNumbers.Contains(region.PageNumber)).ToList();
                var warnings = new List<string>();
                if (pageConfidence < 0.65d)
                {
                    warnings.Add("Low page confidence for this section.");
                }

                if (sectionRegions.Any(region => region.NeedsReview))
                {
                    warnings.Add("One or more evidence regions require review.");
                }

                if (string.IsNullOrWhiteSpace(section.EvidenceSummary))
                {
                    warnings.Add("Evidence summary is thin.");
                }

                return new PresentationSourceGrounding
                {
                    SectionId = section.SectionId,
                    ChunkIds = section.PreferredChunkIds.ToList(),
                    PageNumbers = pageNumbers,
                    Confidence = Math.Round(pageConfidence, 3),
                    EvidenceExcerpt = section.EvidenceSummary,
                    MissingEvidenceWarnings = warnings
                };
            })
            .ToList();

    private static List<PresentationUxReviewHint> BuildUxReviewHints(
        IReadOnlyList<PageUnderstandingResult> pages,
        IReadOnlyList<DocumentRegion> regions,
        IReadOnlyList<PresentationChartCandidate> charts,
        DocumentQualityScoreResult quality,
        IReadOnlyList<PresentationSectionPlan> sections)
    {
        var hints = new List<PresentationUxReviewHint>();

        hints.AddRange(quality.Reasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Take(6)
            .Select(reason => new PresentationUxReviewHint
            {
                Severity = quality.Confidence < 0.65d ? "high" : "medium",
                HintType = "quality",
                Message = reason.Trim(),
                SuggestedAction = "Review extracted text before using generated slides."
            }));

        hints.AddRange(pages
            .Where(page => page.Confidence < 0.65d)
            .Take(8)
            .Select(page => new PresentationUxReviewHint
            {
                Severity = page.Confidence < 0.45d ? "high" : "medium",
                HintType = "weak-ocr",
                PageNumber = page.PageNumber,
                SectionId = ResolveSectionId(sections, page.PageNumber),
                Message = $"Page {page.PageNumber} has low extraction confidence.",
                SuggestedAction = "Review OCR text or upload a clearer source page."
            }));

        hints.AddRange(regions
            .Where(region => region.NeedsReview)
            .Take(12)
            .Select(region => new PresentationUxReviewHint
            {
                Severity = region.RegionType == DocumentRegionTypes.ChartCandidate ? "high" : "medium",
                HintType = region.RegionType,
                PageNumber = region.PageNumber,
                SectionId = ResolveSectionId(sections, region.PageNumber),
                Message = $"{region.RegionType} on page {region.PageNumber} needs review.",
                SuggestedAction = ResolveRegionSuggestedAction(region.RegionType)
            }));

        hints.AddRange(charts
            .Where(chart => chart.NeedsReview)
            .Take(8)
            .Select(chart => new PresentationUxReviewHint
            {
                Severity = "high",
                HintType = "chart-review",
                PageNumber = chart.PageNumber,
                SectionId = chart.SectionId,
                Message = $"Chart candidate needs review: {chart.ReviewReason ?? "insufficient chart evidence"}.",
                SuggestedAction = "Use review badge and avoid exact chart geometry until axis/value evidence is confirmed."
            }));

        return hints
            .Where(hint => !string.IsNullOrWhiteSpace(hint.Message))
            .GroupBy(hint => $"{hint.HintType}|{hint.PageNumber}|{hint.SectionId}|{hint.Message}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(24)
            .ToList();
    }

    private static List<string> BuildWarnings(
        IReadOnlyList<DocumentRegion> regions,
        IReadOnlyList<PresentationChartCandidate> charts,
        DocumentQualityScoreResult quality)
    {
        var warnings = new List<string>();
        warnings.AddRange(quality.Reasons.Where(reason => !string.IsNullOrWhiteSpace(reason)));

        if (regions.Any(region => region.RegionType == DocumentRegionTypes.TableLowConfidence))
        {
            warnings.Add("Low-confidence table evidence is review-only for exact numbers.");
        }

        if (regions.Any(region => region.RegionType == DocumentRegionTypes.FormulaCandidate))
        {
            warnings.Add("Formula-heavy evidence requires review before exact calculation slides.");
        }

        if (charts.Any(chart => chart.NeedsReview))
        {
            warnings.Add("One or more chart candidates need review before chart geometry or exact visual encoding.");
        }

        return warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildSourceSummary(IReadOnlyList<PageUnderstandingResult> pages, IReadOnlyList<DocumentRegion> regions)
    {
        var title = regions.FirstOrDefault(region => region.RegionType == DocumentRegionTypes.Title)?.Text;
        var text = pages.OrderBy(page => page.PageNumber).FirstOrDefault(page => !string.IsNullOrWhiteSpace(page.Text))?.Text
            ?? regions.FirstOrDefault(region => !string.IsNullOrWhiteSpace(region.Text))?.Text
            ?? "Document has limited extracted evidence.";
        return Normalize(string.IsNullOrWhiteSpace(title) ? text : $"{title}: {text}", 360) ?? string.Empty;
    }

    private static string ResolveRhythm(IReadOnlyList<DocumentRegion> pageRegions)
    {
        if (pageRegions.Any(region => region.RegionType == DocumentRegionTypes.Title)
            && pageRegions.Count(region => region.RegionType != DocumentRegionTypes.HeaderFooterCandidate) <= 3)
        {
            return "anchor";
        }

        if (pageRegions.Any(region => region.RegionType is DocumentRegionTypes.TableLikeText
                or DocumentRegionTypes.TableLowConfidence
                or DocumentRegionTypes.ChartCandidate
                or DocumentRegionTypes.NumericEvidence))
        {
            return "dense";
        }

        return pageRegions.Any(region => region.RegionType is DocumentRegionTypes.FigureCandidate or DocumentRegionTypes.DiagramCandidate)
            ? "breathing"
            : "dense";
    }

    private static string ResolveTeachingRole(IReadOnlyList<DocumentRegion> pageRegions)
    {
        if (pageRegions.Any(region => region.RegionType == DocumentRegionTypes.ChartCandidate))
        {
            return "data-explanation";
        }

        if (pageRegions.Any(region => region.RegionType is DocumentRegionTypes.ProcessCandidate or DocumentRegionTypes.DiagramCandidate))
        {
            return "process-explanation";
        }

        if (pageRegions.Any(region => region.RegionType == DocumentRegionTypes.FormulaCandidate))
        {
            return "formula-review";
        }

        if (pageRegions.Any(region => LearningObjectiveRegex.IsMatch(region.Text)))
        {
            return "learning-objective";
        }

        if (pageRegions.Any(region => DefinitionRegex.IsMatch(region.Text)))
        {
            return "definition";
        }

        if (pageRegions.Any(region => ClaimRegex.IsMatch(region.Text)))
        {
            return "key-claim";
        }

        return "explanation";
    }

    private static string ResolveSuggestedLayout(
        IReadOnlyList<DocumentRegion> pageRegions,
        PresentationChartCandidate? chart,
        PresentationVisualOpportunity? visual,
        bool hasComparison)
    {
        if (chart != null)
        {
            return chart.NeedsReview ? "chart-review" : "chart";
        }

        if (hasComparison || pageRegions.Any(region => region.RegionType == DocumentRegionTypes.TableLikeText))
        {
            return "comparison";
        }

        if (pageRegions.Any(region => region.RegionType == DocumentRegionTypes.ProcessCandidate))
        {
            return "timeline-process";
        }

        if (visual != null)
        {
            return "image-led";
        }

        return pageRegions.Count <= 2 ? "title-only" : "content";
    }

    private static string ResolveDensity(int wordCount, int regionCount)
    {
        if (wordCount > 260 || regionCount >= 8)
        {
            return "high";
        }

        if (wordCount < 90 && regionCount <= 3)
        {
            return "low";
        }

        return "medium";
    }

    private static double ResolveSlideabilityScore(
        PresentationSectionPlan section,
        IReadOnlyList<DocumentRegion> pageRegions,
        int wordCount,
        PresentationChartCandidate? chart)
    {
        var score = 0.72d;
        if (!string.IsNullOrWhiteSpace(section.Heading))
        {
            score += 0.08d;
        }

        if (pageRegions.Any(region => region.RegionType is DocumentRegionTypes.ProcessCandidate or DocumentRegionTypes.DiagramCandidate or DocumentRegionTypes.FigureCandidate))
        {
            score += 0.08d;
        }

        if (chart?.NeedsReview == true || pageRegions.Any(region => region.NeedsReview))
        {
            score -= 0.18d;
        }

        if (wordCount > 320)
        {
            score -= 0.12d;
        }

        return Math.Round(Math.Clamp(score, 0.1d, 0.98d), 3);
    }

    private static List<string> BuildQuickActions(string density, string layout, PresentationChartCandidate? chart, bool hasExample)
    {
        var actions = new List<string>();
        if (density == "high")
        {
            actions.Add("split-dense-slide");
        }

        if (layout == "comparison")
        {
            actions.Add("convert-to-comparison");
        }

        if (layout is "chart" or "chart-review")
        {
            actions.Add(chart?.NeedsReview == true ? "remove-unsupported-chart-numbers" : "use-chart");
        }

        if (hasExample)
        {
            actions.Add("highlight-example");
        }

        return actions.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
    }

    private static string ResolveVisualRole(DocumentRegion region)
        => region.RegionType switch
        {
            DocumentRegionTypes.ProcessCandidate => "process",
            DocumentRegionTypes.DiagramCandidate => "diagram",
            DocumentRegionTypes.ChartCandidate => "diagram",
            DocumentRegionTypes.FigureCandidate => "object",
            _ => "conceptual"
        };

    private static string ResolveChartType(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("pie", StringComparison.Ordinal) || lower.Contains("donut", StringComparison.Ordinal))
        {
            return "pie_chart";
        }

        if (lower.Contains("trend", StringComparison.Ordinal) || lower.Contains("line", StringComparison.Ordinal))
        {
            return "line_chart";
        }

        return "bar_chart";
    }

    private static string BuildChartReviewReason(bool hasExplicitScale, bool hasNumericSeries, IReadOnlyList<string> tags)
    {
        var reasons = new List<string>();
        if (!hasExplicitScale)
        {
            reasons.Add("axis or scale is missing");
        }

        if (!hasNumericSeries)
        {
            reasons.Add("numeric series is too thin");
        }

        reasons.AddRange(tags.Where(tag => tag.Contains("Review", StringComparison.OrdinalIgnoreCase)));
        return string.Join("; ", reasons.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string ResolveRegionSuggestedAction(string regionType)
        => regionType switch
        {
            DocumentRegionTypes.ChartCandidate => "Confirm axis, scale, and values before using chart visuals.",
            DocumentRegionTypes.TableLowConfidence => "Review table rows before using exact numbers.",
            DocumentRegionTypes.FormulaCandidate => "Review notation before generating calculation slides.",
            DocumentRegionTypes.FigureCandidate or DocumentRegionTypes.DiagramCandidate => "Confirm image intent before generating visuals.",
            _ => "Review the evidence before finalizing the slide."
        };

    private static string ResolveReadingDifficulty(int averageWordsPerPage, int jargonCount, int reviewCount)
    {
        if (averageWordsPerPage > 260 || jargonCount >= 12 || reviewCount >= 8)
        {
            return "hard";
        }

        if (averageWordsPerPage < 120 && jargonCount <= 4)
        {
            return "easy";
        }

        return "medium";
    }

    private static List<string> ExtractJargonTerms(string text)
        => WordRegex.Matches(text)
            .Select(match => match.Value.Trim())
            .Where(value => value.Length >= 9 || Regex.IsMatch(value, @"[A-Z]{2,}|\d"))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .Take(12)
            .ToList();

    private static IEnumerable<string> ExtractConceptCandidates(string text)
        => Regex.Matches(text, @"\b[\p{Lu}][\p{L}\p{N}-]{3,}(?:\s+[\p{Lu}][\p{L}\p{N}-]{3,}){0,2}\b")
            .Select(match => Normalize(match.Value, 80))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>();

    private static string BuildPageEvidenceSummary(IReadOnlyList<DocumentRegion> regions)
        => string.Join(" | ", regions
            .Where(region => region.RegionType is not DocumentRegionTypes.HeaderFooterCandidate)
            .Select(region => Normalize(region.Text, 120))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(4));

    private static string ResolveSectionId(IReadOnlyList<PresentationSectionPlan> sections, int pageNumber)
        => sections.FirstOrDefault(section => section.StartPage <= pageNumber && section.EndPage >= pageNumber)?.SectionId
            ?? sections.FirstOrDefault(section => section.StartPage == pageNumber)?.SectionId
            ?? $"page-{pageNumber:000}";

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = Regex.Replace(value.ReplaceLineEndings(" "), @"\s+", " ").Trim();
        if (WordRegex.Matches(normalized).Count == 0)
        {
            return null;
        }

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }
}
