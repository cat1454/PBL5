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

