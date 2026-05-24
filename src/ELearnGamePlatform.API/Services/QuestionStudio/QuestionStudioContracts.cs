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

