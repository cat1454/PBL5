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

namespace ELearnGamePlatform.Services.QuestionStudio;

public interface IQuestionSourceUnitExtractor
{
    Task<List<QuestionSourceUnit>> ExtractAsync(Document document, int generationRunId, CancellationToken cancellationToken = default);
}

public interface ICanonicalQuestionGenerator
{
    Task<List<QuestionDraft>> GenerateAsync(
        QuestionGenerationRun run,
        IReadOnlyCollection<QuestionSourceUnit> sourceUnits,
        IReadOnlyCollection<string> questionTypes,
        IReadOnlyCollection<string> difficulties,
        int maxDrafts,
        CancellationToken cancellationToken = default);
}

public interface IQuestionVariantGenerator
{
    Task<List<QuestionDraft>> GenerateAsync(
        QuestionGenerationRun run,
        IReadOnlyCollection<QuestionDraft> canonicalDrafts,
        IReadOnlyCollection<string> questionTypes,
        IReadOnlyCollection<string> difficulties,
        int remainingDraftBudget,
        CancellationToken cancellationToken = default);
}

public interface IQuestionDraftVerifier
{
    Task VerifyAsync(QuestionDraft draft, CancellationToken cancellationToken = default);
}

public interface IQuestionDraftDeduplicator
{
    Task<bool> IsExactDuplicateAsync(QuestionDraft draft, CancellationToken cancellationToken = default);
    Task<bool> IsNearDuplicateAsync(QuestionDraft draft, CancellationToken cancellationToken = default);
    Task MarkDuplicatesAsync(int generationRunId, CancellationToken cancellationToken = default);
}

public interface IQuestionDraftImportService
{
    Task<QuestionDraftImportResult> ImportAsync(int documentId, IReadOnlyCollection<int> draftIds, string userId, CancellationToken cancellationToken = default);
}

public sealed record QuestionDraftImportResult(int ImportedCount, int SkippedCount, IReadOnlyCollection<int> SkippedDraftIds);

public sealed class QuestionStudioOrchestrator
{
    private readonly ApplicationDbContext _context;
    private readonly IQuestionSourceUnitExtractor _sourceUnitExtractor;
    private readonly ICanonicalQuestionGenerator _canonicalQuestionGenerator;
    private readonly IQuestionVariantGenerator _variantGenerator;
    private readonly IQuestionDraftVerifier _verifier;
    private readonly IQuestionDraftDeduplicator _deduplicator;
    private readonly ILogger<QuestionStudioOrchestrator> _logger;

    public QuestionStudioOrchestrator(
        ApplicationDbContext context,
        IQuestionSourceUnitExtractor sourceUnitExtractor,
        ICanonicalQuestionGenerator canonicalQuestionGenerator,
        IQuestionVariantGenerator variantGenerator,
        IQuestionDraftVerifier verifier,
        IQuestionDraftDeduplicator deduplicator,
        ILogger<QuestionStudioOrchestrator> logger)
    {
        _context = context;
        _sourceUnitExtractor = sourceUnitExtractor;
        _canonicalQuestionGenerator = canonicalQuestionGenerator;
        _variantGenerator = variantGenerator;
        _verifier = verifier;
        _deduplicator = deduplicator;
        _logger = logger;
    }

    public async Task RunAsync(int runId, CancellationToken cancellationToken = default)
    {
        var run = await _context.QuestionGenerationRuns
            .Include(x => x.Document)
            .FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run?.Document == null)
        {
            return;
        }

        var startedAt = DateTime.UtcNow;
        try
        {
            await UpdateRunAsync(run, "Running", "ExtractingSourceUnits", cancellationToken, startedAt: startedAt);
            var questionTypes = ParseStringList(run.RequestedQuestionTypesJson, QuestionStudioDefaults.DefaultQuestionTypes);
            var difficulties = ParseStringList(run.RequestedDifficultiesJson, QuestionStudioDefaults.DefaultDifficulties);

            var sourceUnits = await _sourceUnitExtractor.ExtractAsync(run.Document, run.Id, cancellationToken);
            _context.QuestionSourceUnits.AddRange(sourceUnits);
            await _context.SaveChangesAsync(cancellationToken);
            await UpdateRunMetricsAsync(run, cancellationToken);

            await UpdateRunAsync(run, "Running", "GeneratingCanonical", cancellationToken);
            var profile = QuestionStudioDefaults.ResolveProfile(run.Mode);
            var canonicalDraftBudget = CalculateCanonicalDraftBudget(run.TargetDraftCount, profile);
            var canonicalDrafts = await _canonicalQuestionGenerator.GenerateAsync(run, sourceUnits, questionTypes, difficulties, canonicalDraftBudget, cancellationToken);
            _context.QuestionDrafts.AddRange(canonicalDrafts);
            await _context.SaveChangesAsync(cancellationToken);
            await UpdateRunMetricsAsync(run, cancellationToken);

            await UpdateRunAsync(run, "Running", "VerifyingCanonical", cancellationToken);
            foreach (var draft in canonicalDrafts)
            {
                await _verifier.VerifyAsync(draft, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);

            await UpdateRunAsync(run, "Running", "Deduplicating", cancellationToken);
            await _deduplicator.MarkDuplicatesAsync(run.Id, cancellationToken);
            await UpdateRunMetricsAsync(run, cancellationToken);

            var refreshedCanonical = await _context.QuestionDrafts
                .Where(x => x.GenerationRunId == run.Id && x.DraftKind == "Canonical" && (x.Status == "Verified" || x.Status == "Borderline"))
                .OrderByDescending(x => x.OverallScore)
                .Take(Math.Max(1, run.TargetDraftCount))
                .ToListAsync(cancellationToken);

            await UpdateRunAsync(run, "Running", "GeneratingVariants", cancellationToken);
            var remainingDraftBudget = Math.Max(0, run.TargetDraftCount - canonicalDrafts.Count);
            var variantDrafts = await _variantGenerator.GenerateAsync(run, refreshedCanonical, questionTypes, difficulties, remainingDraftBudget, cancellationToken);
            _context.QuestionDrafts.AddRange(variantDrafts);
            await _context.SaveChangesAsync(cancellationToken);

            await UpdateRunAsync(run, "Running", "VerifyingVariants", cancellationToken);
            foreach (var draft in variantDrafts)
            {
                await _verifier.VerifyAsync(draft, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);

            await UpdateRunAsync(run, "Running", "Deduplicating", cancellationToken);
            await _deduplicator.MarkDuplicatesAsync(run.Id, cancellationToken);
            await UpdateRunMetricsAsync(run, cancellationToken);

            await UpdateRunAsync(run, "Completed", "Completed", cancellationToken, completedAt: DateTime.UtcNow);
            await UpdateRunMetricsAsync(run, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Question Studio run {RunId} failed", runId);
            await UpdateRunAsync(run, "Failed", "Failed", cancellationToken, errorMessage: ex.Message, completedAt: DateTime.UtcNow);
        }
    }

    private static int CalculateCanonicalDraftBudget(int targetDraftCount, QuestionStudioProfile profile)
    {
        if (targetDraftCount <= 1 || profile.VariantsPerCanonical <= 0)
        {
            return Math.Max(1, targetDraftCount);
        }

        var seedBudget = (int)Math.Ceiling(targetDraftCount / (double)(profile.VariantsPerCanonical + 1));
        return Math.Clamp(seedBudget, 1, targetDraftCount);
    }

    private async Task UpdateRunAsync(
        QuestionGenerationRun run,
        string status,
        string stage,
        CancellationToken cancellationToken,
        DateTime? startedAt = null,
        DateTime? completedAt = null,
        string? errorMessage = null)
    {
        run.Status = status;
        run.Stage = stage;
        if (startedAt.HasValue)
        {
            run.StartedAt = startedAt;
        }

        if (completedAt.HasValue)
        {
            run.CompletedAt = completedAt;
        }

        if (errorMessage != null)
        {
            run.ErrorMessage = errorMessage;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task UpdateRunMetricsAsync(QuestionGenerationRun run, CancellationToken cancellationToken)
    {
        var drafts = await _context.QuestionDrafts
            .Where(x => x.GenerationRunId == run.Id)
            .ToListAsync(cancellationToken);
        var sourceUnitCount = await _context.QuestionSourceUnits
            .CountAsync(x => x.GenerationRunId == run.Id, cancellationToken);

        run.GeneratedDraftCount = drafts.Count;
        run.VerifiedDraftCount = drafts.Count(x => x.Status == "Verified");
        run.DuplicateCount = drafts.Count(x => x.DuplicateScore < 1.0 || x.FailureReason.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        run.RejectedCount = drafts.Count(x => x.Status == "Rejected");
        run.BorderlineCount = drafts.Count(x => x.Status == "Borderline");
        run.QuarantinedCount = drafts.Count(x => x.Status == "Quarantined");
        run.ImportedCount = drafts.Count(x => x.Status == "Imported");
        run.MetricsJson = JsonSerializer.Serialize(new
        {
            sourceUnitCount,
            canonicalCount = drafts.Count(x => x.DraftKind == "Canonical"),
            variantCount = drafts.Count(x => x.DraftKind == "Variant"),
            run.GeneratedDraftCount,
            run.VerifiedDraftCount,
            run.DuplicateCount,
            run.RejectedCount,
            run.BorderlineCount,
            run.QuarantinedCount,
            run.ImportedCount,
            verifierPassRate = drafts.Count == 0 ? 0 : Math.Round(drafts.Count(x => x.Status == "Verified" || x.Status == "Borderline") / (double)drafts.Count, 4),
            averageScore = drafts.Count == 0 ? 0 : Math.Round(drafts.Average(x => x.OverallScore), 4)
        });
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static List<string> ParseStringList(string? json, IReadOnlyCollection<string> fallback)
    {
        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(json ?? "[]")?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();
            return values?.Count > 0 ? values : fallback.ToList();
        }
        catch
        {
            return fallback.ToList();
        }
    }
}

public sealed class QuestionSourceUnitExtractor : IQuestionSourceUnitExtractor
{
    private const int ChunkSize = 3600;
    private const int ChunkOverlap = 450;

    public Task<List<QuestionSourceUnit>> ExtractAsync(Document document, int generationRunId, CancellationToken cancellationToken = default)
    {
        var units = new List<QuestionSourceUnit>();
        var topics = document.GetMainTopics();
        var keyPoints = document.GetKeyPoints();

        foreach (var keyPoint in keyPoints.Take(20))
        {
            AddUnit(units, document.Id, generationRunId, "SummaryPoint", keyPoint, ResolveTopic(topics, keyPoint), 0, keyPoint.Length);
        }

        var text = NormalizeWhitespace(document.ExtractedText ?? string.Empty);
        foreach (var (chunk, start, end) in SplitChunks(text, ChunkSize, ChunkOverlap).Take(40))
        {
            var sentences = Regex.Split(chunk, @"(?<=[.!?])\s+|\n+")
                .Select(NormalizeWhitespace)
                .Where(x => x.Length >= 45)
                .OrderByDescending(ScoreSentence)
                .Take(4)
                .ToList();

            foreach (var sentence in sentences)
            {
                AddUnit(units, document.Id, generationRunId, ClassifyUnit(sentence), sentence, ResolveTopic(topics, sentence), start, end);
            }
        }

        if (units.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            AddUnit(units, document.Id, generationRunId, "SummaryPoint", Truncate(text, 700), ResolveTopic(topics, text), 0, Math.Min(text.Length, 700));
        }

        return Task.FromResult(units
            .GroupBy(x => x.SourceHash)
            .Select(x => x.First())
            .Take(80)
            .ToList());
    }

    private static void AddUnit(List<QuestionSourceUnit> units, int documentId, int generationRunId, string unitType, string content, string topicTag, int start, int end)
    {
        var normalized = NormalizeWhitespace(content);
        if (normalized.Length < 24)
        {
            return;
        }

        units.Add(new QuestionSourceUnit
        {
            DocumentId = documentId,
            GenerationRunId = generationRunId,
            UnitType = unitType,
            Content = Truncate(normalized, 1100),
            TopicTag = Truncate(NormalizeTag(topicTag), 180),
            SourceHash = QuestionStudioText.Hash(normalized),
            StartOffset = Math.Max(0, start),
            EndOffset = Math.Max(start, end),
            Confidence = EstimateConfidence(normalized),
            MetadataJson = JsonSerializer.Serialize(new { length = normalized.Length })
        });
    }

    private static string ClassifyUnit(string value)
    {
        var lower = value.ToLowerInvariant();
        if (Regex.IsMatch(value, @"[\uFFFD]{1,}|[^\w\s.,;:?!()\-/\[\]\p{L}\p{N}]{4,}"))
        {
            return "OCRRisk";
        }

        if (lower.Contains(" la ") || lower.Contains(" is "))
        {
            return "Definition";
        }

        if (lower.Contains(" buoc ") || lower.Contains(" quy trinh ") || lower.Contains(" process "))
        {
            return "Process";
        }

        if (lower.Contains(" so sanh ") || lower.Contains(" khac ") || lower.Contains(" compare "))
        {
            return "Comparison";
        }

        return "Concept";
    }

    private static double EstimateConfidence(string value)
    {
        var noisy = Regex.Matches(value, @"[\uFFFD]|[^\w\s.,;:?!()\-/\[\]\p{L}\p{N}]").Count;
        var ratio = value.Length == 0 ? 0 : noisy / (double)value.Length;
        return Math.Clamp(1.0 - (ratio * 5), 0.25, 1.0);
    }

    private static string ResolveTopic(IReadOnlyList<string> topics, string content)
        => topics.FirstOrDefault(topic => content.Contains(topic, StringComparison.OrdinalIgnoreCase))
            ?? topics.FirstOrDefault()
            ?? "general";

    private static int ScoreSentence(string sentence)
    {
        var score = 0;
        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (words is >= 8 and <= 40) score += 8;
        if (sentence.Contains(':')) score += 4;
        if (sentence.Any(char.IsDigit)) score += 3;
        if (sentence.Contains(" la ", StringComparison.OrdinalIgnoreCase)) score += 3;
        return score;
    }

    private static IEnumerable<(string Chunk, int Start, int End)> SplitChunks(string content, int chunkSize, int overlap)
    {
        var start = 0;
        while (start < content.Length)
        {
            var end = Math.Min(content.Length, start + chunkSize);
            var chunk = content[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                yield return (chunk, start, end);
            }

            if (end >= content.Length)
            {
                break;
            }

            start = Math.Max(start + 1, end - overlap);
        }
    }

    private static string NormalizeTag(string value)
        => Regex.Replace(NormalizeWhitespace(value).ToLowerInvariant(), @"[^\p{L}\p{N}]+", "-").Trim('-');

    private static string NormalizeWhitespace(string value)
        => Regex.Replace(value.Replace('\u00A0', ' '), @"\s+", " ").Trim();

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
}

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

public sealed class QuestionVariantGenerator : IQuestionVariantGenerator
{
    public Task<List<QuestionDraft>> GenerateAsync(
        QuestionGenerationRun run,
        IReadOnlyCollection<QuestionDraft> canonicalDrafts,
        IReadOnlyCollection<string> questionTypes,
        IReadOnlyCollection<string> difficulties,
        int remainingDraftBudget,
        CancellationToken cancellationToken = default)
    {
        var profile = QuestionStudioDefaults.ResolveProfile(run.Mode);
        var drafts = new List<QuestionDraft>();
        var remaining = Math.Clamp(remainingDraftBudget, 0, run.TargetDraftCount);

        foreach (var canonical in canonicalDrafts.OrderByDescending(x => x.OverallScore))
        {
            if (remaining <= 0)
            {
                break;
            }

            foreach (var type in questionTypes.DefaultIfEmpty("MultipleChoice"))
            {
                if (remaining <= 0 || drafts.Count(x => x.ParentDraftId == canonical.Id) >= profile.VariantsPerCanonical)
                {
                    break;
                }

                var difficulty = difficulties.ElementAtOrDefault(drafts.Count % Math.Max(1, difficulties.Count)) ?? canonical.Difficulty;
                var item = QuestionStudioDraftFactory.BuildVariantQuestion(type, difficulty, canonical);
                drafts.Add(QuestionStudioDraftFactory.Create(run, canonical.SourceUnit, item, "Variant", canonical.Id));
                remaining--;
            }
        }

        return Task.FromResult(drafts);
    }
}

public sealed class QuestionDraftVerifier : IQuestionDraftVerifier
{
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<QuestionDraftVerifier> _logger;

    public QuestionDraftVerifier(IOllamaService ollamaService, ILogger<QuestionDraftVerifier> logger)
    {
        _ollamaService = ollamaService;
        _logger = logger;
    }

    public async Task VerifyAsync(QuestionDraft draft, CancellationToken cancellationToken = default)
    {
        var localIssues = VerifyLocally(draft);
        if (localIssues.Count > 0)
        {
            ApplyScores(draft, 0.45, 0.45, 0.55, string.Join(" ", localIssues));
            return;
        }

        var aiResult = await VerifyWithAiAsync(draft);
        if (aiResult != null)
        {
            ApplyScores(
                draft,
                Math.Clamp(aiResult.GroundingScore, 0, 1),
                Math.Clamp(aiResult.AnswerScore, 0, 1),
                Math.Clamp(aiResult.ClarityScore, 0, 1),
                aiResult.FailureReason ?? string.Empty);
            return;
        }

        var evidenceBonus = !string.IsNullOrWhiteSpace(draft.SourceEvidence) ? 0.9 : 0.72;
        ApplyScores(draft, evidenceBonus, 0.82, 0.82, string.Empty);
    }

    private static List<string> VerifyLocally(QuestionDraft draft)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.QuestionText)) issues.Add("Question text is required.");
        if (RequiresAnswer(draft.QuestionType) && string.IsNullOrWhiteSpace(draft.CorrectAnswer)) issues.Add("Correct answer is required.");
        if (RequiresAnswer(draft.QuestionType) && IsPlaceholderAnswer(draft.QuestionType, draft.CorrectAnswer)) issues.Add("Correct answer looks like a placeholder.");
        if (draft.QuestionText.Length > 700) issues.Add("Question text is too long.");
        if (draft.QuestionText.Contains("```", StringComparison.Ordinal) || draft.QuestionText.Contains("{\"", StringComparison.Ordinal)) issues.Add("Question text contains markup or JSON artifacts.");
        if (draft.DraftKind == "Canonical" && string.IsNullOrWhiteSpace(draft.SourceEvidence)) issues.Add("Source evidence is required.");

        var options = QuestionStudioDraftFactory.ParseOptions(draft.OptionsJson);
        if (draft.QuestionType == "MultipleChoice")
        {
            if (options.Count < 4) issues.Add("MultipleChoice requires at least four options.");
            if (options.Select(x => x.Text.Trim().ToLowerInvariant()).Distinct().Count() != options.Count) issues.Add("MultipleChoice options must be unique.");
            if (!options.Any(x => string.Equals(x.Key, draft.CorrectAnswer, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Text, draft.CorrectAnswer, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add("Correct answer must match an option key or text.");
            }
        }

        if (draft.Explanation.Length < 18) issues.Add("Explanation is too short.");
        return issues;
    }

    private static bool IsPlaceholderAnswer(string questionType, string? answer)
    {
        if (questionType is not ("ShortAnswer" or "Flashcard" or "FillInTheBlank"))
        {
            return false;
        }

        var normalized = answer?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(normalized, "A", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "N/A", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "TODO", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Reference option ", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<QuestionStudioVerificationResult?> VerifyWithAiAsync(QuestionDraft draft)
    {
        try
        {
            var prompt = $@"Verify this study question against source evidence only.

Question: {draft.QuestionText}
Options: {draft.OptionsJson}
Correct answer: {draft.CorrectAnswer}
Explanation: {draft.Explanation}
Source evidence: {draft.SourceEvidence}

Return JSON only. All score fields must be JSON numbers between 0.0 and 1.0.
Never return scores as strings, empty strings, null, percentages, or words.
{{
  ""groundingScore"": 0.0,
  ""answerScore"": 0.0,
  ""clarityScore"": 0.0,
  ""failureReason"": """"
}}";
            return await _ollamaService.GenerateStructuredResponseAsync<QuestionStudioVerificationResult>(
                prompt,
                "Return valid JSON only. Score fields must be numeric JSON values from 0.0 to 1.0, never strings.",
                OllamaModelProfile.Verification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI verification failed for question draft {DraftId}", draft.Id);
            return null;
        }
    }

    private static void ApplyScores(QuestionDraft draft, double groundingScore, double answerScore, double clarityScore, string failureReason)
    {
        draft.GroundingScore = groundingScore;
        draft.AnswerScore = answerScore;
        draft.ClarityScore = clarityScore;
        draft.DuplicateScore = draft.DuplicateScore <= 0 ? 1.0 : draft.DuplicateScore;
        draft.OverallScore = Math.Round((groundingScore * 0.4) + (answerScore * 0.3) + (clarityScore * 0.2) + (draft.DuplicateScore * 0.1), 4);
        draft.FailureReason = failureReason;
        draft.Status = draft.OverallScore switch
        {
            >= 0.85 => "Verified",
            >= 0.70 => "Borderline",
            >= 0.50 => "Rejected",
            _ => "Quarantined"
        };
        draft.VerifiedAt = DateTime.UtcNow;
        draft.StemHash = QuestionStudioText.HashStem(draft.QuestionText);
    }

    private static bool RequiresAnswer(string type)
        => type is "MultipleChoice" or "ShortAnswer" or "TrueFalse" or "FillInTheBlank" or "Flashcard";
}

public sealed class QuestionDraftDeduplicator : IQuestionDraftDeduplicator
{
    private readonly ApplicationDbContext _context;

    public QuestionDraftDeduplicator(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> IsExactDuplicateAsync(QuestionDraft draft, CancellationToken cancellationToken = default)
    {
        var hash = string.IsNullOrWhiteSpace(draft.StemHash) ? QuestionStudioText.HashStem(draft.QuestionText) : draft.StemHash;
        return await _context.QuestionDrafts.AnyAsync(x =>
            x.Id != draft.Id &&
            x.DocumentId == draft.DocumentId &&
            x.StemHash == hash &&
            x.Status != "Imported",
            cancellationToken);
    }

    public async Task<bool> IsNearDuplicateAsync(QuestionDraft draft, CancellationToken cancellationToken = default)
    {
        if (!draft.SourceUnitId.HasValue)
        {
            return false;
        }

        var peers = await _context.QuestionDrafts
            .Where(x => x.Id != draft.Id &&
                x.SourceUnitId == draft.SourceUnitId &&
                x.QuestionType == draft.QuestionType &&
                x.Difficulty == draft.Difficulty &&
                x.LearningObjective == draft.LearningObjective)
            .Select(x => x.QuestionText)
            .ToListAsync(cancellationToken);

        var candidateTokens = QuestionStudioText.Tokenize(draft.QuestionText);
        return peers.Any(peer =>
        {
            var peerTokens = QuestionStudioText.Tokenize(peer);
            if (candidateTokens.Count == 0 || peerTokens.Count == 0)
            {
                return false;
            }

            var overlap = candidateTokens.Intersect(peerTokens, StringComparer.OrdinalIgnoreCase).Count();
            return overlap / (double)Math.Min(candidateTokens.Count, peerTokens.Count) >= 0.82;
        });
    }

    public async Task MarkDuplicatesAsync(int generationRunId, CancellationToken cancellationToken = default)
    {
        var drafts = await _context.QuestionDrafts
            .Where(x => x.GenerationRunId == generationRunId && x.Status != "Imported")
            .OrderByDescending(x => x.OverallScore)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenKeys = new List<(int? SourceUnitId, string QuestionType, string Difficulty, string LearningObjective, HashSet<string> Tokens)>();

        foreach (var draft in drafts)
        {
            draft.StemHash = QuestionStudioText.HashStem(draft.QuestionText);
            var tokens = QuestionStudioText.Tokenize(draft.QuestionText);
            var exactDuplicate = !seenHashes.Add(draft.StemHash);
            var nearDuplicate = seenKeys.Any(key =>
                key.SourceUnitId == draft.SourceUnitId &&
                key.QuestionType == draft.QuestionType &&
                key.Difficulty == draft.Difficulty &&
                key.LearningObjective == draft.LearningObjective &&
                tokens.Count > 0 &&
                key.Tokens.Count > 0 &&
                tokens.Intersect(key.Tokens, StringComparer.OrdinalIgnoreCase).Count() / (double)Math.Min(tokens.Count, key.Tokens.Count) >= 0.82);

            if (exactDuplicate || nearDuplicate)
            {
                draft.DuplicateScore = 0.15;
                draft.OverallScore = Math.Round((draft.GroundingScore * 0.4) + (draft.AnswerScore * 0.3) + (draft.ClarityScore * 0.2) + (draft.DuplicateScore * 0.1), 4);
                draft.Status = draft.OverallScore >= 0.50 ? "Rejected" : "Quarantined";
                draft.FailureReason = string.IsNullOrWhiteSpace(draft.FailureReason)
                    ? "duplicate"
                    : $"{draft.FailureReason} duplicate";
            }
            else
            {
                draft.DuplicateScore = 1.0;
                seenKeys.Add((draft.SourceUnitId, draft.QuestionType, draft.Difficulty, draft.LearningObjective, tokens));
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class QuestionDraftImportService : IQuestionDraftImportService
{
    private readonly ApplicationDbContext _context;

    public QuestionDraftImportService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<QuestionDraftImportResult> ImportAsync(int documentId, IReadOnlyCollection<int> draftIds, string userId, CancellationToken cancellationToken = default)
    {
        var normalizedIds = draftIds.Distinct().ToList();
        var existingQuestionHashes = (await _context.Questions
            .Where(x => x.DocumentId == documentId && !x.IsArchived)
            .Select(x => x.QuestionText)
            .ToListAsync(cancellationToken))
            .Select(QuestionStudioText.HashStem)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var skipped = new List<int>();
        var imported = 0;
        var affectedRunIds = new HashSet<int>();

        foreach (var draftId in normalizedIds)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            var draft = await _context.QuestionDrafts
                .FirstOrDefaultAsync(x => x.Id == draftId && x.DocumentId == documentId, cancellationToken);
            if (draft == null || draft.Status is not ("Verified" or "Borderline"))
            {
                skipped.Add(draftId);
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            var alreadyImported = await _context.Questions
                .AnyAsync(x => x.SourceDraftId == draft.Id, cancellationToken);
            var draftHash = QuestionStudioText.HashStem(draft.QuestionText);
            if (alreadyImported || existingQuestionHashes.Contains(draftHash))
            {
                skipped.Add(draftId);
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            try
            {
                var question = BuildQuestion(draft);
                _context.Questions.Add(question);
                var before = JsonSerializer.Serialize(new { draft.Status });
                draft.Status = "Imported";
                draft.ImportedAt = DateTime.UtcNow;
                _context.QuestionReviewEvents.Add(new QuestionReviewEvent
                {
                    QuestionDraftId = draft.Id,
                    UserId = userId,
                    Action = "Import",
                    BeforeJson = before,
                    AfterJson = JsonSerializer.Serialize(new { draft.Status, draft.ImportedAt }),
                    Note = "Imported into Question bank."
                });

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                existingQuestionHashes.Add(draftHash);
                affectedRunIds.Add(draft.GenerationRunId);
                imported++;
            }
            catch (DbUpdateException ex) when (IsUniqueSourceDraftViolation(ex))
            {
                await transaction.RollbackAsync(cancellationToken);
                _context.ChangeTracker.Clear();
                skipped.Add(draftId);
            }
        }

        await UpdateRunImportedCountsAsync(affectedRunIds.ToList(), cancellationToken);
        return new QuestionDraftImportResult(imported, skipped.Count, skipped);
    }

    private static bool IsUniqueSourceDraftViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException postgresException &&
            postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
            (string.Equals(postgresException.ConstraintName, "ix_questions_source_draft_id", StringComparison.OrdinalIgnoreCase) ||
                postgresException.MessageText.Contains("source_draft_id", StringComparison.OrdinalIgnoreCase));

    private async Task UpdateRunImportedCountsAsync(IReadOnlyCollection<int> runIds, CancellationToken cancellationToken)
    {
        foreach (var runId in runIds)
        {
            var run = await _context.QuestionGenerationRuns.FindAsync(new object?[] { runId }, cancellationToken);
            if (run == null)
            {
                continue;
            }

            run.ImportedCount = await _context.QuestionDrafts.CountAsync(x => x.GenerationRunId == runId && x.Status == "Imported", cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static Question BuildQuestion(QuestionDraft draft)
    {
        var type = QuestionStudioDefaults.ParseQuestionType(draft.QuestionType);
        var question = new Question
        {
            DocumentId = draft.DocumentId,
            QuestionText = draft.QuestionText,
            QuestionType = type,
            OptionsJson = NormalizeOptionsForQuestion(draft, type),
            CorrectAnswer = draft.CorrectAnswer,
            Explanation = draft.Explanation,
            Difficulty = QuestionStudioDefaults.ParseDifficulty(draft.Difficulty),
            Topic = draft.TopicTag,
            VerifierScore = (int)Math.Round(Math.Clamp(draft.OverallScore, 0, 1) * 100),
            SourceDraftId = draft.Id,
            QualityScore = draft.OverallScore,
            CreatedAt = DateTime.UtcNow
        };
        question.SetVerifierIssues(string.IsNullOrWhiteSpace(draft.FailureReason) ? new List<string>() : new List<string> { draft.FailureReason });
        return question;
    }

    private static string NormalizeOptionsForQuestion(QuestionDraft draft, QuestionType type)
    {
        var options = QuestionStudioDraftFactory.ParseOptions(draft.OptionsJson);
        if (type == QuestionType.TrueFalse && options.Count == 0)
        {
            options = new List<QuestionOption>
            {
                new() { Key = "A", Text = "True", IsCorrect = string.Equals(draft.CorrectAnswer, "A", StringComparison.OrdinalIgnoreCase) || draft.CorrectAnswer.Equals("true", StringComparison.OrdinalIgnoreCase) },
                new() { Key = "B", Text = "False", IsCorrect = string.Equals(draft.CorrectAnswer, "B", StringComparison.OrdinalIgnoreCase) || draft.CorrectAnswer.Equals("false", StringComparison.OrdinalIgnoreCase) }
            };
        }

        return JsonSerializer.Serialize(options);
    }
}

internal static class QuestionStudioDefaults
{
    public static readonly IReadOnlyCollection<string> DefaultQuestionTypes = new[] { "MultipleChoice", "Flashcard", "ShortAnswer" };
    public static readonly IReadOnlyCollection<string> DefaultDifficulties = new[] { "Easy", "Medium", "Hard" };

    public static QuestionStudioProfile ResolveProfile(string mode)
        => mode.ToLowerInvariant() switch
        {
            "fast" => new QuestionStudioProfile(1, 2, 0, 0.70, false),
            "quality" => new QuestionStudioProfile(2, 2, 2, 0.88, false),
            "max_draft" => new QuestionStudioProfile(3, 6, 1, 0.72, true),
            _ => new QuestionStudioProfile(2, 3, 1, 0.80, false)
        };

    public static bool IsValidMode(string mode)
        => mode is "fast" or "balanced" or "quality" or "max_draft";

    public static bool IsSupportedGenerationType(string type)
        => type is "MultipleChoice" or "ShortAnswer" or "TrueFalse" or "FillInTheBlank" or "Flashcard";

    public static QuestionType ParseQuestionType(string value)
        => value switch
        {
            "TrueFalse" => QuestionType.TrueFalse,
            "ShortAnswer" => QuestionType.ShortAnswer,
            "FillBlank" => QuestionType.FillInTheBlank,
            "FillInTheBlank" => QuestionType.FillInTheBlank,
            "Flashcard" => QuestionType.ShortAnswer,
            _ => QuestionType.MultipleChoice
        };

    public static DifficultyLevel ParseDifficulty(string value)
        => value switch
        {
            "Easy" => DifficultyLevel.Easy,
            "Hard" => DifficultyLevel.Hard,
            _ => DifficultyLevel.Medium
        };
}

internal sealed record QuestionStudioProfile(int CanonicalPerUnit, int VariantsPerCanonical, int MaxRepairRounds, double TargetVerifierScore, bool AllowBorderlineDrafts);

internal static class QuestionStudioDraftFactory
{
    public static QuestionDraft Create(
        QuestionGenerationRun run,
        QuestionSourceUnit? unit,
        QuestionStudioAiQuestion item,
        string draftKind,
        int? parentDraftId)
    {
        var questionType = NormalizeQuestionType(item.QuestionType);
        return new QuestionDraft
        {
            DocumentId = run.DocumentId,
            GenerationRunId = run.Id,
            SourceUnitId = unit?.Id,
            SourceUnit = unit,
            Status = "Draft",
            DraftKind = draftKind,
            ParentDraftId = parentDraftId,
            QuestionText = NormalizeText(item.QuestionText),
            QuestionType = questionType,
            OptionsJson = JsonSerializer.Serialize(NormalizeOptions(item.Options, item.CorrectAnswer, questionType)),
            CorrectAnswer = NormalizeAnswer(item.CorrectAnswer, questionType),
            Explanation = NormalizeText(item.Explanation),
            Difficulty = NormalizeDifficulty(item.Difficulty),
            LearningObjective = NormalizeLearningObjective(item.LearningObjective),
            TopicTag = unit?.TopicTag ?? string.Empty,
            SourceEvidence = NormalizeText(string.IsNullOrWhiteSpace(item.SourceEvidence) ? unit?.Content ?? string.Empty : item.SourceEvidence),
            StemHash = QuestionStudioText.HashStem(item.QuestionText ?? string.Empty),
            MetadataJson = JsonSerializer.Serialize(new { generatedBy = "question-studio-v2" })
        };
    }

    public static bool IsUsable(QuestionStudioAiQuestion item)
        => !string.IsNullOrWhiteSpace(item.QuestionText) && !string.IsNullOrWhiteSpace(item.Explanation);

    public static QuestionStudioAiQuestion BuildDeterministicQuestion(string type, string difficulty, string source, string topicTag)
    {
        var evidence = NormalizeText(source);
        var focus = Truncate(evidence, 180);
        type = NormalizeQuestionType(type);

        if (type == "Flashcard")
        {
            return new QuestionStudioAiQuestion
            {
                QuestionText = $"What should learners remember about {topicTag}?",
                QuestionType = "Flashcard",
                CorrectAnswer = focus,
                Explanation = $"This answer is grounded in the source evidence: {Truncate(evidence, 220)}",
                Difficulty = difficulty,
                LearningObjective = "Remember",
                SourceEvidence = evidence
            };
        }

        if (type == "ShortAnswer")
        {
            return new QuestionStudioAiQuestion
            {
                QuestionText = $"Summarize the key idea about {topicTag}.",
                QuestionType = "ShortAnswer",
                CorrectAnswer = focus,
                Explanation = $"The source states this idea directly: {Truncate(evidence, 220)}",
                Difficulty = difficulty,
                LearningObjective = "Understand",
                SourceEvidence = evidence
            };
        }

        if (type == "TrueFalse")
        {
            return new QuestionStudioAiQuestion
            {
                QuestionText = $"True or false: {focus}",
                QuestionType = "TrueFalse",
                Options = new List<string> { "A. True", "B. False" },
                CorrectAnswer = "A",
                Explanation = $"The statement is taken from the source evidence: {Truncate(evidence, 220)}",
                Difficulty = difficulty,
                LearningObjective = "Remember",
                SourceEvidence = evidence
            };
        }

        return new QuestionStudioAiQuestion
        {
            QuestionText = $"Which statement best matches the source about {topicTag}?",
            QuestionType = "MultipleChoice",
            Options = new List<string>
            {
                $"A. {focus}",
                "B. The source says the opposite.",
                "C. The source does not mention this topic.",
                "D. The topic is unrelated to the document."
            },
            CorrectAnswer = "A",
            Explanation = $"Option A is grounded in the source evidence: {Truncate(evidence, 220)}",
            Difficulty = difficulty,
            LearningObjective = "Understand",
            SourceEvidence = evidence
        };
    }

    public static QuestionStudioAiQuestion BuildVariantQuestion(string type, string difficulty, QuestionDraft canonical)
    {
        type = NormalizeQuestionType(type);
        if (type == canonical.QuestionType)
        {
            type = canonical.QuestionType == "MultipleChoice" ? "ShortAnswer" : "MultipleChoice";
        }

        var evidence = string.IsNullOrWhiteSpace(canonical.SourceEvidence) ? canonical.Explanation : canonical.SourceEvidence;
        var baseAnswer = string.IsNullOrWhiteSpace(canonical.CorrectAnswer) ? Truncate(evidence, 160) : canonical.CorrectAnswer;

        if (type == "ShortAnswer" || type == "Flashcard")
        {
            return new QuestionStudioAiQuestion
            {
                QuestionText = $"Explain the idea tested by: {canonical.QuestionText}",
                QuestionType = type,
                CorrectAnswer = baseAnswer,
                Explanation = $"This variant keeps the same source-grounded answer. {canonical.Explanation}",
                Difficulty = difficulty,
                LearningObjective = "Understand",
                SourceEvidence = evidence
            };
        }

        return new QuestionStudioAiQuestion
        {
            QuestionText = $"Which answer is most consistent with this source evidence: {Truncate(evidence, 120)}?",
            QuestionType = "MultipleChoice",
            Options = new List<string>
            {
                $"A. {baseAnswer}",
                "B. A statement not supported by the source.",
                "C. A detail from another topic.",
                "D. There is not enough information."
            },
            CorrectAnswer = "A",
            Explanation = $"Option A preserves the canonical answer. {canonical.Explanation}",
            Difficulty = difficulty,
            LearningObjective = "Apply",
            SourceEvidence = evidence
        };
    }

    public static List<QuestionOption> ParseOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<QuestionOption>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<QuestionOption>>(json) ?? new List<QuestionOption>();
        }
        catch
        {
            try
            {
                var values = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                return NormalizeOptions(values, "A", "MultipleChoice");
            }
            catch
            {
                return new List<QuestionOption>();
            }
        }
    }

    private static List<QuestionOption> NormalizeOptions(List<string>? rawOptions, string? correctAnswer, string questionType)
    {
        if (questionType is "ShortAnswer" or "Flashcard" or "FillInTheBlank")
        {
            return new List<QuestionOption>();
        }

        if (questionType == "TrueFalse")
        {
            var normalizedAnswer = NormalizeAnswer(correctAnswer, questionType);
            return new List<QuestionOption>
            {
                new() { Key = "A", Text = "True", IsCorrect = normalizedAnswer == "A" },
                new() { Key = "B", Text = "False", IsCorrect = normalizedAnswer == "B" }
            };
        }

        var keys = new[] { "A", "B", "C", "D", "E", "F" };
        var normalized = (rawOptions ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select((value, index) => ParseOption(value, keys.ElementAtOrDefault(index) ?? "A"))
            .GroupBy(x => x.Text.Trim().ToLowerInvariant())
            .Select(x => x.First())
            .Take(6)
            .ToList();

        while (normalized.Count < 4)
        {
            var key = keys[normalized.Count];
            normalized.Add(new QuestionOption { Key = key, Text = $"Reference option {key}", IsCorrect = false });
        }

        var answer = NormalizeAnswer(correctAnswer, questionType);
        foreach (var option in normalized)
        {
            option.IsCorrect = string.Equals(option.Key, answer, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(option.Text, correctAnswer, StringComparison.OrdinalIgnoreCase);
        }

        if (!normalized.Any(x => x.IsCorrect))
        {
            normalized[0].IsCorrect = true;
        }

        return normalized;
    }

    private static QuestionOption ParseOption(string value, string fallbackKey)
    {
        var match = Regex.Match(value.Trim(), @"^([A-Fa-f])[\).\:\-]\s*(.+)$");
        if (match.Success)
        {
            return new QuestionOption
            {
                Key = match.Groups[1].Value.ToUpperInvariant(),
                Text = Truncate(NormalizeText(match.Groups[2].Value), 260),
                IsCorrect = false
            };
        }

        return new QuestionOption
        {
            Key = fallbackKey,
            Text = Truncate(NormalizeText(value), 260),
            IsCorrect = false
        };
    }

    private static string NormalizeQuestionType(string? value)
        => value switch
        {
            "TrueFalse" => "TrueFalse",
            "ShortAnswer" => "ShortAnswer",
            "FillBlank" => "FillInTheBlank",
            "FillInTheBlank" => "FillInTheBlank",
            "MatchPair" => "ShortAnswer",
            "Flashcard" => "Flashcard",
            _ => "MultipleChoice"
        };

    private static string NormalizeAnswer(string? value, string questionType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return questionType is "ShortAnswer" or "Flashcard" or "FillInTheBlank"
                ? string.Empty
                : "A";
        }

        if (questionType == "TrueFalse")
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "false" => "B",
                "b" => "B",
                _ => "A"
            };
        }

        return Truncate(NormalizeText(value), 500);
    }

    private static string NormalizeDifficulty(string? value)
        => value switch
        {
            "Easy" => "Easy",
            "Hard" => "Hard",
            _ => "Medium"
        };

    private static string NormalizeLearningObjective(string? value)
        => value switch
        {
            "Remember" => "Remember",
            "Apply" => "Apply",
            "Analyze" => "Analyze",
            _ => "Understand"
        };

    private static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Replace('\u00A0', ' '), @"\s+", " ").Trim();

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
}

internal static class QuestionStudioText
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "about", "what", "which",
        "mot", "cac", "cho", "voi", "trong", "duoc", "khong", "nhung", "theo"
    };

    public static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string HashStem(string value)
        => Hash(NormalizeStem(value));

    public static HashSet<string> Tokenize(string value)
        => NormalizeStem(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 3 && !StopWords.Contains(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeStem(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }
}

internal sealed class QuestionStudioAiQuestionList
{
    public List<QuestionStudioAiQuestion>? Questions { get; set; }
}

internal sealed class QuestionStudioAiQuestion
{
    public string? QuestionText { get; set; }
    public string? QuestionType { get; set; }
    public List<string>? Options { get; set; }
    public string? CorrectAnswer { get; set; }
    public string? Explanation { get; set; }
    public string? Difficulty { get; set; }
    public string? LearningObjective { get; set; }
    public string? SourceEvidence { get; set; }
}

internal sealed class QuestionStudioVerificationResult
{
    public double GroundingScore { get; set; }
    public double AnswerScore { get; set; }
    public double ClarityScore { get; set; }
    public string? FailureReason { get; set; }
}
