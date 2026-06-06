using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ELearnGamePlatform.API.Services.QuestionStudio;
public sealed class CanonicalQuestionGenerator : ICanonicalQuestionGenerator
{
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<CanonicalQuestionGenerator> _logger;

    public CanonicalQuestionGenerator(IOllamaService ollamaService, ILogger<CanonicalQuestionGenerator> logger)
    {
        _ollamaService = ollamaService;
        _logger = logger;
    }

    public async Task<List<QuestionDraft>> GenerateAsync(
        QuestionGenerationRun run,
        IReadOnlyCollection<QuestionSourceUnit> sourceUnits,
        IReadOnlyCollection<string> questionTypes,
        IReadOnlyCollection<string> difficulties,
        int maxDrafts,
        CancellationToken cancellationToken = default)
    {
        var profile = QuestionStudioDefaults.ResolveProfile(run.Mode);
        var drafts = new List<QuestionDraft>();
        var draftLimit = Math.Clamp(maxDrafts, 0, run.TargetDraftCount);

        foreach (var unit in sourceUnits.OrderByDescending(x => x.Confidence))
        {
            if (drafts.Count >= draftLimit)
            {
                break;
            }

            var generated = await GenerateFromAiAsync(unit, profile.CanonicalPerUnit, questionTypes, difficulties);
            if (generated.Count == 0)
            {
                generated = BuildFallbackQuestions(unit, profile.CanonicalPerUnit, questionTypes, difficulties);
            }

            foreach (var item in generated.Take(profile.CanonicalPerUnit))
            {
                if (drafts.Count >= draftLimit)
                {
                    break;
                }

                drafts.Add(QuestionStudioDraftFactory.Create(run, unit, item, "Canonical", null));
            }
        }

        return drafts;
    }

    private async Task<List<QuestionStudioAiQuestion>> GenerateFromAiAsync(
        QuestionSourceUnit unit,
        int count,
        IReadOnlyCollection<string> questionTypes,
        IReadOnlyCollection<string> difficulties)
    {
        try
        {
            var prompt = $@"Generate grounded study questions from this source unit. Use only the source.

Source unit:
{unit.Content}

Topic:
{unit.TopicTag}

Generate {count} canonical questions.
Allowed types: {string.Join(", ", questionTypes.Where(QuestionStudioDefaults.IsSupportedGenerationType))}
Allowed difficulties: {string.Join(", ", difficulties)}

Requirements:
- Treat the topic as the source section's Markdown heading path when it represents H1/H2/H3 hierarchy, and keep every question within that topic.
- Generate from this specific source section, not from a generic document summary.
- Ground every question in a specific excerpt from the source unit and copy or closely paraphrase that excerpt into sourceEvidence.
- explanation must quote a short evidence phrase or clearly paraphrase why the evidence supports the correct answer.
- Do not generate from front matter, table of contents, references/bibliography, standalone page numbers, or repeated headers and footers.
- If the source unit contains a reliable Markdown table, generate interpretation, comparison, trend, relationship, or process questions where suitable.
- If the evidence is weak, noisy, ambiguous, or insufficient, return fewer questions instead of inventing facts.
- Keep the JSON schema below unchanged.

Return JSON only:
{{
  ""questions"": [
    {{
      ""questionText"": ""..."",
      ""questionType"": ""MultipleChoice|ShortAnswer|TrueFalse|FillInTheBlank"",
      ""options"": [""A. ..."", ""B. ..."", ""C. ..."", ""D. ...""],
      ""correctAnswer"": ""A"",
      ""explanation"": ""..."",
      ""difficulty"": ""Easy|Medium|Hard"",
      ""learningObjective"": ""Remember|Understand|Apply|Analyze"",
      ""sourceEvidence"": ""...""
    }}
  ]
}}";
            var result = await _ollamaService.GenerateStructuredResponseAsync<QuestionStudioAiQuestionList>(
                prompt,
                "Return valid JSON only. Do not invent facts outside the source unit.",
                OllamaModelProfile.Generation);
            return result?.Questions?.Where(QuestionStudioDraftFactory.IsUsable).ToList() ?? new List<QuestionStudioAiQuestion>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Canonical question AI generation failed for source unit {SourceUnitId}", unit.Id);
            return new List<QuestionStudioAiQuestion>();
        }
    }

    private static List<QuestionStudioAiQuestion> BuildFallbackQuestions(
        QuestionSourceUnit unit,
        int count,
        IReadOnlyCollection<string> questionTypes,
        IReadOnlyCollection<string> difficulties)
    {
        var selectedTypes = questionTypes.Where(QuestionStudioDefaults.IsSupportedGenerationType).DefaultIfEmpty("MultipleChoice").Take(count).ToList();
        while (selectedTypes.Count < count)
        {
            selectedTypes.Add("MultipleChoice");
        }

        return selectedTypes.Select((type, index) => QuestionStudioDraftFactory.BuildDeterministicQuestion(
            type,
            difficulties.ElementAtOrDefault(index % Math.Max(1, difficulties.Count)) ?? "Medium",
            unit.Content,
            unit.TopicTag)).ToList();
    }
}

