using System.Text.Json;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Options;
using Microsoft.Extensions.Options;

namespace ELearnGamePlatform.Services.DocumentProcessing;

public class DocumentGenerationReadinessService : IDocumentGenerationReadinessService
{
    private readonly IDocumentUnderstandingRunRepository _understandingRunRepository;
    private readonly IDocumentQualityScorer _qualityScorer;
    private readonly DocumentUnderstandingOptions _options;

    public DocumentGenerationReadinessService(
        IDocumentUnderstandingRunRepository understandingRunRepository,
        IDocumentQualityScorer qualityScorer,
        IOptions<DocumentUnderstandingOptions> options)
    {
        _understandingRunRepository = understandingRunRepository;
        _qualityScorer = qualityScorer;
        _options = options.Value;
    }

    public async Task<DocumentGenerationReadiness> GetReadinessAsync(Document document, bool confirmed = false)
    {
        var latestRun = await _understandingRunRepository.GetLatestByDocumentIdAsync(document.Id);
        if (latestRun?.DocumentConfidence is double confidence)
        {
            var reasons = ExtractRunReasons(latestRun);
            if (reasons.Count == 0 && latestRun.NeedsReview)
            {
                reasons.Add("Document understanding marked this source as needing review.");
            }

            return Build(
                document.Id,
                confidence,
                latestRun.NeedsReview,
                reasons,
                confirmed);
        }

        var score = _qualityScorer.Score(new DocumentQualityScoreInput
        {
            ExtractedText = document.ExtractedText
        });

        return Build(
            document.Id,
            score.Confidence,
            score.NeedsReview,
            score.Reasons,
            confirmed);
    }

    public DocumentGenerationReadiness GetAggregateReadiness(IEnumerable<DocumentGenerationReadiness> readinessResults, bool confirmed = false)
    {
        var results = readinessResults.Where(result => result != null).ToList();
        if (results.Count == 0)
        {
            return Build(
                null,
                0d,
                needsReview: true,
                new[] { "No ready source documents were available for generation." },
                confirmed);
        }

        var weakest = results
            .OrderBy(result => result.Confidence ?? -1d)
            .ThenByDescending(result => result.Blocked)
            .First();

        return Build(
            weakest.DocumentId,
            weakest.Confidence ?? 0d,
            results.Any(result => result.NeedsReview),
            results.SelectMany(result => result.Reasons).Distinct(StringComparer.OrdinalIgnoreCase).Take(12),
            confirmed);
    }

    private DocumentGenerationReadiness Build(
        int? documentId,
        double confidence,
        bool needsReview,
        IEnumerable<string> reasons,
        bool confirmed)
    {
        confidence = Math.Clamp(Math.Round(confidence, 4), 0d, 1d);
        var result = new DocumentGenerationReadiness
        {
            DocumentId = documentId,
            Confidence = confidence,
            NeedsReview = needsReview,
            ShowWarning = _options.ShowGenerationWarnings,
            Reasons = reasons
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .Select(reason => reason.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList()
        };

        if (confidence >= _options.MinAutoGenerateConfidence)
        {
            result.Status = DocumentGenerationReadinessStatuses.Good;
            result.Action = DocumentGenerationReadinessActions.Allow;
            result.NeedsReview = false;
            return result;
        }

        if (confidence >= _options.MinReviewRequiredConfidence)
        {
            result.Status = DocumentGenerationReadinessStatuses.NeedsReview;
            result.Action = DocumentGenerationReadinessActions.AllowWithReviewWarning;
            result.NeedsReview = true;
            return result;
        }

        if (confidence >= _options.MinStrongWarningConfidence)
        {
            result.Status = DocumentGenerationReadinessStatuses.LowConfidence;
            result.Action = DocumentGenerationReadinessActions.WarnStrongly;
            result.NeedsReview = true;
            result.RequiresConfirmation = _options.EnforceGenerationGate && !confirmed;
            result.Blocked = result.RequiresConfirmation;
            return result;
        }

        result.Status = DocumentGenerationReadinessStatuses.ExtractionFailed;
        result.Action = DocumentGenerationReadinessActions.BlockAutoGeneration;
        result.NeedsReview = true;
        result.Blocked = _options.EnforceGenerationGate;
        result.RequiresConfirmation = false;
        return result;
    }

    private static List<string> ExtractRunReasons(DocumentUnderstandingRun run)
    {
        var reasons = new List<string>();
        AddJsonStringValues(run.FailureReasonsJson, reasons);

        if (string.Equals(run.Status, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(run.Status, DocumentQualityStatuses.ExtractionFailed, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("Document understanding did not complete successfully.");
        }

        return reasons;
    }

    private static void AddJsonStringValues(string? json, List<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            AddJsonStringValues(document.RootElement, reasons);
        }
        catch (JsonException)
        {
            reasons.Add(json.Trim());
        }
    }

    private static void AddJsonStringValues(JsonElement element, List<string> reasons)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    reasons.Add(value.Trim());
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AddJsonStringValues(item, reasons);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddJsonStringValues(property.Value, reasons);
                }
                break;
        }
    }
}
