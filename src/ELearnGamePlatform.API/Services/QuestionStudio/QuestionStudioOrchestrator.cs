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
public sealed class QuestionStudioOrchestrator
{
    private const string SafeRunFailureMessage = "Question Studio run failed. Please try again or contact support if the problem continues.";

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

            var refreshedCanonicalQuery = _context.QuestionDrafts
                .Include(x => x.SourceUnit)
                .Where(x => x.GenerationRunId == run.Id && x.DraftKind == "Canonical" && x.Status == "Verified");
            if (profile.AllowBorderlineDrafts)
            {
                refreshedCanonicalQuery = _context.QuestionDrafts
                    .Include(x => x.SourceUnit)
                    .Where(x => x.GenerationRunId == run.Id && x.DraftKind == "Canonical" && (x.Status == "Verified" || x.Status == "Borderline"));
            }

            var refreshedCanonical = await refreshedCanonicalQuery
                .OrderByDescending(x => x.OverallScore)
                .Take(Math.Max(1, run.TargetDraftCount))
                .ToListAsync(cancellationToken);

            await UpdateRunAsync(run, "Running", "GeneratingVariants", cancellationToken);
            var currentDraftCount = await _context.QuestionDrafts.CountAsync(x => x.GenerationRunId == run.Id, cancellationToken);
            var remainingDraftBudget = Math.Max(0, run.TargetDraftCount - currentDraftCount);
            var variantDrafts = remainingDraftBudget > 0 && refreshedCanonical.Count > 0
                ? await _variantGenerator.GenerateAsync(run, refreshedCanonical, questionTypes, difficulties, remainingDraftBudget, cancellationToken)
                : new List<QuestionDraft>();
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
            await UpdateRunAsync(run, "Failed", "Failed", cancellationToken, errorMessage: SafeRunFailureMessage, completedAt: DateTime.UtcNow);
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

