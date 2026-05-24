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

