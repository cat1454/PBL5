using System.Text.Json.Serialization;

namespace ELearnGamePlatform.Core.Entities;

public static class DocumentPageProcessingMethods
{
    public const string DirectText = "direct_text";
    public const string Ocr = "ocr";
    public const string Empty = "empty";
    public const string Failed = "failed";
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

    [JsonPropertyName("selectedVariant")]
    public string? SelectedVariant { get; set; }

    [JsonPropertyName("selectedPass")]
    public string? SelectedPass { get; set; }

    [JsonPropertyName("ocrRetry")]
    public DocumentPageOcrRetryMetadata? OcrRetry { get; set; }

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

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; set; }
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

    [JsonPropertyName("totalEstimatedTokens")]
    public int TotalEstimatedTokens { get; set; }

    [JsonPropertyName("pages")]
    public List<DocumentPageProcessingReport> Pages { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}
