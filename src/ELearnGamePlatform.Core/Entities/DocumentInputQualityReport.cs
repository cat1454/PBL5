using System.Text.Json.Serialization;

namespace ELearnGamePlatform.Core.Entities;

public static class DocumentPageProcessingMethods
{
    public const string DirectText = "direct_text";
    public const string Ocr = "ocr";
    public const string Empty = "empty";
    public const string Failed = "failed";
}

public static class DocumentPageRoles
{
    public const string Cover = "cover";
    public const string Title = "title";
    public const string TableOfContents = "toc";
    public const string Body = "body";
    public const string References = "references";
    public const string FootnoteHeavy = "footnote_heavy";
    public const string Empty = "empty";
}

public static class DocumentQualityStatuses
{
    public const string AutoGenerateAllowed = "AutoGenerateAllowed";
    public const string SummaryOnlyRecommended = "SummaryOnlyRecommended";
    public const string ExtractionFailed = "ExtractionFailed";
    public const string Accepted = "Accepted";
    public const string AcceptedWithWarnings = "AcceptedWithWarnings";
    public const string NeedsReview = "NeedsReview";
    public const string Rejected = "Rejected";
}

public class DocumentQualityScoreInput
{
    public string? ExtractedText { get; set; }
    public DocumentInputQualityReport? PageQualityReport { get; set; }
}

public class DocumentQualityScoreResult
{
    public double Confidence { get; set; }
    public string Status { get; set; } = DocumentQualityStatuses.ExtractionFailed;
    public bool NeedsReview { get; set; } = true;
    public int CharCount { get; set; }
    public int WordCount { get; set; }
    public double GarbageRatio { get; set; }
    public double ShortLineRatio { get; set; }
    public double? AverageOcrConfidence { get; set; }
    public int LowTextPageCount { get; set; }
    public int TotalPages { get; set; }
    public List<string> Reasons { get; set; } = new();
}

public class DocumentPageProcessingReport
{
    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = DocumentPageProcessingMethods.Empty;

    [JsonPropertyName("charCount")]
    public int CharCount { get; set; }

    [JsonPropertyName("wordCount")]
    public int WordCount { get; set; }

    [JsonPropertyName("signalRatio")]
    public double SignalRatio { get; set; }

    [JsonPropertyName("noiseScore")]
    public int NoiseScore { get; set; }

    [JsonPropertyName("estimatedTokenCount")]
    public int EstimatedTokenCount { get; set; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; set; }

    [JsonPropertyName("qualityScore")]
    public int QualityScore { get; set; }

    [JsonPropertyName("pageRole")]
    public string? PageRole { get; set; }

    [JsonPropertyName("excludedFromDocumentQualityAverage")]
    public bool ExcludedFromDocumentQualityAverage { get; set; }

    [JsonPropertyName("qualityAdjustments")]
    public List<string> QualityAdjustments { get; set; } = new();

    [JsonPropertyName("artifactRatio")]
    public double ArtifactRatio { get; set; }

    [JsonPropertyName("symbolRatio")]
    public double SymbolRatio { get; set; }

    [JsonPropertyName("digitOnlyLineRatio")]
    public double DigitOnlyLineRatio { get; set; }

    [JsonPropertyName("footnoteRatio")]
    public double FootnoteRatio { get; set; }

    [JsonPropertyName("paragraphCoherenceScore")]
    public double ParagraphCoherenceScore { get; set; }

    [JsonPropertyName("vietnameseDiacriticRatio")]
    public double VietnameseDiacriticRatio { get; set; }

    [JsonPropertyName("selectedVariant")]
    public string? SelectedVariant { get; set; }

    [JsonPropertyName("selectedPass")]
    public string? SelectedPass { get; set; }

    [JsonPropertyName("ocrRetry")]
    public DocumentPageOcrRetryMetadata? OcrRetry { get; set; }

    [JsonPropertyName("preprocessingSkipReasons")]
    public List<string> PreprocessingSkipReasons { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}

public class DocumentPageOcrRetryMetadata
{
    [JsonPropertyName("wasRetried")]
    public bool WasRetried { get; set; }

    [JsonPropertyName("retryThreshold")]
    public int RetryThreshold { get; set; }

    [JsonPropertyName("retryPdfDpi")]
    public int RetryPdfDpi { get; set; }

    [JsonPropertyName("maxRetryPerPage")]
    public int MaxRetryPerPage { get; set; }

    [JsonPropertyName("initialQualityScore")]
    public int InitialQualityScore { get; set; }

    [JsonPropertyName("selectedQualityScore")]
    public int SelectedQualityScore { get; set; }

    [JsonPropertyName("selectedAttempt")]
    public string SelectedAttempt { get; set; } = "initial";

    [JsonPropertyName("attempts")]
    public List<DocumentPageOcrAttemptMetadata> Attempts { get; set; } = new();

    [JsonPropertyName("preprocessingSkipReasons")]
    public List<string> PreprocessingSkipReasons { get; set; } = new();
}

public class DocumentPageOcrAttemptMetadata
{
    [JsonPropertyName("attempt")]
    public string Attempt { get; set; } = string.Empty;

    [JsonPropertyName("pdfDpi")]
    public int? PdfDpi { get; set; }

    [JsonPropertyName("qualityScore")]
    public int QualityScore { get; set; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; set; }

    [JsonPropertyName("selectedVariant")]
    public string? SelectedVariant { get; set; }

    [JsonPropertyName("selectedPass")]
    public string? SelectedPass { get; set; }

    [JsonPropertyName("preprocessingProfile")]
    public string? PreprocessingProfile { get; set; }

    [JsonPropertyName("isPreprocessingFallback")]
    public bool IsPreprocessingFallback { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("qualityGain")]
    public double QualityGain { get; set; }

    [JsonPropertyName("isLowGain")]
    public bool IsLowGain { get; set; }

    [JsonPropertyName("isSelectedBest")]
    public bool IsSelectedBest { get; set; }

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; set; }
}

public class DocumentPreprocessingEffectivenessSummary
{
    [JsonPropertyName("attemptCount")]
    public int AttemptCount { get; set; }

    [JsonPropertyName("selectedAttemptCount")]
    public int SelectedAttemptCount { get; set; }

    [JsonPropertyName("lowGainAttemptCount")]
    public int LowGainAttemptCount { get; set; }

    [JsonPropertyName("averageQualityGain")]
    public double AverageQualityGain { get; set; }

    [JsonPropertyName("averageDurationMs")]
    public double AverageDurationMs { get; set; }

    [JsonPropertyName("bestProfile")]
    public string? BestProfile { get; set; }

    [JsonPropertyName("worstProfile")]
    public string? WorstProfile { get; set; }

    [JsonPropertyName("profileWinCounts")]
    public Dictionary<string, int> ProfileWinCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("skipReasonCounts")]
    public Dictionary<string, int> SkipReasonCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class DocumentInputQualityReport
{
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("directTextPages")]
    public int DirectTextPages { get; set; }

    [JsonPropertyName("ocrPages")]
    public int OcrPages { get; set; }

    [JsonPropertyName("emptyPages")]
    public int EmptyPages { get; set; }

    [JsonPropertyName("failedPages")]
    public int FailedPages { get; set; }

    [JsonPropertyName("lowQualityPages")]
    public int LowQualityPages { get; set; }

    [JsonPropertyName("averagePageQuality")]
    public double AveragePageQuality { get; set; }

    [JsonPropertyName("averagePageQualityRaw")]
    public double AveragePageQualityRaw { get; set; }

    [JsonPropertyName("averagePageQualityWeighted")]
    public double AveragePageQualityWeighted { get; set; }

    [JsonPropertyName("bodyPageQualityAverage")]
    public double BodyPageQualityAverage { get; set; }

    [JsonPropertyName("excludedPageCount")]
    public int ExcludedPageCount { get; set; }

    [JsonPropertyName("bodyPageCount")]
    public int BodyPageCount { get; set; }

    [JsonPropertyName("coverTitlePageCount")]
    public int CoverTitlePageCount { get; set; }

    [JsonPropertyName("footnoteHeavyPageCount")]
    public int FootnoteHeavyPageCount { get; set; }

    [JsonPropertyName("qualityStatus")]
    public string QualityStatus { get; set; } = DocumentQualityStatuses.NeedsReview;

    [JsonPropertyName("qualityDecisionReason")]
    public string QualityDecisionReason { get; set; } = string.Empty;

    [JsonPropertyName("topQualityPenalties")]
    public List<string> TopQualityPenalties { get; set; } = new();

    [JsonPropertyName("totalEstimatedTokens")]
    public int TotalEstimatedTokens { get; set; }

    [JsonPropertyName("pages")]
    public List<DocumentPageProcessingReport> Pages { get; set; } = new();

    [JsonPropertyName("preprocessingEffectiveness")]
    public DocumentPreprocessingEffectivenessSummary PreprocessingEffectiveness { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}
