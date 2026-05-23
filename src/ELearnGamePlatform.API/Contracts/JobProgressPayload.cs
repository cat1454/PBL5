using System.Text.Json.Serialization;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;

namespace ELearnGamePlatform.API.Contracts;

public sealed class JobProgressPayload
{
    public string JobId { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? DocumentId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? FolderProjectId { get; init; }

    public string FileName { get; init; } = string.Empty;
    public string Status { get; init; } = "queued";
    public int Percent { get; init; }
    public string Stage { get; init; } = "queued";
    public string StageLabel { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? Current { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? Total { get; init; }

    public string UnitLabel { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? StageIndex { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? StageCount { get; init; }

    public int ElapsedSeconds { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? EstimatedRemainingSeconds { get; init; }

    public string Error { get; init; } = string.Empty;
    public string TopicTag { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DocumentConfidence { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QualityStatus { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NeedsReview { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? QuestionsGenerated { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? SlidesGenerated { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? SlideDeckId { get; init; }
}

public static class JobProgressPayloadFactory
{
    public static JobProgressPayload BuildDocument(DocumentProcessingJobState? state, Document? document = null)
    {
        if (state != null)
        {
            return new JobProgressPayload
            {
                DocumentId = state.DocumentId,
                FileName = ResolveFileName(state.FileName, document?.FileName),
                Status = ResolveStatus(state.Status, document?.Status),
                Percent = ResolvePercent(state.Percent, document?.Status),
                Stage = ResolveStage(state.Stage, document?.Status),
                StageLabel = ResolveStageLabel(state.StageLabel, document?.Status),
                Message = ResolveDocumentMessage(state.Message, document?.Status),
                Detail = Clean(state.Detail),
                Current = state.Current,
                Total = state.Total,
                UnitLabel = Clean(state.UnitLabel),
                StageIndex = state.StageIndex,
                StageCount = state.StageCount,
                ElapsedSeconds = state.ElapsedSeconds ?? 0,
                EstimatedRemainingSeconds = state.EstimatedRemainingSeconds,
                Error = Clean(state.Error),
                DocumentConfidence = state.DocumentConfidence,
                QualityStatus = string.IsNullOrWhiteSpace(state.QualityStatus) ? null : state.QualityStatus.Trim(),
                NeedsReview = state.NeedsReview
            };
        }

        return BuildDocumentFromSnapshot(document);
    }

    public static JobProgressPayload BuildQuestion(QuestionGenerationJobState state)
    {
        return new JobProgressPayload
        {
            JobId = Clean(state.JobId),
            DocumentId = state.DocumentId,
            Status = ResolveStatus(state.Status),
            Percent = ResolvePercent(state.Percent),
            Stage = ResolveStage(state.Stage),
            StageLabel = ResolveStageLabel(state.StageLabel),
            Message = ResolveQuestionMessage(state.Message),
            Detail = Clean(state.Detail),
            Current = state.Current,
            Total = state.Total,
            UnitLabel = Clean(state.UnitLabel),
            StageIndex = state.StageIndex,
            StageCount = state.StageCount,
            ElapsedSeconds = state.ElapsedSeconds ?? 0,
            EstimatedRemainingSeconds = state.EstimatedRemainingSeconds,
            Error = Clean(state.Error),
            TopicTag = Clean(state.TopicTag),
            QuestionsGenerated = state.QuestionsGenerated
        };
    }

    public static JobProgressPayload BuildSlide(SlideGenerationJobState? state, SlideDeck? deck = null)
    {
        if (state != null)
        {
            return new JobProgressPayload
            {
                JobId = Clean(state.JobId),
                DocumentId = state.DocumentId,
                FolderProjectId = state.FolderProjectId,
                Status = ResolveStatus(state.Status, deck?.Status),
                Percent = ResolvePercent(state.Percent, deck?.Status),
                Stage = ResolveStage(state.Stage, deck?.Status),
                StageLabel = ResolveStageLabel(state.StageLabel, deck?.Status),
                Message = ResolveSlideMessage(state.Message, deck?.Status),
                Detail = Clean(state.Detail),
                Current = state.Current,
                Total = state.Total,
                UnitLabel = Clean(state.UnitLabel),
                StageIndex = state.StageIndex,
                StageCount = state.StageCount,
                ElapsedSeconds = state.ElapsedSeconds ?? 0,
                EstimatedRemainingSeconds = state.EstimatedRemainingSeconds,
                Error = Clean(state.Error),
                SlidesGenerated = state.SlidesGenerated,
                SlideDeckId = state.SlideDeckId
            };
        }

        return BuildSlideFromSnapshot(deck);
    }

    private static JobProgressPayload BuildDocumentFromSnapshot(Document? document)
    {
        if (document == null)
        {
            return BuildEmpty();
        }

        var elapsedSeconds = Math.Max(0, (int)Math.Round((document.UpdatedAt - document.CreatedAt).TotalSeconds));
        var (status, percent, stage, stageLabel, message) = document.Status switch
        {
            DocumentStatus.Extracting => ("running", 25, "extracting", "Trich xuat van ban", "Dang trich xuat van ban tu tai lieu"),
            DocumentStatus.Analyzing => ("running", 80, "analyzing", "Phan tich noi dung", "Dang phan tich noi dung sau OCR/extraction"),
            DocumentStatus.Completed => ("completed", 100, "completed", "Hoan tat", "Da xu ly xong tai lieu"),
            DocumentStatus.Failed => ("failed", 100, "failed", "That bai", "Xu ly tai lieu that bai"),
            _ => ("queued", 0, "queued", "Cho xu ly", "Da xep hang xu ly tai lieu")
        };

        return new JobProgressPayload
        {
            DocumentId = document.Id,
            FileName = Clean(document.FileName),
            Status = status,
            Percent = percent,
            Stage = stage,
            StageLabel = stageLabel,
            Message = message,
            Detail = document.Status switch
            {
                DocumentStatus.Completed => "San sang tao cau hoi va hoc bang game",
                DocumentStatus.Failed => "Khong con state chi tiet trong RAM, can kiem tra log backend neu muon truy vet loi cu",
                DocumentStatus.Analyzing => "Dang tong hop topics, key points va summary",
                DocumentStatus.Extracting => $"Dang xu ly dinh dang {document.FileType}",
                _ => $"Dang cho bat dau xu ly file {document.FileName}"
            },
            StageIndex = document.Status switch
            {
                DocumentStatus.Extracting => 2,
                DocumentStatus.Analyzing => 4,
                DocumentStatus.Completed or DocumentStatus.Failed => 6,
                _ => 1
            },
            StageCount = 6,
            ElapsedSeconds = elapsedSeconds,
            EstimatedRemainingSeconds = document.Status switch
            {
                DocumentStatus.Completed or DocumentStatus.Failed => 0,
                _ => null
            },
            Error = document.Status == DocumentStatus.Failed
                ? "Document processing failed. Detailed in-memory error is unavailable after restart."
                : string.Empty
        };
    }

    private static JobProgressPayload BuildSlideFromSnapshot(SlideDeck? deck)
    {
        if (deck == null)
        {
            return BuildEmpty();
        }

        var completedSlides = deck.Items.Count(item => item.Status == SlideItemStatus.Completed);
        var totalSlides = deck.Items.Count;
        var elapsedSeconds = Math.Max(0, (int)Math.Round(((deck.CompletedAt ?? deck.UpdatedAt) - deck.CreatedAt).TotalSeconds));

        var (status, percent, stage, stageLabel, message, stageIndex) = deck.Status switch
        {
            SlideDeckStatus.GeneratingOutline => ("running", 18, "generating-outline", "Dang tao outline", "Dang len outline cho deck", 2),
            SlideDeckStatus.GeneratingSlides => ("running", totalSlides > 0 ? 24 + (int)Math.Round(66d * completedSlides / totalSlides) : 40, "generating-slides", "Dang sinh slide", "Dang sinh noi dung tung slide", 4),
            SlideDeckStatus.Completed => ("completed", 100, "completed", "Hoan tat", "Da tao xong bo slide", 6),
            SlideDeckStatus.Failed => ("failed", 100, "failed", "That bai", "Sinh slide that bai", 6),
            _ => ("queued", 0, "queued", "Cho xu ly", "Da tao job sinh slide", 1)
        };

        return new JobProgressPayload
        {
            DocumentId = deck.DocumentId,
            FolderProjectId = deck.FolderProjectId,
            Status = status,
            Percent = percent,
            Stage = stage,
            StageLabel = stageLabel,
            Message = message,
            Detail = deck.Status switch
            {
                SlideDeckStatus.Completed => $"Deck {Clean(deck.Title)} san sang de preview va export PDF",
                SlideDeckStatus.Failed => "Khong con state chi tiet trong RAM, can kiem tra log backend neu muon truy vet loi cu",
                SlideDeckStatus.GeneratingSlides when totalSlides > 0 => $"Da xong {completedSlides}/{totalSlides} slide",
                SlideDeckStatus.GeneratingOutline => "Dang tao cau truc tong the cho slide deck",
                _ => "Dang cho backend bat dau qua trinh sinh slide"
            },
            Current = totalSlides > 0 ? completedSlides : null,
            Total = totalSlides > 0 ? totalSlides : null,
            UnitLabel = totalSlides > 0 ? "slide" : string.Empty,
            StageIndex = stageIndex,
            StageCount = 6,
            ElapsedSeconds = elapsedSeconds,
            EstimatedRemainingSeconds = deck.Status switch
            {
                SlideDeckStatus.Completed or SlideDeckStatus.Failed => 0,
                _ => null
            },
            Error = deck.Status == SlideDeckStatus.Failed
                ? "Slide generation failed. Detailed in-memory error is unavailable after restart."
                : string.Empty,
            SlidesGenerated = totalSlides > 0 ? completedSlides : null,
            SlideDeckId = deck.Id
        };
    }

    private static JobProgressPayload BuildEmpty()
    {
        return new JobProgressPayload
        {
            Status = "queued",
            Percent = 0,
            Stage = "queued",
            StageLabel = "Cho xu ly",
            Message = "Chua co thong tin tien trinh",
            Detail = string.Empty,
            ElapsedSeconds = 0,
            EstimatedRemainingSeconds = null,
            Error = string.Empty
        };
    }

    private static string ResolveFileName(string? primary, string? fallback)
        => !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : Clean(fallback);

    private static string ResolveStatus(string? primary, DocumentStatus? fallbackStatus = null)
        => !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : fallbackStatus switch
        {
            DocumentStatus.Completed => "completed",
            DocumentStatus.Failed => "failed",
            DocumentStatus.Extracting or DocumentStatus.Analyzing => "running",
            _ => "queued"
        };

    private static string ResolveStatus(string? primary, SlideDeckStatus? fallbackStatus)
        => !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : fallbackStatus switch
        {
            SlideDeckStatus.Completed => "completed",
            SlideDeckStatus.Failed => "failed",
            SlideDeckStatus.GeneratingOutline or SlideDeckStatus.GeneratingSlides => "running",
            _ => "queued"
        };

    private static int ResolvePercent(int percent, DocumentStatus? fallbackStatus = null)
        => percent > 0 ? percent : fallbackStatus switch
        {
            DocumentStatus.Completed or DocumentStatus.Failed => 100,
            DocumentStatus.Analyzing => 80,
            DocumentStatus.Extracting => 25,
            _ => 0
        };

    private static int ResolvePercent(int percent, SlideDeckStatus? fallbackStatus)
        => percent > 0 ? percent : fallbackStatus switch
        {
            SlideDeckStatus.Completed or SlideDeckStatus.Failed => 100,
            SlideDeckStatus.GeneratingSlides => 40,
            SlideDeckStatus.GeneratingOutline => 18,
            _ => 0
        };

    private static string ResolveStage(string? primary, DocumentStatus? fallbackStatus = null)
        => !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : fallbackStatus switch
        {
            DocumentStatus.Extracting => "extracting",
            DocumentStatus.Analyzing => "analyzing",
            DocumentStatus.Completed => "completed",
            DocumentStatus.Failed => "failed",
            _ => "queued"
        };

    private static string ResolveStage(string? primary, SlideDeckStatus? fallbackStatus)
        => !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : fallbackStatus switch
        {
            SlideDeckStatus.GeneratingOutline => "generating-outline",
            SlideDeckStatus.GeneratingSlides => "generating-slides",
            SlideDeckStatus.Completed => "completed",
            SlideDeckStatus.Failed => "failed",
            _ => "queued"
        };

    private static string ResolveStageLabel(string? primary, DocumentStatus? fallbackStatus = null)
        => !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : fallbackStatus switch
        {
            DocumentStatus.Extracting => "Trich xuat van ban",
            DocumentStatus.Analyzing => "Phan tich noi dung",
            DocumentStatus.Completed => "Hoan tat",
            DocumentStatus.Failed => "That bai",
            _ => "Cho xu ly"
        };

    private static string ResolveStageLabel(string? primary, SlideDeckStatus? fallbackStatus)
        => !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : fallbackStatus switch
        {
            SlideDeckStatus.GeneratingOutline => "Dang tao outline",
            SlideDeckStatus.GeneratingSlides => "Dang sinh slide",
            SlideDeckStatus.Completed => "Hoan tat",
            SlideDeckStatus.Failed => "That bai",
            _ => "Cho xu ly"
        };

    private static string ResolveDocumentMessage(string? primary, DocumentStatus? fallbackStatus)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        return fallbackStatus switch
        {
            DocumentStatus.Extracting => "Dang trich xuat van ban tu tai lieu",
            DocumentStatus.Analyzing => "Dang phan tich noi dung sau OCR/extraction",
            DocumentStatus.Completed => "Da xu ly xong tai lieu",
            DocumentStatus.Failed => "Xu ly tai lieu that bai",
            _ => "Da xep hang xu ly tai lieu"
        };
    }

    private static string ResolveQuestionMessage(string? primary)
        => !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : "Dang cap nhat tien trinh sinh cau hoi";

    private static string ResolveSlideMessage(string? primary, SlideDeckStatus? fallbackStatus)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        return fallbackStatus switch
        {
            SlideDeckStatus.GeneratingOutline => "Dang len outline cho deck",
            SlideDeckStatus.GeneratingSlides => "Dang sinh noi dung tung slide",
            SlideDeckStatus.Completed => "Da tao xong bo slide",
            SlideDeckStatus.Failed => "Sinh slide that bai",
            _ => "Da tao job sinh slide"
        };
    }

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;
}
