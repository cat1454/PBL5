using ELearnGamePlatform.API.Configuration;
using ELearnGamePlatform.API.Contracts;
using ELearnGamePlatform.Core.Configuration;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using ELearnGamePlatform.Core.Options;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace ELearnGamePlatform.API.Services;

public class DocumentIngestionService : IDocumentIngestionService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IEnumerable<IDocumentProcessor> _documentProcessors;
    private readonly IOptions<FileUploadSettings> _fileUploadOptions;
    private readonly IDocumentProcessingJobStore _documentJobStore;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(
        IDocumentRepository documentRepository,
        IEnumerable<IDocumentProcessor> documentProcessors,
        IOptions<FileUploadSettings> fileUploadOptions,
        IDocumentProcessingJobStore documentJobStore,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<DocumentIngestionService> logger)
    {
        _documentRepository = documentRepository;
        _documentProcessors = documentProcessors;
        _fileUploadOptions = fileUploadOptions;
        _documentJobStore = documentJobStore;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task<Document> UploadDocumentAsync(IFormFile file, string userId, int? folderProjectId = null)
    {
        if (file == null || file.Length == 0)
        {
            throw new InvalidOperationException("No file provided");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("UserId is required");
        }

        var settings = _fileUploadOptions.Value;
        if (file.Length > settings.MaxFileSizeInBytes)
        {
            throw new InvalidOperationException($"File size exceeds {settings.MaxFileSizeInMB} MB limit");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!settings.IsExtensionAllowed(extension))
        {
            throw new InvalidOperationException($"File type {extension} is not supported. Allowed: {string.Join(", ", settings.AllowedExtensions)}");
        }

        var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        Directory.CreateDirectory(uploadsPath);

        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(uploadsPath, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var folderSourceOrder = folderProjectId.HasValue
            ? await _documentRepository.GetNextFolderSourceOrderAsync(folderProjectId.Value)
            : 0;

        var document = new Document
        {
            FileName = file.FileName,
            FileType = extension.TrimStart('.'),
            FilePath = filePath,
            FileSize = file.Length,
            UploadedBy = userId,
            Status = DocumentStatus.Uploaded,
            FolderProjectId = folderProjectId,
            FolderSourceOrder = folderSourceOrder,
            IncludeInFolderSlides = false
        };

        var createdDocument = await _documentRepository.CreateAsync(document);
        _documentJobStore.StartJob(createdDocument.Id, createdDocument.FileName);
        return createdDocument;
    }

    public void StartBackgroundProcessing(int documentId)
    {
        _ = Task.Run(() => ProcessDocumentAsync(documentId));
    }

    private async Task ProcessDocumentAsync(int documentId)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var documentRepository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var contentAnalyzer = scope.ServiceProvider.GetRequiredService<IContentAnalyzer>();
        var qualityGate = scope.ServiceProvider.GetRequiredService<IDocumentInputQualityGate>();
        var tokenBudgetPlanner = scope.ServiceProvider.GetRequiredService<ITokenBudgetPlanner>();
        var ocrSettings = scope.ServiceProvider.GetRequiredService<IOptions<OcrSettings>>().Value;
        var documentUnderstandingOptions = scope.ServiceProvider.GetRequiredService<IOptions<DocumentUnderstandingOptions>>().Value;
        var documentProcessors = scope.ServiceProvider.GetRequiredService<IEnumerable<IDocumentProcessor>>();
        var documentMarkdownParser = scope.ServiceProvider.GetRequiredService<IDocumentMarkdownParser>();
        var documentJobStore = scope.ServiceProvider.GetRequiredService<IDocumentProcessingJobStore>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<DocumentIngestionService>>();

        try
        {
            var document = await documentRepository.GetByIdAsync(documentId);
            if (document == null)
            {
                return;
            }

            documentJobStore.UpdateJob(documentId, state =>
            {
                state.Status = "running";
                state.Percent = 3;
                state.Stage = "preparing";
                state.StageLabel = "Chuan bi xu ly";
                state.Message = "Dang xac dinh cach xu ly tai lieu";
                state.Detail = $"Dinh dang file: {document.FileType}";
                state.StageIndex = 1;
                state.StageCount = 6;
                state.Error = null;
                UpdateEta(state);
            });

            document.Status = DocumentStatus.Extracting;
            await documentRepository.UpdateAsync(documentId, document);

            var processor = documentProcessors.FirstOrDefault(p => p.SupportedFileType(document.FileType));
            if (processor == null)
            {
                logger.LogError("No processor found for file type: {FileType}", document.FileType);
                document.Status = DocumentStatus.Failed;
                await documentRepository.UpdateAsync(documentId, document);
                documentJobStore.UpdateJob(documentId, state =>
                {
                    state.Status = "failed";
                    state.Percent = 100;
                    state.Stage = "failed";
                    state.StageLabel = "That bai";
                    state.Message = "Khong tim thay processor phu hop";
                    state.Detail = $"Khong ho tro file type {document.FileType}";
                    state.Error = "No processor found for file type";
                    state.StageIndex = 6;
                    state.StageCount = 6;
                    state.EstimatedRemainingSeconds = 0;
                    UpdateEta(state);
                });
                return;
            }

            var extractionProgress = new Progress<DocumentProcessingProgressUpdate>(update =>
            {
                documentJobStore.UpdateJob(documentId, state =>
                {
                    ApplyProgressUpdate(state, MapProgressUpdate(update, 8, 58, "extracting", "Trich xuat van ban", 2, 6));
                });
            });

            string legacyExtractedText = await processor.ExtractTextAsync(document.FilePath, document.FileType, extractionProgress)
                ?? string.Empty;
            var pageQualityReport = (processor as IDocumentInputQualityReportProvider)?.LastInputQualityReport;
            var doclingMarkdown = await documentMarkdownParser.TryParseAsync(document.FilePath);
            string extractedText = doclingMarkdown ?? legacyExtractedText;
            logger.LogInformation(
                "Document {DocumentId} selected {ExtractionSource} text for analysis: selectedChars={SelectedCharacterCount}, legacyChars={LegacyCharacterCount}.",
                documentId,
                doclingMarkdown == null ? "legacy" : "Docling Markdown",
                extractedText.Length,
                legacyExtractedText.Length);
            var understandingResult = await ApplyDocumentUnderstandingAsync(
                document,
                extractedText,
                documentUnderstandingOptions,
                pageQualityReport,
                scope.ServiceProvider,
                logger);
            document.ExtractedText = extractedText;
            var qualityResult = qualityGate.Evaluate(extractedText);
            var budgetPlan = tokenBudgetPlanner.PlanText(extractedText, "analysis");
            ApplyPageQualityCalibration(qualityResult, pageQualityReport, budgetPlan);
            var metadata = document.GetProcessingMetadata();
            metadata.InputQuality = qualityResult;
            metadata.PageQualityReport = pageQualityReport;
            metadata.AnalysisTokenBudget = budgetPlan;
            document.SetProcessingMetadata(metadata);

            logger.LogInformation(
                "Document {DocumentId} input quality: {Classification}, score={QualityScore}, chars={CharCount}, words={WordCount}, estimatedTokens={EstimatedTokenCount}, withinBudget={IsWithinBudget}",
                documentId,
                qualityResult.Classification,
                qualityResult.QualityScore,
                qualityResult.CharCount,
                qualityResult.WordCount,
                qualityResult.EstimatedTokenCount,
                budgetPlan.IsWithinBudget);

            if (pageQualityReport != null && ocrSettings.EnableQualityProfile)
            {
                logger.LogInformation(
                    "Document {DocumentId} page quality: total={TotalPages}, direct={DirectTextPages}, ocr={OcrPages}, empty={EmptyPages}, failed={FailedPages}, lowQuality={LowQualityPages}, avgQuality={AveragePageQuality}, estimatedTokens={EstimatedTokens}",
                    documentId,
                    pageQualityReport.TotalPages,
                    pageQualityReport.DirectTextPages,
                    pageQualityReport.OcrPages,
                    pageQualityReport.EmptyPages,
                    pageQualityReport.FailedPages,
                    pageQualityReport.LowQualityPages,
                    pageQualityReport.AveragePageQuality,
                    pageQualityReport.TotalEstimatedTokens);
            }
            else if (pageQualityReport != null)
            {
                logger.LogInformation(
                    "Document {DocumentId} page extraction: total={TotalPages}, direct={DirectTextPages}, ocr={OcrPages}, empty={EmptyPages}, failed={FailedPages}, estimatedTokens={EstimatedTokens}",
                    documentId,
                    pageQualityReport.TotalPages,
                    pageQualityReport.DirectTextPages,
                    pageQualityReport.OcrPages,
                    pageQualityReport.EmptyPages,
                    pageQualityReport.FailedPages,
                    pageQualityReport.TotalEstimatedTokens);
            }

            var qualityWarnings = qualityResult.Warnings
                .Concat(budgetPlan.Warnings)
                .Concat(pageQualityReport?.Warnings ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (qualityWarnings.Count > 0)
            {
                logger.LogWarning(
                    "Document {DocumentId} quality/budget warnings: total={WarningCount}, examples={WarningExamples}",
                    documentId,
                    qualityWarnings.Count,
                    string.Join(" | ", qualityWarnings.Take(5)));
            }

            documentJobStore.UpdateJob(documentId, state =>
            {
                state.Status = "running";
                state.Percent = Math.Max(state.Percent, 60);
                state.Stage = "quality-gate";
                state.StageLabel = "Kiểm tra chất lượng";
                state.Message = "Đã xong bước trích xuất văn bản, đang kiểm tra chất lượng đầu vào cho AI";
                state.Detail = $"Quality={qualityResult.Classification}, score={qualityResult.QualityScore}, tokens={qualityResult.EstimatedTokenCount}/{budgetPlan.MaxInputTokens}";
                state.DocumentConfidence = understandingResult?.Confidence;
                state.QualityStatus = understandingResult?.Status;
                state.NeedsReview = understandingResult?.Quality?.NeedsReview;
                state.StageIndex = 3;
                state.StageCount = 6;
                UpdateEta(state);
            });

            if (qualityResult.IsRejected)
            {
                document.Status = DocumentStatus.Failed;
                document.UpdatedAt = DateTime.UtcNow;
                await documentRepository.UpdateAsync(documentId, document);

                documentJobStore.UpdateJob(documentId, state =>
                {
                    state.Status = "failed";
                    state.Percent = 100;
                    state.Stage = "quality-gate";
                    state.StageLabel = "Không đủ chất lượng";
                    state.Message = "Tài liệu bị từ chối trước bước AI vì văn bản trích xuất quá thấp";
                    state.Detail = string.Join(" ", qualityResult.Warnings.DefaultIfEmpty("Document input quality gate rejected the extracted text."));
                    state.Error = "Document input quality gate rejected the extracted text";
                    state.StageIndex = 3;
                    state.StageCount = 6;
                    state.EstimatedRemainingSeconds = 0;
                    UpdateEta(state);
                });

                logger.LogWarning(
                    "Document {DocumentId} rejected before AI analysis. Qwen analysis was not called.",
                    documentId);
                return;
            }

            document.Status = DocumentStatus.Analyzing;
            await documentRepository.UpdateAsync(documentId, document);

            var analysisProgress = new Progress<DocumentProcessingProgressUpdate>(update =>
            {
                documentJobStore.UpdateJob(documentId, state =>
                {
                    ApplyProgressUpdate(state, MapProgressUpdate(update, 62, 96, "analyzing", "Phan tich noi dung", update.Stage == "analyzing-chunks" ? 4 : 5, 6));
                });
            });

            var processedContent = await contentAnalyzer.AnalyzeContentAsync(extractedText, understandingResult, analysisProgress);
            var analysisChunkBudget = tokenBudgetPlanner.PlanChunks(processedContent.CoverageMap, "analysis");
            document.SetMainTopics(processedContent.MainTopics);
            document.SetKeyPoints(processedContent.KeyPoints);
            document.SetCoverageMap(processedContent.CoverageMap);
            document.SetProcessingMetadata(new DocumentProcessingMetadata
            {
                DocumentType = processedContent.DocumentType,
                Language = processedContent.Language,
                Title = processedContent.Title,
                MainContentStartPage = processedContent.MainContentStartPage,
                Structure = processedContent.Structure,
                ExcludedContent = processedContent.ExcludedContent,
                InputQuality = qualityResult,
                PageQualityReport = pageQualityReport,
                AnalysisTokenBudget = analysisChunkBudget,
                TotalChunks = analysisChunkBudget.TotalChunks,
                AverageChunkTokens = analysisChunkBudget.AverageChunkTokens,
                SelectedChunks = analysisChunkBudget.SelectedChunks.Count,
                SelectedTextTokens = analysisChunkBudget.SelectedTextTokens,
                BudgetFillRatio = analysisChunkBudget.BudgetFillRatio,
                IncludeFullChunkText = analysisChunkBudget.IncludeFullChunkText,
                OmittedChunks = analysisChunkBudget.OmittedChunks.Count
            });
            document.Summary = processedContent.Summary;
            document.Language = processedContent.Language;

            logger.LogInformation(
                "Document {DocumentId} analysis chunk budget: totalChunks={TotalChunks}, averageChunkTokens={AverageChunkTokens}, selectedChunks={SelectedChunks}, selectedTextTokens={SelectedTextTokens}, budgetFillRatio={BudgetFillRatio:P0}, includeFullChunkText={IncludeFullChunkText}, omittedChunks={OmittedChunks}",
                documentId,
                analysisChunkBudget.TotalChunks,
                analysisChunkBudget.AverageChunkTokens,
                analysisChunkBudget.SelectedChunks.Count,
                analysisChunkBudget.SelectedTextTokens,
                analysisChunkBudget.BudgetFillRatio,
                analysisChunkBudget.IncludeFullChunkText,
                analysisChunkBudget.OmittedChunks.Count);

            documentJobStore.UpdateJob(documentId, state =>
            {
                state.Status = "running";
                state.Percent = 98;
                state.Stage = "saving";
                state.StageLabel = "Luu ket qua";
                state.Message = "Dang luu ket qua phan tich vao he thong";
                state.Detail = $"Type={processedContent.DocumentType}, cleanChunk={processedContent.CoverageMap.Count}, excluded={processedContent.ExcludedContent.Count}";
                state.StageIndex = 6;
                state.StageCount = 6;
                state.EstimatedRemainingSeconds = 1;
                UpdateEta(state);
            });

            document.Status = DocumentStatus.Completed;
            document.UpdatedAt = DateTime.UtcNow;
            await documentRepository.UpdateAsync(documentId, document);

            documentJobStore.UpdateJob(documentId, state =>
            {
                state.Status = "completed";
                state.Percent = 100;
                state.Stage = "completed";
                state.StageLabel = "Hoan tat";
                state.Message = "Da xu ly xong tai lieu";
                state.Detail = "San sang tao cau hoi va hoc bang game";
                state.DocumentConfidence = understandingResult?.Confidence;
                state.QualityStatus = understandingResult?.Status;
                state.NeedsReview = understandingResult?.Quality?.NeedsReview;
                state.StageIndex = 6;
                state.StageCount = 6;
                state.EstimatedRemainingSeconds = 0;
                UpdateEta(state);
            });

            logger.LogInformation("Document processed successfully: {DocumentId}", documentId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing document: {DocumentId}", documentId);

            var document = await documentRepository.GetByIdAsync(documentId);
            if (document != null)
            {
                document.Status = DocumentStatus.Failed;
                await documentRepository.UpdateAsync(documentId, document);
            }

            documentJobStore.UpdateJob(documentId, state =>
            {
                state.Status = "failed";
                state.Percent = 100;
                state.Stage = "failed";
                state.StageLabel = "That bai";
                state.Message = "Xu ly tai lieu that bai";
                state.Detail = ex.Message;
                state.Error = ex.Message;
                state.StageIndex = 6;
                state.StageCount = 6;
                state.EstimatedRemainingSeconds = 0;
                UpdateEta(state);
            });
        }
    }

    private static DocumentProcessingProgressUpdate MapProgressUpdate(
        DocumentProcessingProgressUpdate update,
        int startPercent,
        int endPercent,
        string defaultStage,
        string defaultStageLabel,
        int defaultStageIndex,
        int stageCount)
    {
        return new DocumentProcessingProgressUpdate
        {
            Percent = startPercent + (int)Math.Round((endPercent - startPercent) * (Math.Clamp(update.Percent, 0, 100) / 100d)),
            Stage = string.IsNullOrWhiteSpace(update.Stage) ? defaultStage : update.Stage,
            StageLabel = string.IsNullOrWhiteSpace(update.StageLabel) ? defaultStageLabel : update.StageLabel,
            Message = update.Message,
            Detail = update.Detail,
            Current = update.Current,
            Total = update.Total,
            UnitLabel = update.UnitLabel,
            StageIndex = update.StageIndex ?? defaultStageIndex,
            StageCount = update.StageCount ?? stageCount
        };
    }

    private static void ApplyProgressUpdate(DocumentProcessingJobState state, DocumentProcessingProgressUpdate update)
    {
        state.Status = update.Stage == "failed" ? "failed" : "running";
        state.Percent = Math.Clamp(update.Percent, 0, 100);
        state.Stage = string.IsNullOrWhiteSpace(update.Stage) ? state.Stage : update.Stage;
        state.StageLabel = string.IsNullOrWhiteSpace(update.StageLabel) ? state.StageLabel : update.StageLabel;
        state.Message = update.Message;
        state.Detail = update.Detail;
        state.Current = update.Current;
        state.Total = update.Total;
        state.UnitLabel = update.UnitLabel;
        state.StageIndex = update.StageIndex ?? state.StageIndex;
        state.StageCount = update.StageCount ?? state.StageCount;
        UpdateEta(state);
    }

    private static void UpdateEta(DocumentProcessingJobState state)
    {
        var now = DateTime.UtcNow;
        var elapsedSeconds = Math.Max(0, (int)Math.Round((DateTime.UtcNow - state.CreatedAt).TotalSeconds));
        state.ElapsedSeconds = elapsedSeconds;

        if (state.Status is "completed" or "failed")
        {
            state.EstimatedRemainingSeconds = 0;
            return;
        }

        if (state.Current.HasValue && state.Total.HasValue && state.Total.Value > 0)
        {
            var current = Math.Clamp(state.Current.Value, 0, state.Total.Value);
            var anchorNeedsReset =
                state.EtaAnchorAt == null ||
                !string.Equals(state.EtaAnchorStage, state.Stage, StringComparison.OrdinalIgnoreCase) ||
                state.EtaAnchorTotal != state.Total ||
                current < (state.EtaAnchorCurrent ?? 0);

            if (anchorNeedsReset)
            {
                state.EtaAnchorStage = state.Stage;
                state.EtaAnchorTotal = state.Total;
                state.EtaAnchorCurrent = Math.Max(0, current - 1);
                state.EtaAnchorAt = now;
            }

            if (current >= state.Total.Value)
            {
                state.EstimatedRemainingSeconds = 0;
                return;
            }

            var stageElapsedSeconds = Math.Max(0.1d, (now - state.EtaAnchorAt!.Value).TotalSeconds);
            var processedUnits = Math.Max(0, current - (state.EtaAnchorCurrent ?? 0));
            var remainingUnits = Math.Max(0, state.Total.Value - current);

            if (processedUnits > 0 && remainingUnits > 0)
            {
                var secondsPerUnit = stageElapsedSeconds / processedUnits;
                state.EstimatedRemainingSeconds = Math.Max(1, (int)Math.Round(secondsPerUnit * remainingUnits));
                return;
            }
        }

        if (state.Percent <= 3)
        {
            state.EstimatedRemainingSeconds = null;
            return;
        }

        var estimatedTotalSeconds = elapsedSeconds / Math.Max(0.03d, state.Percent / 100d);
        var estimatedRemaining = Math.Max(1, (int)Math.Round(estimatedTotalSeconds - elapsedSeconds));
        state.EstimatedRemainingSeconds = estimatedRemaining;
    }

    private static void ApplyPageQualityCalibration(
        DocumentInputQualityResult qualityResult,
        DocumentInputQualityReport? pageQualityReport,
        TokenBudgetPlan budgetPlan)
    {
        if (pageQualityReport == null)
        {
            return;
        }

        var readableStatus = pageQualityReport.QualityStatus is DocumentQualityStatuses.Accepted
            or DocumentQualityStatuses.AcceptedWithWarnings
            or DocumentQualityStatuses.NeedsReview;
        if (!readableStatus || pageQualityReport.BodyPageCount <= 0)
        {
            return;
        }

        if (pageQualityReport.BodyPageQualityAverage >= 60 && pageQualityReport.DirectTextPages >= Math.Max(1, pageQualityReport.TotalPages / 2))
        {
            qualityResult.Classification = DocumentInputQualityClassifications.UsableWithWarning;
            qualityResult.QualityScore = Math.Max(qualityResult.QualityScore, (int)Math.Round(pageQualityReport.BodyPageQualityAverage));
            qualityResult.Warnings.Add("Page-level calibration accepted readable direct-text body pages despite cover, footnote, or token-budget artifacts.");
        }
        else if (pageQualityReport.BodyPageQualityAverage >= 45 && !budgetPlan.IsWithinBudget)
        {
            qualityResult.Classification = DocumentInputQualityClassifications.NeedReview;
            qualityResult.QualityScore = Math.Max(qualityResult.QualityScore, (int)Math.Round(pageQualityReport.BodyPageQualityAverage));
            qualityResult.Warnings.Add("Page-level calibration marked the document NeedsReview: readable body pages exist, but chunk selection should control token budget.");
        }

        qualityResult.Warnings = qualityResult.Warnings
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<DocumentUnderstandingResult?> ApplyDocumentUnderstandingAsync(
        Document document,
        string? legacyExtractedText,
        DocumentUnderstandingOptions options,
        DocumentInputQualityReport? pageQualityReport,
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        try
        {
            var orchestrator = serviceProvider.GetRequiredService<IDocumentUnderstandingOrchestrator>();
            var runRepository = serviceProvider.GetRequiredService<IDocumentUnderstandingRunRepository>();
            var result = await orchestrator.UnderstandAsync(
                document.Id,
                document.FilePath,
                legacyExtractedText,
                pageQualityReport);

            await runRepository.CreateAsync(BuildUnderstandingRun(document.Id, result, options));

            logger.LogInformation(
                "DocumentUnderstanding completed and persisted for document {DocumentId}: status={Status}, confidence={Confidence}, combinedTextChars={CombinedTextChars}",
                document.Id,
                result.Status,
                result.Confidence,
                result.CombinedText?.Length ?? 0);

            return result;
        }
        catch (Exception ex)
        {
            await SaveFailedUnderstandingRunAsync(
                document.Id,
                legacyExtractedText,
                ex.Message,
                serviceProvider,
                logger);

            logger.LogWarning(
                ex,
                "DocumentUnderstanding failed for document {DocumentId} at {FilePath}; falling back to legacy extracted text. LegacyTextChars={LegacyTextChars}",
                document.Id,
                document.FilePath,
                legacyExtractedText?.Length ?? 0);

            return null;
        }
    }

    private static DocumentUnderstandingRun BuildUnderstandingRun(
        int documentId,
        DocumentUnderstandingResult result,
        DocumentUnderstandingOptions options)
    {
        var failureReasons = result.Warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DocumentUnderstandingRun
        {
            DocumentId = documentId,
            Status = string.IsNullOrWhiteSpace(result.Status) ? "Completed" : result.Status,
            DocumentConfidence = result.Confidence,
            NeedsReview = result.Quality?.NeedsReview
                ?? (result.Confidence < options.MinAutoGenerateConfidence || failureReasons.Count > 0),
            CombinedText = result.CombinedText,
            ResultJson = JsonSerializer.Serialize(new
            {
                result.Pages,
                result.Regions,
                result.PresentationContract,
                result.Warnings,
                result.Quality
            }),
            FailureReasonsJson = JsonSerializer.Serialize(failureReasons)
        };
    }

    private static async Task SaveFailedUnderstandingRunAsync(
        int documentId,
        string? legacyExtractedText,
        string failureReason,
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        try
        {
            var runRepository = serviceProvider.GetRequiredService<IDocumentUnderstandingRunRepository>();
            var failureReasons = new[] { failureReason };
            await runRepository.CreateAsync(new DocumentUnderstandingRun
            {
                DocumentId = documentId,
                Status = "Failed",
                DocumentConfidence = null,
                NeedsReview = true,
                CombinedText = legacyExtractedText,
                ResultJson = JsonSerializer.Serialize(new
                {
                    Pages = Array.Empty<PageUnderstandingResult>(),
                    Regions = Array.Empty<DocumentRegion>(),
                    Warnings = failureReasons
                }),
                FailureReasonsJson = JsonSerializer.Serialize(failureReasons)
            });
        }
        catch (Exception saveException)
        {
            logger.LogWarning(
                saveException,
                "Failed to persist DocumentUnderstanding failure run for document {DocumentId}",
                documentId);
        }
    }
}
