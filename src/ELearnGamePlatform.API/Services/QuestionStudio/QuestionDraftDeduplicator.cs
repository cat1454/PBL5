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

