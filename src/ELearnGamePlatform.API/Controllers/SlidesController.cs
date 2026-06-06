using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ELearnGamePlatform.API.Contracts;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SlidesController : AuthenticatedControllerBase
{
    private const int SlideLowConfidenceThreshold = 86;
    private static readonly HashSet<string> ExplicitlyExcludedScopeClassifications = new(StringComparer.OrdinalIgnoreCase)
    {
        ChunkClassifications.FrontMatter,
        ChunkClassifications.TableOfContents,
        ChunkClassifications.Reference,
        ChunkClassifications.Appendix,
        ChunkClassifications.Noise
    };

    private readonly IDocumentRepository _documentRepository;
    private readonly IFolderProjectRepository _folderProjectRepository;
    private readonly ISlideDeckRepository _slideDeckRepository;
    private readonly ISlideGenerator _slideGenerator;
    private readonly ISlideExportService _slideExportService;
    private readonly ISlideImageService _slideImageService;
    private readonly IDocumentGenerationReadinessService _generationReadinessService;
    private readonly ISlideGenerationJobStore _jobStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlidesController> _logger;

    public SlidesController(
        IDocumentRepository documentRepository,
        IFolderProjectRepository folderProjectRepository,
        ISlideDeckRepository slideDeckRepository,
        ISlideGenerator slideGenerator,
        ISlideExportService slideExportService,
        ISlideImageService slideImageService,
        IDocumentGenerationReadinessService generationReadinessService,
        ISlideGenerationJobStore jobStore,
        IServiceScopeFactory scopeFactory,
        ILogger<SlidesController> logger)
    {
        _documentRepository = documentRepository;
        _folderProjectRepository = folderProjectRepository;
        _slideDeckRepository = slideDeckRepository;
        _slideGenerator = slideGenerator;
        _slideExportService = slideExportService;
        _slideImageService = slideImageService;
        _generationReadinessService = generationReadinessService;
        _jobStore = jobStore;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [HttpPost("generate/start")]
    public async Task<IActionResult> StartGenerateSlides([FromBody] GenerateSlidesRequest request)
    {
        if (!IsValidSlideCount(request.DesiredSlideCount))
        {
            return BadRequest("DesiredSlideCount must be between 5 and 18");
        }

        if (!IsSupportedSpeedMode(request.SpeedMode))
        {
            return BadRequest("Unsupported slide generation speed mode.");
        }

        var document = await _documentRepository.GetByIdAsync(request.DocumentId);
        if (document == null)
        {
            return NotFound("Document not found");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        try
        {
            _ = await _slideDeckRepository.GetLatestByDocumentIdAsync(request.DocumentId);
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }

        var generationReadiness = await _generationReadinessService.GetReadinessAsync(document, request.ConfirmLowConfidence);
        if (generationReadiness.Blocked)
        {
            return GenerationGateBlocked(generationReadiness);
        }

        var jobId = _jobStore.CreateJob(request.DocumentId, request.DesiredSlideCount, CurrentUserIdAsString);
        _jobStore.TryGetJob(jobId, out var state);
        _ = Task.Run(() => RunGenerateSlidesJobAsync(jobId, new SlideGenerationTarget
        {
            DocumentId = request.DocumentId,
            DesiredSlideCount = request.DesiredSlideCount,
            ThemeKey = request.ThemeKey,
            Audience = request.Audience,
            Tone = request.Tone,
            NarrativeGoal = request.NarrativeGoal,
            LanguageStyle = request.LanguageStyle,
            Mode = request.Mode,
            SpeedMode = request.SpeedMode,
            ScopePolicy = request.ScopePolicy,
            SelectedSectionIds = request.SelectedSectionIds,
            SourceIds = request.SourceIds,
            ConfirmLowConfidence = request.ConfirmLowConfidence
        }));

        return Accepted(new
        {
            jobId,
            status = "queued",
            progressUrl = $"/api/slides/generate/progress/{jobId}",
            resultUrl = $"/api/slides/document/{request.DocumentId}",
            generationReadiness,
            progress = state == null ? null : JobProgressPayloadFactory.BuildSlide(state)
        });
    }

    [HttpGet("generate/progress/{jobId}")]
    public IActionResult GetGenerateProgress(string jobId)
    {
        if (!_jobStore.TryGetJob(jobId, out var state) || state == null)
        {
            return ApiNotFound("job_not_found", "Job not found");
        }

        var authResult = EnsureCurrentUserMatches(state.CreatedByUserId);
        if (authResult != null)
        {
            return authResult;
        }

        return Ok(JobProgressPayloadFactory.BuildSlide(state));
    }

    [HttpPost("generate/{jobId}/pause")]
    public IActionResult PauseGenerateSlides(string jobId)
        => ChangeSlideJobState(jobId, _jobStore.PauseJob, "job_not_pausable", "Slide generation cannot be paused in its current state.");

    [HttpPost("generate/{jobId}/resume")]
    public IActionResult ResumeGenerateSlides(string jobId)
        => ChangeSlideJobState(jobId, _jobStore.ResumeJob, "job_not_resumable", "Slide generation cannot be resumed in its current state.");

    [HttpPost("generate/{jobId}/cancel")]
    public IActionResult CancelGenerateSlides(string jobId)
        => ChangeSlideJobState(jobId, _jobStore.CancelJob, "job_not_cancellable", "Slide generation cannot be cancelled in its current state.");

    [HttpDelete("{deckId:int}")]
    public async Task<IActionResult> DeleteDeck(int deckId)
    {
        var deck = await _slideDeckRepository.GetByIdAsync(deckId);
        if (deck == null)
        {
            return ApiNotFound("deck_not_found", "Slide deck not found.");
        }

        var authResult = EnsureOwnerAccess(deck.Document?.UploadedBy ?? deck.FolderProject?.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        var hasActiveJob = deck.FolderProjectId.HasValue
            ? _jobStore.TryGetLatestActiveJobForFolder(deck.FolderProjectId.Value, out _)
            : deck.DocumentId.HasValue && _jobStore.TryGetLatestActiveJobForDocument(deck.DocumentId.Value, out _);
        if (hasActiveJob)
        {
            return ApiConflict("generation_active", "Cancel the active slide generation job before deleting this deck.");
        }

        return await _slideDeckRepository.DeleteDeckAsync(deck.Id)
            ? NoContent()
            : ApiNotFound("deck_not_found", "Slide deck not found.");
    }

    [HttpGet("document/{documentId}")]
    public async Task<IActionResult> GetDeckByDocument(int documentId)
    {
        var document = await _documentRepository.GetByIdAsync(documentId);
        if (document == null)
        {
            return NotFound("Document not found");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        try
        {
            var deck = await _slideDeckRepository.GetLatestByDocumentIdAsync(documentId);
            if (deck == null)
            {
                return NoContent();
            }

            _jobStore.TryGetLatestJobForDocument(documentId, out var jobState);
            return Ok(BuildDeckPayload(deck, jobState));
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }
    }

    [HttpGet("document/{documentId}/html")]
    public async Task<IActionResult> GetDeckHtml(int documentId)
    {
        var document = await _documentRepository.GetByIdAsync(documentId);
        if (document == null)
        {
            return NotFound("Document not found");
        }

        var authResult = EnsureOwnerAccess(document.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        try
        {
            var deck = await _slideDeckRepository.GetLatestByDocumentIdAsync(documentId);
            if (deck == null)
            {
                return NotFound("Slide deck not found");
            }

            var html = _slideGenerator.RenderDeckHtml(deck, deck.Items.OrderBy(item => item.SlideIndex).ToList());
            return Content(html, "text/html; charset=utf-8");
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }
    }

    [HttpGet("{deckId:int}/export/html")]
    public async Task<IActionResult> ExportDeckHtml(int deckId)
    {
        try
        {
            var access = await GetDeckForExportAsync(deckId);
            if (access.ErrorResult != null || access.Deck == null)
            {
                return access.ErrorResult ?? NotFound("Slide deck not found");
            }

            var items = access.Deck.Items.OrderBy(item => item.SlideIndex).ToList();
            var html = _slideExportService.RenderHtmlFile(access.Deck, items);
            var bytes = Encoding.UTF8.GetBytes(html);
            return File(bytes, "text/html; charset=utf-8", BuildExportFileName(access.Deck, "html"));
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }
    }

    [HttpGet("{deckId:int}/export/print")]
    public async Task<IActionResult> ExportDeckPrintView(int deckId)
    {
        try
        {
            var access = await GetDeckForExportAsync(deckId);
            if (access.ErrorResult != null || access.Deck == null)
            {
                return access.ErrorResult ?? NotFound("Slide deck not found");
            }

            var items = access.Deck.Items.OrderBy(item => item.SlideIndex).ToList();
            var html = _slideExportService.RenderPrintHtml(access.Deck, items);
            return Content(html, "text/html; charset=utf-8");
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }
    }

    [HttpGet("{deckId:int}/export/pptx")]
    public async Task<IActionResult> ExportDeckPptx(int deckId)
    {
        try
        {
            var access = await GetDeckForExportAsync(deckId);
            if (access.ErrorResult != null || access.Deck == null)
            {
                return access.ErrorResult ?? NotFound("Slide deck not found");
            }

            var items = access.Deck.Items.OrderBy(item => item.SlideIndex).ToList();
            var pptx = _slideExportService.BuildPptx(access.Deck, items);
            return File(
                pptx,
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                BuildExportFileName(access.Deck, "pptx"));
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }
    }

    [HttpPut("{deckId}/items/{itemId}")]
    public async Task<IActionResult> UpdateSlideItem(int deckId, int itemId, [FromBody] UpdateSlideItemRequest request)
    {
        var deckAccess = await EnsureDeckAccessAsync(deckId);
        if (deckAccess != null)
        {
            return deckAccess;
        }

        try
        {
            var item = await _slideDeckRepository.GetItemAsync(deckId, itemId);
            if (item == null)
            {
                return NotFound("Slide item not found");
            }

            if (request.EditorState != null)
            {
                item.ApplyEditorState(request.EditorState);
                item.AccentTone = request.AccentTone?.Trim() ?? item.AccentTone;
            }
            else
            {
                item.Heading = string.IsNullOrWhiteSpace(request.Heading) ? item.Heading : request.Heading.Trim();
                item.Subheading = request.Subheading?.Trim();
                item.Goal = request.Goal?.Trim();
                item.KeyMessage = request.Goal?.Trim() ?? item.KeyMessage;
                item.SpeakerNotes = request.SpeakerNotes?.Trim();
                item.AccentTone = request.AccentTone?.Trim();
                item.SetBodyBlocks(request.BodyBlocks?
                    .Where(block => !string.IsNullOrWhiteSpace(block))
                    .Select(block => block.Trim())
                    .ToList() ?? new List<string>());
                item.SetEditorState(item.BuildDefaultEditorState());
            }
            item.VerifierScore = null;
            item.SetVerifierIssues(new List<string>
            {
                "Slide da duoc chinh sua thu cong sau khi verifier chay.",
                "Can sinh lai hoac verifier lai neu muon diem tin cay moi."
            });
            item.SetEvidenceDebug(null);
            RefreshImageScaffold(item);

            await _slideDeckRepository.UpdateItemAsync(item);
            return Ok(BuildSlideItemPayload(item));
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }
    }

    [HttpPost("{deckId}/items/{itemId}/images/refresh")]
    public async Task<IActionResult> RefreshSlideItemImages(int deckId, int itemId)
    {
        var deckAccess = await EnsureDeckAccessAsync(deckId);
        if (deckAccess != null)
        {
            return deckAccess;
        }

        try
        {
            var item = await _slideImageService.RefreshImagesAsync(deckId, itemId, HttpContext.RequestAborted);
            if (item == null)
            {
                return NotFound("Slide item not found");
            }

            return Ok(BuildSlideItemPayload(item));
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }
    }

    [HttpGet("folders/{folderId}")]
    public async Task<IActionResult> GetDeckByFolder(int folderId)
    {
        var folder = await GetFolderAsync(folderId);
        if (folder == null)
        {
            return NotFound("Folder project not found");
        }

        var authResult = EnsureOwnerAccess(folder.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        try
        {
            var deck = await _slideDeckRepository.GetLatestByFolderIdAsync(folderId);
            if (deck == null)
            {
                return NoContent();
            }

            var sources = await _documentRepository.GetByFolderProjectIdAsync(folderId);
            _jobStore.TryGetLatestJobForFolder(folderId, out var jobState);
            return Ok(BuildDeckPayload(deck, jobState, IsFolderDeckStale(deck, sources)));
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }
    }

    [HttpGet("folders/{folderId}/html")]
    public async Task<IActionResult> GetFolderDeckHtml(int folderId)
    {
        var folder = await GetFolderAsync(folderId);
        if (folder == null)
        {
            return NotFound("Folder project not found");
        }

        var authResult = EnsureOwnerAccess(folder.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        try
        {
            var deck = await _slideDeckRepository.GetLatestByFolderIdAsync(folderId);
            if (deck == null)
            {
                return NotFound("Slide deck not found");
            }

            var html = _slideGenerator.RenderDeckHtml(deck, deck.Items.OrderBy(item => item.SlideIndex).ToList());
            return Content(html, "text/html; charset=utf-8");
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }
    }

    [HttpPost("{deckId}/items/{itemId}/images/select")]
    public async Task<IActionResult> SelectSlideItemImage(int deckId, int itemId, [FromBody] SelectSlideImageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CandidateKey))
        {
            return BadRequest("CandidateKey is required");
        }

        var deckAccess = await EnsureDeckAccessAsync(deckId);
        if (deckAccess != null)
        {
            return deckAccess;
        }

        try
        {
            var item = await _slideImageService.SelectImageAsync(deckId, itemId, request.CandidateKey.Trim(), HttpContext.RequestAborted);
            if (item == null)
            {
                return NotFound("Image candidate not found");
            }

            return Ok(BuildSlideItemPayload(item));
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }
    }

    private async Task RunGenerateSlidesJobAsync(string jobId, SlideGenerationTarget target)
    {
        SlideDeck? persistedDeck = null;
        var totalStopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "[SlideGen:{JobId}] Phase=job Step=started FolderId={FolderId} DocumentId={DocumentId} SourceIds={SourceIds} SelectedSectionCount={SelectedSectionCount} DesiredSlideCount={DesiredSlideCount} SpeedMode={SpeedMode}",
                jobId,
                target.FolderProjectId,
                target.DocumentId,
                target.SourceIds == null ? string.Empty : string.Join(",", target.SourceIds),
                target.SelectedSectionIds?.Count ?? 0,
                target.DesiredSlideCount,
                NormalizeSpeedMode(target.SpeedMode));

            using var scope = _scopeFactory.CreateScope();
            var documentRepository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var folderProjectRepository = scope.ServiceProvider.GetRequiredService<IFolderProjectRepository>();
            var slideDeckRepository = scope.ServiceProvider.GetRequiredService<ISlideDeckRepository>();
            var slideGenerator = scope.ServiceProvider.GetRequiredService<ISlideGenerator>();
            var slideImageService = scope.ServiceProvider.GetRequiredService<ISlideImageService>();
            var generationReadinessService = scope.ServiceProvider.GetRequiredService<IDocumentGenerationReadinessService>();

            if (!await _jobStore.WaitForExecutionAsync(jobId))
            {
                return;
            }

            UpdateJob(jobId, state =>
            {
                state.Status = "running";
                state.Percent = 3;
                state.Stage = target.FolderProjectId.HasValue ? "validating-folder" : "validating-document";
                state.StageLabel = target.FolderProjectId.HasValue ? "Kiem tra folder" : "Kiem tra tai lieu";
                state.Message = "Dang kiem tra du lieu truoc khi tao slide";
                state.Detail = target.FolderProjectId.HasValue
                    ? "Can it nhat 1 source da OCR/analyze xong va duoc chon cho slide"
                    : "Can document da OCR va phan tich xong";
                state.StageIndex = 1;
                state.StageCount = 6;
                state.Error = null;
                UpdateEta(state);
            });

            var contextStopwatch = Stopwatch.StartNew();
            var context = await ResolveGenerationContextAsync(target, documentRepository, folderProjectRepository, generationReadinessService);
            contextStopwatch.Stop();
            _logger.LogInformation(
                "[SlideGen:{JobId}] Phase=validation Step=context-resolved FolderId={FolderId} DocumentId={DocumentId} Success={Success} SectionCount={SectionCount} ChunkCount={ChunkCount} TextLength={TextLength} DurationMs={DurationMs}",
                jobId,
                context.FolderProjectId,
                context.DocumentId,
                context.Success,
                context.ProcessedContent.Structure?.Count ?? 0,
                context.ProcessedContent.CoverageMap.Count,
                context.ExtractedText.Length,
                contextStopwatch.ElapsedMilliseconds);
            if (!context.Success)
            {
                FailJob(jobId, context.Error ?? "Khong the tao slide deck", context.ErrorMessage ?? "Khong the tao slide deck");
                return;
            }

            if (!await CanContinueJobAsync(jobId, context))
            {
                return;
            }

            var brief = BuildBrief(target);
            var speedMode = NormalizeSpeedMode(target.SpeedMode);
            var isFastMode = speedMode == "fast";
            var imageSourcingOptions = ResolveImageSourcingOptions(speedMode);
            var outlineProgress = new Progress<SlideGenerationProgressUpdate>(update =>
            {
                UpdateJob(jobId, state =>
                {
                    ApplyGeneratorProgress(state, update, 2, 6);
                });
            });

            var outlineStopwatch = Stopwatch.StartNew();
            var outline = await slideGenerator.GenerateOutlineAsync(
                context.ExtractedText,
                context.ProcessedContent,
                brief,
                target.DesiredSlideCount,
                outlineProgress,
                context.DocumentId,
                jobId,
                speedMode);
            outlineStopwatch.Stop();
            _logger.LogInformation(
                "[SlideGen:{JobId}] Phase=outline Step=completed FolderId={FolderId} DocumentId={DocumentId} SlideCount={SlideCount} DurationMs={DurationMs}",
                jobId,
                context.FolderProjectId,
                context.DocumentId,
                outline.Slides.Count,
                outlineStopwatch.ElapsedMilliseconds);
            if (!await CanContinueJobAsync(jobId, context))
            {
                return;
            }

            var deck = new SlideDeck
            {
                DocumentId = context.DocumentId,
                FolderProjectId = context.FolderProjectId,
                Status = SlideDeckStatus.GeneratingSlides,
                Title = outline.Title,
                Subtitle = outline.Subtitle,
                ThemeKey = outline.ThemeKey,
                OutlineJson = JsonSerializer.Serialize(outline)
            };

            var placeholderItems = outline.Slides
                .OrderBy(slide => slide.SlideIndex)
                .Select(CreatePlaceholderItem)
                .ToList();

            if (isFastMode)
            {
                UpdateJob(jobId, state =>
                {
                    state.Percent = Math.Max(state.Percent, 22);
                    state.Stage = "saving-deck";
                    state.StageLabel = "Saving deck";
                    state.Message = "Saving deck";
                    state.Detail = $"Saving {placeholderItems.Count} slide placeholders";
                    state.StageIndex = 3;
                    state.StageCount = 6;
                    UpdateEta(state);
                });
            }

            if (!await CanContinueJobAsync(jobId, context))
            {
                return;
            }

            var saveOutlineStopwatch = Stopwatch.StartNew();
            persistedDeck = context.FolderProjectId.HasValue
                ? await slideDeckRepository.ReplaceForFolderAsync(deck, placeholderItems)
                : await slideDeckRepository.ReplaceForDocumentAsync(deck, placeholderItems);
            saveOutlineStopwatch.Stop();
            _logger.LogInformation(
                "[SlideGen:{JobId}] Phase=db Step=placeholder-deck-saved FolderId={FolderId} DocumentId={DocumentId} DeckId={DeckId} SlideCount={SlideCount} DurationMs={DurationMs}",
                jobId,
                context.FolderProjectId,
                context.DocumentId,
                persistedDeck.Id,
                persistedDeck.Items.Count,
                saveOutlineStopwatch.ElapsedMilliseconds);

            UpdateJob(jobId, state =>
            {
                state.SlideDeckId = persistedDeck.Id;
                state.Percent = 24;
                state.Stage = "outline-ready";
                state.StageLabel = "Outline san sang";
                state.Message = "Da co outline, bat dau sinh tung slide";
                state.Detail = $"Deck co {persistedDeck.Items.Count} slide placeholder";
                state.StageIndex = 3;
                state.StageCount = 6;
                UpdateEta(state);
            });

            var slideItems = persistedDeck.Items.OrderBy(item => item.SlideIndex).ToList();
            for (var index = 0; index < slideItems.Count; index++)
            {
                if (!await CanContinueJobAsync(jobId, context))
                {
                    return;
                }

                var item = slideItems[index];
                var outlineSlide = outline.Slides.First(slide => slide.SlideIndex == item.SlideIndex);
                var slideStopwatch = Stopwatch.StartNew();

                item.Status = SlideItemStatus.Generating;
                if (!await CanContinueJobAsync(jobId, context))
                {
                    return;
                }
                await slideDeckRepository.UpdateItemAsync(item);

                UpdateJob(jobId, state =>
                {
                    state.Percent = MapProgress(26, 88, index, slideItems.Count);
                    state.Stage = "generating-slides";
                    state.StageLabel = "Dang sinh slide";
                    state.Message = isFastMode ? "Generating slides" : $"Dang tao slide {index + 1}/{slideItems.Count}";
                    state.Detail = outlineSlide.Heading;
                    state.Current = index + 1;
                    state.Total = slideItems.Count;
                    state.UnitLabel = "slide";
                    state.StageIndex = 4;
                    state.StageCount = 6;
                    UpdateEta(state);
                });

                var slideProgress = new Progress<SlideGenerationProgressUpdate>(update =>
                {
                    UpdateJob(jobId, state =>
                    {
                        ApplyGeneratorProgress(state, update, 4, 6, index + 1, slideItems.Count);

                        state.Percent = Math.Max(
                            state.Percent,
                            MapSlideProgress(
                                completedSlides: index,
                                totalSlides: slideItems.Count,
                                currentSlidePercent: update.Percent,
                                startPercent: 30,
                                endPercent: 90));
                        state.Stage = "generating-slides";
                        state.StageLabel = "Dang sinh slide";
                        state.Message = isFastMode ? "Generating slides" : $"Dang tao slide {index + 1}/{slideItems.Count}";
                        state.Detail = string.IsNullOrWhiteSpace(update.Detail)
                            ? (string.IsNullOrWhiteSpace(update.Message) ? outlineSlide.Heading : update.Message)
                            : update.Detail;
                        state.Current = index + 1;
                        state.Total = slideItems.Count;
                        state.UnitLabel = "slide";
                        UpdateEta(state);
                    });
                });

                var content = await slideGenerator.GenerateSlideAsync(
                    context.ExtractedText,
                    context.ProcessedContent,
                    brief,
                    outlineSlide,
                    index + 1,
                    slideItems.Count,
                    slideProgress,
                    context.DocumentId,
                    jobId,
                    speedMode);
                slideStopwatch.Stop();
                if (!await CanContinueJobAsync(jobId, context))
                {
                    return;
                }

                _logger.LogInformation(
                    "[SlideGen:{JobId}] Phase=slide-generation Step=content-completed FolderId={FolderId} DocumentId={DocumentId} DeckId={DeckId} Slide={Index}/{Total} Heading={Heading} DurationMs={DurationMs}",
                    jobId,
                    context.FolderProjectId,
                    context.DocumentId,
                    persistedDeck.Id,
                    index + 1,
                    slideItems.Count,
                    outlineSlide.Heading,
                    slideStopwatch.ElapsedMilliseconds);
                EnforceEvidenceScope(content, context.AllowedChunkIds);

                item.Heading = content.Heading ?? item.Heading;
                item.Subheading = content.Subheading;
                item.Goal = content.Goal;
                item.KeyMessage = content.KeyMessage;
                item.EvidenceFromText = content.EvidenceFromText;
                item.SpeakerNotes = content.SpeakerNotes;
                item.AccentTone = content.AccentTone;
                item.VerifierScore = content.VerifierScore;
                item.SetVerifierIssues(content.VerifierIssues);
                item.SetEvidenceDebug(content.EvidenceDebug);
                item.Rhythm = content.EvidenceDebug?.Rhythm ?? content.Rhythm;
                item.VisualRole = content.EvidenceDebug?.VisualRole ?? content.VisualRole;
                item.ChartIntent = content.EvidenceDebug?.ChartIntent ?? content.ChartIntent;
                item.NeedsChartReview = content.EvidenceDebug?.NeedsChartReview ?? content.NeedsChartReview;
                item.SetBodyBlocks(content.BodyBlocks);
                item.SetEditorState(item.BuildDefaultEditorState());
                RefreshImageScaffold(item);
                item.Status = content.SuggestedStatus;
                if (item.Status == SlideItemStatus.Completed)
                {
                    if (!await CanContinueJobAsync(jobId, context))
                    {
                        return;
                    }

                    UpdateJob(jobId, state =>
                    {
                        state.Percent = Math.Max(
                            state.Percent,
                            MapSlideProgress(index + 1, slideItems.Count, 0, 30, 90));
                        state.Stage = "image-sourcing";
                        state.StageLabel = "Dang xu ly media";
                        state.Message = isFastMode
                            ? "Generating slides"
                            : $"Dang tim/chon media cho slide {index + 1}/{slideItems.Count}";
                        state.Detail = item.Heading;
                        state.Current = index + 1;
                        state.Total = slideItems.Count;
                        state.UnitLabel = "slide";
                        state.StageIndex = 5;
                        state.StageCount = 6;
                        UpdateEta(state);
                    });
                    var imageStopwatch = Stopwatch.StartNew();
                    await slideImageService.SourceImagesForItemAsync(item, imageSourcingOptions);
                    imageStopwatch.Stop();
                    if (!await CanContinueJobAsync(jobId, context))
                    {
                        return;
                    }
                    _logger.LogInformation(
                        "[SlideGen:{JobId}] Phase=image-sourcing Step=completed FolderId={FolderId} DocumentId={DocumentId} DeckId={DeckId} Slide={Index}/{Total} SpeedMode={SpeedMode} DurationMs={DurationMs}",
                        jobId,
                        context.FolderProjectId,
                        context.DocumentId,
                        persistedDeck.Id,
                        index + 1,
                        slideItems.Count,
                        speedMode,
                        imageStopwatch.ElapsedMilliseconds);
                }
                if (!await CanContinueJobAsync(jobId, context))
                {
                    return;
                }

                var saveSlideStopwatch = Stopwatch.StartNew();
                await slideDeckRepository.UpdateItemAsync(item);
                saveSlideStopwatch.Stop();
                _logger.LogInformation(
                    "[SlideGen:{JobId}] Phase=db Step=slide-saved FolderId={FolderId} DocumentId={DocumentId} DeckId={DeckId} Slide={Index}/{Total} Status={Status} DurationMs={DurationMs}",
                    jobId,
                    context.FolderProjectId,
                    context.DocumentId,
                    persistedDeck.Id,
                    index + 1,
                    slideItems.Count,
                    item.Status,
                    saveSlideStopwatch.ElapsedMilliseconds);

                UpdateJob(jobId, state =>
                {
                    state.SlidesGenerated = index + 1;
                    state.Percent = MapProgress(30, 90, index + 1, slideItems.Count);
                    state.Stage = "generating-slides";
                    state.StageLabel = "Dang sinh slide";
                    state.Message = isFastMode ? "Generating slides" : $"Da xong slide {index + 1}/{slideItems.Count}";
                    state.Detail = item.Heading;
                    state.Current = index + 1;
                    state.Total = slideItems.Count;
                    state.UnitLabel = "slide";
                    state.StageIndex = 4;
                    state.StageCount = 6;
                    UpdateEta(state);
                });
            }

            if (!await CanContinueJobAsync(jobId, context))
            {
                return;
            }

            if (isFastMode)
            {
                UpdateJob(jobId, state =>
                {
                    state.Percent = Math.Max(state.Percent, 94);
                    state.Stage = "saving-deck";
                    state.StageLabel = "Saving deck";
                    state.Message = "Saving deck";
                    state.Detail = persistedDeck.Title;
                    state.StageIndex = 6;
                    state.StageCount = 6;
                    UpdateEta(state);
                });
            }

            if (!await CanContinueJobAsync(jobId, context))
            {
                return;
            }

            if (!_jobStore.TrySealCompletion(jobId))
            {
                _logger.LogInformation(
                    "[SlideGen:{JobId}] Phase=job Step=completion-seal-rejected FolderId={FolderId} DocumentId={DocumentId}",
                    jobId,
                    context.FolderProjectId,
                    context.DocumentId);
                return;
            }

            persistedDeck.Status = SlideDeckStatus.Completed;
            persistedDeck.CompletedAt = DateTime.UtcNow;
            persistedDeck.UpdatedAt = DateTime.UtcNow;
            var saveCompletedStopwatch = Stopwatch.StartNew();
            await slideDeckRepository.UpdateDeckAsync(persistedDeck);
            saveCompletedStopwatch.Stop();
            if (!await CanContinueJobAsync(jobId, context))
            {
                persistedDeck.Status = SlideDeckStatus.GeneratingSlides;
                persistedDeck.CompletedAt = null;
                persistedDeck.UpdatedAt = DateTime.UtcNow;
                await slideDeckRepository.UpdateDeckAsync(persistedDeck);
                return;
            }
            _logger.LogInformation(
                "[SlideGen:{JobId}] Phase=db Step=deck-completed-saved FolderId={FolderId} DocumentId={DocumentId} DeckId={DeckId} DurationMs={DurationMs}",
                jobId,
                context.FolderProjectId,
                context.DocumentId,
                persistedDeck.Id,
                saveCompletedStopwatch.ElapsedMilliseconds);

            var finalItems = persistedDeck.Items.ToList();
            var plannedImageCount = finalItems.Count(item => item.GetImagePlan()?.NeedsImage == true);
            var invalidPlanCount = finalItems.Count(item =>
                string.Equals(item.GetImagePlan()?.StatusHint, "image-plan-invalid", StringComparison.OrdinalIgnoreCase));
            var skippedTextOnlyCount = finalItems.Count - plannedImageCount;
            _logger.LogInformation(
                "Slide image planning completed for deck {DeckId}: plannedImages={PlannedImageCount}, skippedTextOnly={SkippedTextOnlyCount}, invalidPlans={InvalidPlanCount}",
                persistedDeck.Id,
                plannedImageCount,
                skippedTextOnlyCount,
                invalidPlanCount);

            UpdateJob(jobId, state =>
            {
                state.Status = "completed";
                state.Percent = 100;
                state.Stage = "completed";
                state.StageLabel = "Hoan tat";
                state.Message = "Da tao xong bo slide";
                state.Detail = $"Deck {persistedDeck.Title} san sang de preview va export PDF";
                state.SlidesGenerated = slideItems.Count;
                state.Current = slideItems.Count;
                state.Total = slideItems.Count;
                state.UnitLabel = "slide";
                state.StageIndex = 6;
                state.StageCount = 6;
                state.EstimatedRemainingSeconds = 0;
                UpdateEta(state);
            });
            totalStopwatch.Stop();
            _logger.LogInformation(
                "[SlideGen:{JobId}] Phase=job Step=completed FolderId={FolderId} DocumentId={DocumentId} DeckId={DeckId} SlideCount={SlideCount} DurationMs={DurationMs}",
                jobId,
                context.FolderProjectId,
                context.DocumentId,
                persistedDeck.Id,
                slideItems.Count,
                totalStopwatch.ElapsedMilliseconds);
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            totalStopwatch.Stop();
            _logger.LogError(ex, "Slide schema is unavailable for job {JobId}", jobId);
            if (persistedDeck != null)
            {
                try
                {
                    persistedDeck.Status = SlideDeckStatus.Failed;
                    persistedDeck.UpdatedAt = DateTime.UtcNow;
                    using var failureScope = _scopeFactory.CreateScope();
                    var slideDeckRepository = failureScope.ServiceProvider.GetRequiredService<ISlideDeckRepository>();
                    await slideDeckRepository.UpdateDeckAsync(persistedDeck);
                }
                catch (Exception updateEx)
                {
                    _logger.LogWarning(updateEx, "Could not mark slide deck {DeckId} as failed", persistedDeck.Id);
                }
            }

            FailJob(
                jobId,
                ex.Message,
                "Slide schema chưa sẵn sàng. Hãy chạy migration/backend update trước khi tạo deck.");
            _logger.LogInformation(
                "[SlideGen:{JobId}] Phase=job Step=failed ErrorType=schema-unavailable DurationMs={DurationMs}",
                jobId,
                totalStopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            _logger.LogError(ex, "Error generating slides for job {JobId}", jobId);
            if (persistedDeck != null)
            {
                try
                {
                    persistedDeck.Status = SlideDeckStatus.Failed;
                    persistedDeck.UpdatedAt = DateTime.UtcNow;
                    using var failureScope = _scopeFactory.CreateScope();
                    var slideDeckRepository = failureScope.ServiceProvider.GetRequiredService<ISlideDeckRepository>();
                    await slideDeckRepository.UpdateDeckAsync(persistedDeck);
                }
                catch (Exception updateEx)
                {
                    _logger.LogWarning(updateEx, "Could not mark slide deck {DeckId} as failed", persistedDeck.Id);
                }
            }

            FailJob(jobId, ex.Message, "Sinh slide that bai");
            _logger.LogInformation(
                "[SlideGen:{JobId}] Phase=job Step=failed ErrorType=unexpected DurationMs={DurationMs}",
                jobId,
                totalStopwatch.ElapsedMilliseconds);
        }
    }

    private object BuildDeckPayload(SlideDeck deck, SlideGenerationJobState? jobState, bool isStale = false)
    {
        return new
        {
            id = deck.Id,
            documentId = deck.DocumentId,
            folderProjectId = deck.FolderProjectId,
            ownerType = deck.FolderProjectId.HasValue ? "folder" : "document",
            status = deck.Status.ToString(),
            title = deck.Title,
            subtitle = deck.Subtitle,
            themeKey = deck.ThemeKey,
            outline = DeserializeOutline(deck.OutlineJson),
            createdAt = deck.CreatedAt,
            updatedAt = deck.UpdatedAt,
            completedAt = deck.CompletedAt,
            isStale,
            items = deck.Items
                .OrderBy(item => item.SlideIndex)
                .Select(BuildSlideItemPayload)
                .ToList(),
            qualitySummary = new
            {
                averageScore = deck.Items.Any(item => item.VerifierScore.HasValue)
                    ? (int)Math.Round(deck.Items.Where(item => item.VerifierScore.HasValue).Average(item => item.VerifierScore ?? 0))
                    : (int?)null,
                lowConfidenceCount = deck.Items.Count(item => IsLowConfidenceScore(item.VerifierScore)),
                unknownCount = deck.Items.Count(item => !item.VerifierScore.HasValue)
            },
            generationProgress = JobProgressPayloadFactory.BuildSlide(jobState, deck),
            imageSourcingProgress = BuildImageSourcingProgress(deck)
        };
    }

    private static object BuildImageSourcingProgress(SlideDeck deck)
    {
        var totalSlides = deck.Items.Count;
        var totalNeedsImage = 0;
        var readyCount = 0;
        var queuedCount = 0;
        var candidateOnlyCount = 0;
        var noLicenseSafeCount = 0;
        var failedCount = 0;
        var noImageNeededCount = 0;
        var generatedOnlyCount = 0;
        var invalidPlanCount = 0;

        foreach (var item in deck.Items)
        {
            var imagePlan = item.GetImagePlan() ?? BuildDefaultImagePlan(item);
            var imageCandidates = NormalizeImageCandidates(item.GetImageCandidates(), item.SelectedImageKey);
            var selectedImage = ResolveSelectedImage(imageCandidates, item.SelectedImageKey);
            var imageState = BuildImageState(item, imagePlan, imageCandidates, selectedImage);

            if (!imageState.NeedsImage)
            {
                if (string.Equals(imageState.Status, "image-plan-invalid", StringComparison.OrdinalIgnoreCase))
                {
                    invalidPlanCount += 1;
                }

                noImageNeededCount += 1;
                continue;
            }

            totalNeedsImage += 1;

            if (string.Equals(imageState.Status, "ready", StringComparison.OrdinalIgnoreCase))
            {
                if (selectedImage != null)
                {
                    readyCount += 1;

                    var selectedIsGenerated = string.Equals(selectedImage.SourceType, "generated", StringComparison.OrdinalIgnoreCase);
                    var hasWebCandidate = imageCandidates.Any(candidate =>
                        string.Equals(candidate.SourceType, "web", StringComparison.OrdinalIgnoreCase));
                    if (selectedIsGenerated && !hasWebCandidate)
                    {
                        generatedOnlyCount += 1;
                    }
                }
                else
                {
                    candidateOnlyCount += 1;
                }

                continue;
            }

            if (string.Equals(imageState.Status, "no-license-safe-image", StringComparison.OrdinalIgnoreCase))
            {
                noLicenseSafeCount += 1;
                continue;
            }

            if (string.Equals(imageState.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                failedCount += 1;
                continue;
            }

            queuedCount += 1;
        }

        var hasWork = totalNeedsImage > 0;
        var percent = hasWork
            ? (int)Math.Round((double)readyCount * 100 / totalNeedsImage)
            : 100;

        var status = "running";
        var stage = "image-sourcing";
        var stageLabel = "Dang xu ly media";
        var message = "Dang tim va chon media cho cac slide can hinh.";

        if (!hasWork)
        {
            status = "completed";
            stage = "no-image-needed";
            stageLabel = "Text-only";
            message = "Khong co slide nao can media trong deck nay.";
        }
        else if (readyCount == totalNeedsImage)
        {
            status = "completed";
            stage = "completed";
            stageLabel = "Media san sang";
            message = "Tat ca slide can hinh da co selected media.";
        }
        else if (failedCount + noLicenseSafeCount == totalNeedsImage)
        {
            status = "failed";
            stage = "failed";
            stageLabel = "Media gap loi";
            message = "Image workflow that bai hoac chua tim thay nguon an toan cho tat ca slide can hinh.";
        }
        else if (candidateOnlyCount > 0)
        {
            stage = "awaiting-selection";
            stageLabel = "Can chon candidate";
            message = "Da co candidate, can chon selected media cho mot so slide.";
        }

        return new
        {
            status,
            percent,
            stage,
            stageLabel,
            message,
            detail = $"ready={readyCount}, candidateOnly={candidateOnlyCount}, queued={queuedCount}, noLicense={noLicenseSafeCount}, failed={failedCount}",
            current = readyCount,
            total = totalNeedsImage,
            unitLabel = "slide",
            totalSlides,
            noImageNeededCount,
            readyCount,
            candidateOnlyCount,
            queuedCount,
            noLicenseSafeCount,
            failedCount,
            generatedOnlyCount,
            invalidPlanCount
        };
    }

    private static object BuildSlideItemPayload(SlideItem item)
    {
        var imagePlan = item.GetImagePlan() ?? BuildDefaultImagePlan(item);
        var imageCandidates = NormalizeImageCandidates(item.GetImageCandidates(), item.SelectedImageKey);
        var selectedImage = ResolveSelectedImage(imageCandidates, item.SelectedImageKey);
        var imageState = BuildImageState(item, imagePlan, imageCandidates, selectedImage);
        var evidenceDebug = item.GetEvidenceDebug();

        return new
        {
            item.Id,
            item.SlideDeckId,
            item.SlideIndex,
            slideType = item.SlideType.ToString(),
            status = item.Status.ToString(),
            item.Heading,
            item.Subheading,
            item.Goal,
            item.KeyMessage,
            bodyBlocks = item.GetBodyBlocks(),
            item.EvidenceFromText,
            item.SpeakerNotes,
            item.AccentTone,
            editorState = item.GetEditorState(),
            imageState,
            selectedImage,
            imageCandidates,
            quality = BuildQualityPayload(item.VerifierScore, item.GetVerifierIssues()),
            rhythm = evidenceDebug?.Rhythm ?? item.Rhythm,
            visualRole = evidenceDebug?.VisualRole ?? item.VisualRole,
            chartIntent = evidenceDebug?.ChartIntent ?? item.ChartIntent,
            needsChartReview = evidenceDebug?.NeedsChartReview ?? item.NeedsChartReview,
            evidenceDebug,
            item.CreatedAt,
            item.UpdatedAt
        };
    }

    private static object BuildQualityPayload(int? score, IReadOnlyCollection<string> issues)
    {
        return new
        {
            score,
            issues,
            isLowConfidence = IsLowConfidenceScore(score),
            isUnknown = !score.HasValue
        };
    }

    private static bool IsLowConfidenceScore(int? score)
        => score.HasValue && score.Value < SlideLowConfidenceThreshold;

    private static SlideOutlineResult? DeserializeOutline(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SlideOutlineResult>(json);
        }
        catch
        {
            return null;
        }
    }

    private static SlideItem CreatePlaceholderItem(SlideOutlineSlide slide)
    {
        var item = new SlideItem
        {
            SlideIndex = slide.SlideIndex,
            SlideType = slide.SlideType,
            Status = SlideItemStatus.Pending,
            Heading = slide.Heading,
            Subheading = slide.Subheading,
            Goal = slide.Goal,
            KeyMessage = slide.KeyMessage
        };
        item.SetBodyBlocks(new List<string>());
        item.SetEditorState(item.BuildDefaultEditorState());
        RefreshImageScaffold(item, preserveExistingCandidates: false);
        return item;
    }

    private static ProcessedContent BuildProcessedContentFromDocument(Document document)
    {
        var metadata = document.GetProcessingMetadata();
        return new ProcessedContent
        {
            MainTopics = document.GetMainTopics(),
            KeyPoints = document.GetKeyPoints(),
            Summary = document.Summary,
            Language = document.Language,
            DocumentType = metadata.DocumentType,
            Title = metadata.Title,
            MainContentStartPage = metadata.MainContentStartPage,
            Structure = metadata.Structure,
            ExcludedContent = metadata.ExcludedContent,
            CoverageMap = document.GetCoverageMap()
        };
    }

    private static SlideDeckBrief BuildBrief(SlideGenerationTarget request)
    {
        return new SlideDeckBrief
        {
            ThemeKey = string.IsNullOrWhiteSpace(request.ThemeKey) ? "editorial-sunrise" : request.ThemeKey.Trim(),
            Audience = string.IsNullOrWhiteSpace(request.Audience) ? "Sinh vien va nguoi hoc" : request.Audience.Trim(),
            Tone = string.IsNullOrWhiteSpace(request.Tone) ? "Rõ ràng, hiện đại, dễ nhớ" : request.Tone.Trim(),
            NarrativeGoal = string.IsNullOrWhiteSpace(request.NarrativeGoal)
                ? "Giup nguoi doc nam duoc cau truc va cac y chinh cua tai lieu trong mot lan xem"
                : request.NarrativeGoal.Trim(),
            LanguageStyle = string.IsNullOrWhiteSpace(request.LanguageStyle)
                ? "Tieng Viet ngan gon, chuyen nghiep, de doc tren slide"
                : request.LanguageStyle.Trim(),
            Mode = NormalizeGenerationMode(request.Mode),
            ScopePolicy = string.IsNullOrWhiteSpace(request.ScopePolicy)
                ? "selected-sections-only"
                : request.ScopePolicy.Trim(),
            SelectedSectionIds = request.SelectedSectionIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>()
        };
    }

    private static ProcessedContent BuildProcessedContentFromSources(IReadOnlyCollection<Document> sources)
    {
        var orderedSources = sources
            .Where(source => !string.IsNullOrWhiteSpace(source.ExtractedText))
            .OrderBy(source => source.FolderSourceOrder)
            .ThenBy(source => source.CreatedAt)
            .ToList();
        var metadataBySource = orderedSources
            .ToDictionary(source => source.Id, source => source.GetProcessingMetadata());

        return new ProcessedContent
        {
            MainTopics = orderedSources
                .SelectMany(source => source.GetMainTopics())
                .Where(topic => !string.IsNullOrWhiteSpace(topic))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToList(),
            KeyPoints = orderedSources
                .SelectMany(source => source.GetKeyPoints())
                .Where(point => !string.IsNullOrWhiteSpace(point))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .ToList(),
            Summary = string.Join("\n\n", orderedSources
                .Select(source => string.IsNullOrWhiteSpace(source.Summary)
                    ? null
                    : $"{source.FileName}: {source.Summary.Trim()}")
                .Where(value => !string.IsNullOrWhiteSpace(value))),
            Language = orderedSources
                .Select(source => metadataBySource[source.Id].Language ?? source.Language)
                .FirstOrDefault(language => !string.IsNullOrWhiteSpace(language)),
            DocumentType = orderedSources
                .Select(source => metadataBySource[source.Id].DocumentType)
                .FirstOrDefault(type => !string.IsNullOrWhiteSpace(type)) ?? DocumentTypes.Unknown,
            Title = orderedSources
                .Select(source => metadataBySource[source.Id].Title)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            MainContentStartPage = orderedSources
                .Select(source => metadataBySource[source.Id].MainContentStartPage)
                .FirstOrDefault(page => page.HasValue),
            Structure = orderedSources
                .SelectMany(source => metadataBySource[source.Id].Structure)
                .ToList(),
            ExcludedContent = orderedSources
                .SelectMany(source => metadataBySource[source.Id].ExcludedContent)
                .ToList(),
            CoverageMap = orderedSources
                .SelectMany((source, sourceIndex) => source.GetCoverageMap().Select((chunk, chunkIndex) => new DocumentCoverageChunk
                {
                    ChunkNumber = sourceIndex * 1000 + chunkIndex + 1,
                    ChunkId = $"{source.Id}-{chunk.ChunkId}",
                    Zone = chunk.Zone,
                    Label = $"{source.FileName}: {chunk.Label}",
                    HeadingKind = chunk.HeadingKind,
                    HeadingLevel = chunk.HeadingLevel,
                    HeadingMarker = chunk.HeadingMarker,
                    HeadingText = chunk.HeadingText,
                    NormalizedHeading = chunk.NormalizedHeading,
                    HeadingPath = chunk.HeadingPath,
                    ParentHeadingPath = chunk.ParentHeadingPath,
                    SectionKey = string.IsNullOrWhiteSpace(chunk.SectionKey) ? $"{source.Id}-{chunk.ChunkId}" : $"{source.Id}-{chunk.SectionKey}",
                    IsPrimarySection = chunk.IsPrimarySection,
                    Classification = chunk.Classification,
                    TeachabilityScore = chunk.TeachabilityScore,
                    PositiveSignals = chunk.PositiveSignals,
                    NegativeSignals = chunk.NegativeSignals,
                    SelectionReason = chunk.SelectionReason,
                    StartPage = chunk.StartPage,
                    EndPage = chunk.EndPage,
                    Summary = chunk.Summary,
                    EvidenceExcerpt = chunk.EvidenceExcerpt,
                    KeyFacts = chunk.KeyFacts
                }))
                .ToList()
        };
    }

    private static string BuildCombinedExtractedText(IReadOnlyCollection<Document> sources)
    {
        return string.Join(
            "\n\n==============================\n\n",
            sources
                .Where(source => !string.IsNullOrWhiteSpace(source.ExtractedText))
                .OrderBy(source => source.FolderSourceOrder)
                .ThenBy(source => source.CreatedAt)
                .Select(source => $"Nguon: {source.FileName}\n\n{source.ExtractedText?.Trim()}"));
    }

    private static string BuildCombinedExtractedTextFromChunks(Document source, IReadOnlyCollection<DocumentCoverageChunk> chunks)
    {
        var ordered = chunks.OrderBy(chunk => chunk.ChunkNumber).ToList();
        var body = string.Join(
            "\n\n",
            ordered.Select(chunk =>
                string.Join(
                    "\n",
                    new[]
                    {
                        $"[{chunk.ChunkId}] {chunk.Label}",
                        chunk.Summary,
                        chunk.EvidenceExcerpt,
                        string.Join(" ", chunk.KeyFacts.Where(value => !string.IsNullOrWhiteSpace(value)))
                    }.Where(value => !string.IsNullOrWhiteSpace(value)))));

        return $"Nguon: {source.FileName}\n\n{body}";
    }

    private async Task<SlideGenerationContext> ResolveGenerationContextAsync(
        SlideGenerationTarget target,
        IDocumentRepository documentRepository,
        IFolderProjectRepository folderProjectRepository,
        IDocumentGenerationReadinessService generationReadinessService)
    {
        if (target.DocumentId.HasValue)
        {
            var document = await documentRepository.GetByIdAsync(target.DocumentId.Value);
            if (document == null)
            {
                return SlideGenerationContext.Fail("Document not found", "Khong tim thay tai lieu");
            }

            if (string.IsNullOrWhiteSpace(document.ExtractedText))
            {
                return SlideGenerationContext.Fail("Document has not been processed yet", "Tai lieu chua co noi dung ExtractedText");
            }

            var generationReadiness = await generationReadinessService.GetReadinessAsync(document, target.ConfirmLowConfidence);
            if (generationReadiness.Blocked)
            {
                return SlideGenerationContext.Fail(
                    "Document generation readiness gate blocked auto-generation",
                    BuildReadinessDetail(generationReadiness));
            }

            var processedContent = BuildProcessedContentFromDocument(document);
            if (target.SelectedSectionIds?.Count > 0)
            {
                var filtered = TryFilterProcessedContentForDocument(document, processedContent, target.SelectedSectionIds, out var filterError);
                if (filtered == null)
                {
                    return SlideGenerationContext.Fail("Selected sections are invalid", filterError ?? "Khong tim thay section duoc chon");
                }

                return SlideGenerationContext.FromDocument(
                    document.Id,
                    BuildCombinedExtractedTextFromChunks(document, filtered.CoverageMap),
                    filtered);
            }

            return SlideGenerationContext.FromDocument(document.Id, document.ExtractedText, processedContent);
        }

        if (target.FolderProjectId.HasValue)
        {
            var folder = await folderProjectRepository.GetByIdAsync(target.FolderProjectId.Value);
            if (folder == null)
            {
                return SlideGenerationContext.Fail("Folder project not found", "Khong tim thay folder project");
            }

            var selectedSources = folder.Documents
                .Where(source => source.IncludeInFolderSlides)
                .OrderBy(source => source.FolderSourceOrder)
                .ThenBy(source => source.CreatedAt)
                .ToList();

            if (selectedSources.Count == 0)
            {
                return SlideGenerationContext.Fail("No folder sources selected for slides", "Can chon it nhat 1 source de tao slide");
            }

            var readySources = selectedSources
                .Where(source => source.Status == DocumentStatus.Completed && !string.IsNullOrWhiteSpace(source.ExtractedText))
                .ToList();

            if (readySources.Count == 0)
            {
                return SlideGenerationContext.Fail("Selected sources are not ready", "Nguon duoc chon cho slide chua OCR/analyze xong");
            }

            if (target.SourceIds?.Count > 0 || target.SelectedSectionIds?.Count > 0)
            {
                var primarySourceId = target.SourceIds?.FirstOrDefault() ?? 0;
                var primarySource = readySources.FirstOrDefault(source => source.Id == primarySourceId);
                if (primarySource == null)
                {
                    return SlideGenerationContext.Fail("Primary source is missing", "Can chon 1 tai lieu chinh de tao deck theo chuong");
                }

                var generationReadiness = await generationReadinessService.GetReadinessAsync(primarySource, target.ConfirmLowConfidence);
                if (generationReadiness.Blocked)
                {
                    return SlideGenerationContext.Fail(
                        "Document generation readiness gate blocked auto-generation",
                        BuildReadinessDetail(generationReadiness));
                }

                var processedContent = BuildProcessedContentFromDocument(primarySource);
                var filtered = TryFilterProcessedContentForDocument(primarySource, processedContent, target.SelectedSectionIds ?? new List<string>(), out var filterError);
                if (filtered == null)
                {
                    return SlideGenerationContext.Fail("Selected sections are invalid", filterError ?? "Khong tim thay section duoc chon");
                }

                return SlideGenerationContext.FromFolder(
                    folder.Id,
                    BuildCombinedExtractedTextFromChunks(primarySource, filtered.CoverageMap),
                    filtered);
            }

            var aggregateReadiness = generationReadinessService.GetAggregateReadiness(
                await Task.WhenAll(readySources.Select(source => generationReadinessService.GetReadinessAsync(source, target.ConfirmLowConfidence))),
                target.ConfirmLowConfidence);
            if (aggregateReadiness.Blocked)
            {
                return SlideGenerationContext.Fail(
                    "Document generation readiness gate blocked auto-generation",
                    BuildReadinessDetail(aggregateReadiness));
            }

            return SlideGenerationContext.FromFolder(
                folder.Id,
                BuildCombinedExtractedText(readySources),
                BuildProcessedContentFromSources(readySources));
        }

        return SlideGenerationContext.Fail("Missing slide generation owner", "Khong xac dinh duoc owner cua slide deck");
    }

    private static ProcessedContent? TryFilterProcessedContentForDocument(
        Document source,
        ProcessedContent processedContent,
        IReadOnlyCollection<string> selectedSectionIds,
        out string? error)
    {
        error = null;
        if (selectedSectionIds.Count == 0)
        {
            error = "Can chon it nhat 1 chapter/section truoc khi tao slide.";
            return null;
        }

        var normalizedSelections = selectedSectionIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ParseSelectedSectionId(value.Trim(), source.Id))
            .Where(parsed => parsed.SourceId == source.Id && !string.IsNullOrWhiteSpace(parsed.SectionKey))
            .Select(parsed => parsed.SectionKey!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (normalizedSelections.Count == 0)
        {
            error = "Section da chon khong thuoc tai lieu chinh hien tai.";
            return null;
        }

        var structure = processedContent.Structure ?? new List<DocumentSectionDescriptor>();
        var selectedSections = structure
            .Where(section => normalizedSelections.Contains(section.SectionKey))
            .ToList();

        if (selectedSections.Count == 0)
        {
            error = "Khong tim thay section duoc chon trong metadata cau truc.";
            return null;
        }

        var allowedChunkIds = selectedSections
            .SelectMany(section => section.ChunkIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var explicitlyAllowedClassifications = selectedSections
            .Select(section => section.Classification)
            .Where(value => !string.IsNullOrWhiteSpace(value) && ExplicitlyExcludedScopeClassifications.Contains(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filteredCoverage = processedContent.CoverageMap
            .Where(chunk => allowedChunkIds.Contains(chunk.ChunkId))
            .Where(chunk =>
                !ExplicitlyExcludedScopeClassifications.Contains(chunk.Classification)
                || explicitlyAllowedClassifications.Contains(chunk.Classification))
            .OrderBy(chunk => chunk.ChunkNumber)
            .ToList();

        if (filteredCoverage.Count == 0)
        {
            error = "Section da chon khong co chunk day hoc hop le de tao slide.";
            return null;
        }

        var filteredStructure = selectedSections
            .Select(section => new DocumentSectionDescriptor
            {
                SectionKey = BuildScopedSectionId(source.Id, section.SectionKey),
                Heading = section.Heading,
                Classification = section.Classification,
                StartPage = section.StartPage,
                EndPage = section.EndPage,
                ChunkIds = section.ChunkIds
                    .Where(allowedChunkIds.Contains)
                    .ToList()
            })
            .ToList();

        var keyPoints = filteredCoverage
            .SelectMany(chunk => chunk.KeyFacts)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();

        return new ProcessedContent
        {
            MainTopics = processedContent.MainTopics
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(12)
                .ToList(),
            KeyPoints = keyPoints.Any()
                ? keyPoints
                : processedContent.KeyPoints
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Take(18)
                    .ToList(),
            Summary = string.Join(" ", filteredStructure
                .Select(section => section.Heading)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)),
            Language = processedContent.Language,
            DocumentType = processedContent.DocumentType,
            Title = processedContent.Title,
            MainContentStartPage = filteredStructure
                .Select(section => section.StartPage)
                .FirstOrDefault(page => page.HasValue),
            Structure = filteredStructure,
            ExcludedContent = processedContent.ExcludedContent
                .Where(item => item.ChunkId == null || !allowedChunkIds.Contains(item.ChunkId))
                .ToList(),
            CoverageMap = filteredCoverage
        };
    }

    private static string BuildScopedSectionId(int sourceId, string sectionKey)
        => $"{sourceId}::{sectionKey}";

    private static (int? SourceId, string? SectionKey) ParseSelectedSectionId(string rawValue, int fallbackSourceId)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return (fallbackSourceId, null);
        }

        var parts = rawValue.Split(new[] { "::" }, StringSplitOptions.None);
        if (parts.Length == 2 && int.TryParse(parts[0], out var sourceId))
        {
            return (sourceId, parts[1]);
        }

        return (fallbackSourceId, rawValue);
    }

    private static string NormalizeGenerationMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "lecture";
        }

        return mode.Trim().ToLowerInvariant() switch
        {
            "summary" => "summary",
            "exam-review" => "exam-review",
            "timeline" => "timeline",
            _ => "lecture"
        };
    }

    private static object? BuildScopeRecommendation(GenerateFolderSlidesRequest request)
    {
        if (request.SelectedSectionIds == null || request.SelectedSectionIds.Count == 0)
        {
            return null;
        }

        if (request.DesiredSlideCount >= 18)
        {
            return null;
        }

        return new
        {
            suggestedSlideCount = request.DesiredSlideCount < 12 ? 12 : 18,
            reason = "Selected sections may need a larger deck to preserve chapter structure and review flow."
        };
    }

    private static bool IsFolderDeckStale(SlideDeck deck, IEnumerable<Document> sources)
    {
        if (!deck.FolderProjectId.HasValue)
        {
            return false;
        }

        var comparisonPoint = deck.CompletedAt ?? deck.UpdatedAt;
        return sources.Any(source => source.CreatedAt > comparisonPoint || source.UpdatedAt > comparisonPoint);
    }

    private static void EnforceEvidenceScope(SlideContentResult content, IReadOnlyCollection<string> allowedChunkIds)
    {
        if (content.EvidenceDebug?.SelectedChunks == null || content.EvidenceDebug.SelectedChunks.Count == 0)
        {
            return;
        }

        var allowed = allowedChunkIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outOfScope = content.EvidenceDebug.SelectedChunks
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk.ChunkId) && !allowed.Contains(chunk.ChunkId))
            .Select(chunk => chunk.ChunkId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (outOfScope.Count == 0)
        {
            return;
        }

        content.SuggestedStatus = SlideItemStatus.NeedsReview;
        content.VerifierScore = Math.Min(content.VerifierScore ?? 75, 75);
        content.VerifierIssues = content.VerifierIssues
            .Concat(new[]
            {
                $"Evidence vuot pham vi section da chon: {string.Join(", ", outOfScope)}."
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void FailJob(string jobId, string error, string message)
    {
        UpdateJob(jobId, state =>
        {
            state.Status = "failed";
            state.Percent = 100;
            state.Stage = "failed";
            state.StageLabel = "That bai";
            state.Message = message;
            state.Detail = error;
            state.Error = error;
            state.StageIndex = 6;
            state.StageCount = 6;
            state.EstimatedRemainingSeconds = 0;
            UpdateEta(state);
        });
    }

    private bool IsJobStillCurrent(string jobId, SlideGenerationContext context)
    {
        if (_jobStore.IsLatestJob(jobId, context.DocumentId, context.FolderProjectId))
        {
            return true;
        }

        _logger.LogInformation(
            "[SlideGen:{JobId}] Phase=job Step=superseded-stop FolderId={FolderId} DocumentId={DocumentId}",
            jobId,
            context.FolderProjectId,
            context.DocumentId);
        return false;
    }

    private async Task<bool> CanContinueJobAsync(string jobId, SlideGenerationContext context)
    {
        if (!await _jobStore.WaitForExecutionAsync(jobId))
        {
            _logger.LogInformation(
                "[SlideGen:{JobId}] Phase=job Step=cancelled-stop FolderId={FolderId} DocumentId={DocumentId}",
                jobId,
                context.FolderProjectId,
                context.DocumentId);
            return false;
        }

        return IsJobStillCurrent(jobId, context);
    }

    private IActionResult ChangeSlideJobState(
        string jobId,
        Func<string, bool> transition,
        string conflictCode,
        string conflictMessage)
    {
        if (!_jobStore.TryGetJob(jobId, out var state) || state == null)
        {
            return ApiNotFound("job_not_found", "Job not found.");
        }

        var authResult = EnsureCurrentUserMatches(state.CreatedByUserId);
        if (authResult != null)
        {
            return authResult;
        }

        if (!transition(jobId) || !_jobStore.TryGetJob(jobId, out state) || state == null)
        {
            return ApiConflict(conflictCode, conflictMessage);
        }

        return Ok(JobProgressPayloadFactory.BuildSlide(state));
    }

    private void UpdateJob(string jobId, Action<SlideGenerationJobState> updater)
        => _jobStore.UpdateJob(jobId, state =>
        {
            updater(state);
            _logger.LogDebug(
                "[SlideGen:{JobId}] Phase={Phase} Step=progress-emitted DocumentId={DocumentId} FolderId={FolderId} DeckId={DeckId} Percent={Percent} Status={Status} Current={Current} Total={Total} EtaSeconds={EtaSeconds}",
                jobId,
                state.Stage,
                state.DocumentId,
                state.FolderProjectId,
                state.SlideDeckId,
                state.Percent,
                state.Status,
                state.Current,
                state.Total,
                state.EstimatedRemainingSeconds);
        });

    private static void ApplyGeneratorProgress(
        SlideGenerationJobState state,
        SlideGenerationProgressUpdate update,
        int stageIndex,
        int stageCount,
        int? current = null,
        int? total = null)
    {
        state.Status = "running";
        state.Percent = Math.Max(state.Percent, Math.Clamp(update.Percent, 0, 100));
        state.Stage = string.IsNullOrWhiteSpace(update.Stage) ? state.Stage : update.Stage;
        state.StageLabel = string.IsNullOrWhiteSpace(update.StageLabel) ? state.StageLabel : update.StageLabel;
        state.Message = update.Message;
        state.Detail = update.Detail;
        state.Current = update.Current ?? current ?? state.Current;
        state.Total = update.Total ?? total ?? state.Total;
        state.UnitLabel = update.UnitLabel ?? state.UnitLabel;
        state.StageIndex = stageIndex;
        state.StageCount = stageCount;
        UpdateEta(state);
    }

    private static int MapProgress(int startPercent, int endPercent, int current, int total)
    {
        if (total <= 0)
        {
            return endPercent;
        }

        var ratio = Math.Clamp(current / (double)total, 0d, 1d);
        return startPercent + (int)Math.Round((endPercent - startPercent) * ratio);
    }

    private static int MapSlideProgress(
        int completedSlides,
        int totalSlides,
        int currentSlidePercent,
        int startPercent = 30,
        int endPercent = 90)
    {
        var safeStart = Math.Clamp(startPercent, 0, 100);
        var safeEnd = Math.Clamp(endPercent, safeStart, 100);

        if (totalSlides <= 0)
        {
            return safeStart;
        }

        var clampedCurrent = Math.Clamp(currentSlidePercent, 0, 100);
        var clampedCompleted = Math.Clamp(completedSlides, 0, totalSlides);
        var progressUnits = clampedCompleted + clampedCurrent / 100d;
        var ratio = Math.Clamp(progressUnits / totalSlides, 0d, 1d);

        return safeStart + (int)Math.Round((safeEnd - safeStart) * ratio);
    }

    private static void UpdateEta(SlideGenerationJobState state)
    {
        var elapsedSeconds = Math.Max(0, (int)Math.Round((DateTime.UtcNow - state.CreatedAt).TotalSeconds));
        state.ElapsedSeconds = elapsedSeconds;

        if (state.Status is "completed" or "failed")
        {
            state.EstimatedRemainingSeconds = 0;
            return;
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

    private IActionResult SlideSchemaUnavailable()
    {
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            message = "Slide feature schema is not initialized. Run database migrations to create slide_decks, slide_items, and slide image metadata columns."
        });
    }

    [HttpPost("folders/{folderId}/generate/start")]
    public async Task<IActionResult> StartGenerateSlidesForFolder(int folderId, [FromBody] GenerateFolderSlidesRequest request)
    {
        _logger.LogInformation(
            "[SlideGen] Phase=request Step=received FolderId={FolderId} DesiredSlideCount={DesiredSlideCount} SourceIds={SourceIds} SelectedSectionCount={SelectedSectionCount} Mode={Mode} SpeedMode={SpeedMode} ScopePolicy={ScopePolicy}",
            folderId,
            request.DesiredSlideCount,
            request.SourceIds == null ? string.Empty : string.Join(",", request.SourceIds),
            request.SelectedSectionIds?.Count ?? 0,
            request.Mode,
            NormalizeSpeedMode(request.SpeedMode),
            request.ScopePolicy);

        if (!IsValidSlideCount(request.DesiredSlideCount))
        {
            return BadRequest("DesiredSlideCount must be between 5 and 18");
        }

        var folder = await GetFolderAsync(folderId);
        if (folder == null)
        {
            return NotFound("Folder project not found");
        }

        var authResult = EnsureOwnerAccess(folder.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        if (request.SourceIds?.Count > 1)
        {
            return BadRequest("V1 supports one primary source document per deck run.");
        }

        if (!string.IsNullOrWhiteSpace(request.ScopePolicy) &&
            !string.Equals(request.ScopePolicy, "selected-sections-only", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only scopePolicy=selected-sections-only is supported.");
        }

        if (!IsSupportedGenerationMode(request.Mode))
        {
            return BadRequest("Unsupported slide generation mode.");
        }

        if (!IsSupportedSpeedMode(request.SpeedMode))
        {
            return BadRequest("Unsupported slide generation speed mode.");
        }

        try
        {
            _ = await _slideDeckRepository.GetLatestByFolderIdAsync(folderId);
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }

        var generationReadiness = await GetFolderGenerationReadinessAsync(folder, request);
        if (generationReadiness.Blocked)
        {
            return GenerationGateBlocked(generationReadiness);
        }

        var jobId = _jobStore.CreateFolderJob(folderId, request.DesiredSlideCount, CurrentUserIdAsString);
        _jobStore.TryGetJob(jobId, out var state);
        _logger.LogInformation(
            "[SlideGen:{JobId}] Phase=request Step=job-created FolderId={FolderId} SourceIds={SourceIds} SelectedSectionCount={SelectedSectionCount}",
            jobId,
            folderId,
            request.SourceIds == null ? string.Empty : string.Join(",", request.SourceIds),
            request.SelectedSectionIds?.Count ?? 0);
        _ = Task.Run(() => RunGenerateSlidesJobAsync(jobId, new SlideGenerationTarget
        {
            FolderProjectId = folderId,
            DesiredSlideCount = request.DesiredSlideCount,
            ThemeKey = request.ThemeKey,
            Audience = request.Audience,
            Tone = request.Tone,
            NarrativeGoal = request.NarrativeGoal,
            LanguageStyle = request.LanguageStyle,
            SourceIds = request.SourceIds,
            SelectedSectionIds = request.SelectedSectionIds,
            Mode = request.Mode,
            SpeedMode = request.SpeedMode,
            ScopePolicy = request.ScopePolicy,
            ConfirmLowConfidence = request.ConfirmLowConfidence
        }));
        _logger.LogInformation(
            "[SlideGen:{JobId}] Phase=job Step=request-completed-background-continues FolderId={FolderId}",
            jobId,
            folderId);

        var recommendation = BuildScopeRecommendation(request);

        return Accepted(new
        {
            jobId,
            status = "queued",
            progressUrl = $"/api/slides/generate/progress/{jobId}",
            resultUrl = $"/api/slides/folders/{folderId}",
            generationReadiness,
            progress = state == null ? null : JobProgressPayloadFactory.BuildSlide(state),
            scopeRecommendation = recommendation
        });
    }

    [HttpGet("folders/{folderId}/generate/active")]
    public async Task<IActionResult> GetActiveGenerateSlidesForFolder(int folderId)
    {
        _logger.LogInformation(
            "[SlideGen] Phase=job Step=resume-active-query FolderId={FolderId}",
            folderId);

        var folder = await GetFolderAsync(folderId);
        if (folder == null)
        {
            return NotFound("Folder project not found");
        }

        var authResult = EnsureOwnerAccess(folder.UploadedBy);
        if (authResult != null)
        {
            return authResult;
        }

        if (!_jobStore.TryGetLatestActiveJobForFolder(folderId, out var state) || state == null)
        {
            _logger.LogInformation(
                "[SlideGen] Phase=job Step=resume-active-empty FolderId={FolderId}",
                folderId);
            return NoContent();
        }

        var userAuthResult = EnsureCurrentUserMatches(state.CreatedByUserId);
        if (userAuthResult != null)
        {
            return userAuthResult;
        }

        _logger.LogInformation(
            "[SlideGen:{JobId}] Phase=job Step=resume-active-found FolderId={FolderId} Status={Status}",
            state.JobId,
            folderId,
            state.Status);

        return Ok(JobProgressPayloadFactory.BuildSlide(state));
    }

    private async Task<DocumentGenerationReadiness> GetFolderGenerationReadinessAsync(FolderProject folder, GenerateFolderSlidesRequest request)
    {
        var selectedSources = folder.Documents
            .Where(source => source.IncludeInFolderSlides)
            .OrderBy(source => source.FolderSourceOrder)
            .ThenBy(source => source.CreatedAt)
            .ToList();

        var readySources = selectedSources
            .Where(source => source.Status == DocumentStatus.Completed && !string.IsNullOrWhiteSpace(source.ExtractedText))
            .ToList();

        if (request.SourceIds?.Count > 0)
        {
            var primarySourceId = request.SourceIds.First();
            var primarySource = readySources.FirstOrDefault(source => source.Id == primarySourceId);
            return primarySource == null
                ? _generationReadinessService.GetAggregateReadiness(Array.Empty<DocumentGenerationReadiness>(), request.ConfirmLowConfidence)
                : await _generationReadinessService.GetReadinessAsync(primarySource, request.ConfirmLowConfidence);
        }

        var readinessResults = await Task.WhenAll(
            readySources.Select(source => _generationReadinessService.GetReadinessAsync(source, request.ConfirmLowConfidence)));
        return _generationReadinessService.GetAggregateReadiness(readinessResults, request.ConfirmLowConfidence);
    }

    private static bool IsValidSlideCount(int desiredSlideCount)
        => desiredSlideCount is >= 5 and <= 18;

    private static bool IsSupportedGenerationMode(string? mode)
        => string.IsNullOrWhiteSpace(mode)
            || mode.Equals("lecture", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("summary", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("exam-review", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("timeline", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedSpeedMode(string? speedMode)
        => string.IsNullOrWhiteSpace(speedMode)
            || speedMode.Equals("fast", StringComparison.OrdinalIgnoreCase)
            || speedMode.Equals("balanced", StringComparison.OrdinalIgnoreCase)
            || speedMode.Equals("quality", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSpeedMode(string? speedMode)
        => string.IsNullOrWhiteSpace(speedMode)
            ? "quality"
            : speedMode.Trim().ToLowerInvariant() switch
            {
                "fast" => "fast",
                "balanced" => "balanced",
                "quality" => "quality",
                _ => "quality"
            };

    private static SlideImageSourcingOptions ResolveImageSourcingOptions(string? speedMode)
        => NormalizeSpeedMode(speedMode) == "fast"
            ? SlideImageSourcingOptions.FastPreview
            : SlideImageSourcingOptions.Quality;

    private static bool IsSlideSchemaMissing(PostgresException ex)
    {
        if (ex.SqlState != PostgresErrorCodes.UndefinedTable && ex.SqlState != PostgresErrorCodes.UndefinedColumn)
        {
            return false;
        }

        var messageText = ex.MessageText ?? string.Empty;
        return messageText.Contains("slide_decks", StringComparison.OrdinalIgnoreCase)
            || messageText.Contains("slide_items", StringComparison.OrdinalIgnoreCase)
            || messageText.Contains("folder_projects", StringComparison.OrdinalIgnoreCase)
            || messageText.Contains("folder_project_id", StringComparison.OrdinalIgnoreCase)
            || messageText.Contains("image_plan", StringComparison.OrdinalIgnoreCase)
            || messageText.Contains("image_candidates", StringComparison.OrdinalIgnoreCase)
            || messageText.Contains("selected_image_key", StringComparison.OrdinalIgnoreCase)
            || messageText.Contains("editor_state", StringComparison.OrdinalIgnoreCase);
    }

    private static void RefreshImageScaffold(SlideItem item, bool preserveExistingCandidates = true)
    {
        var candidates = preserveExistingCandidates ? item.GetImageCandidates() : new List<SlideImageCandidate>();
        item.SetImagePlan(BuildDefaultImagePlan(item));
        item.SetImageCandidates(candidates);

        if (candidates.Count == 0)
        {
            item.SelectedImageKey = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(item.SelectedImageKey))
        {
            var selectedCandidate = candidates.FirstOrDefault(candidate =>
                candidate.IsSelected && !string.IsNullOrWhiteSpace(candidate.Key));
            item.SelectedImageKey = selectedCandidate?.Key;
            return;
        }

        if (candidates.Count > 0 && !candidates.Any(candidate =>
                string.Equals(candidate.Key, item.SelectedImageKey, StringComparison.OrdinalIgnoreCase)))
        {
            item.SelectedImageKey = null;
        }
    }

    private static SlideImagePlan BuildDefaultImagePlan(SlideItem item)
    {
        var needsImage = item.SlideType is not (SlideItemType.SectionDivider or SlideItemType.Quote);
        var evidenceDebug = item.GetEvidenceDebug();
        var heading = SanitizeImageText(item.Heading, 160) ?? $"Slide {item.SlideIndex}";
        var subheading = SanitizeImageText(item.Subheading, 220);
        var goal = SanitizeImageText(item.Goal, 180);
        var visualRole = needsImage
            ? FirstNonEmpty(evidenceDebug?.VisualRole, item.VisualRole, ResolveVisualRole(item.SlideType)) ?? "supporting"
            : "none";
        var chartIntent = FirstNonEmpty(evidenceDebug?.ChartIntent, item.ChartIntent);
        var descriptor = string.Join(", ", new[] { heading, goal }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var searchQueries = new List<string>();

        if (needsImage)
        {
            searchQueries.Add(heading);

            if (!string.IsNullOrWhiteSpace(goal) && !string.Equals(goal, heading, StringComparison.OrdinalIgnoreCase))
            {
                searchQueries.Add($"{heading} {goal}");
            }

            if (!string.IsNullOrWhiteSpace(subheading))
            {
                searchQueries.Add($"{heading} {subheading}");
            }
        }

        return new SlideImagePlan
        {
            NeedsImage = needsImage,
            Reason = needsImage
                ? "Default scaffold before Qwen image planning runs."
                : "Default scaffold keeps this slide text-only before Qwen image planning runs.",
            VisualRole = visualRole,
            ImageRendering = visualRole.Contains("diagram", StringComparison.OrdinalIgnoreCase) || visualRole.Contains("process", StringComparison.OrdinalIgnoreCase)
                ? "diagrammatic-illustration"
                : "vector-illustration",
            ImagePalette = item.NeedsChartReview || evidenceDebug?.NeedsChartReview == true
                ? "neutral-review"
                : "academic-blue",
            SourceEvidence = string.Join(" | ", new[]
            {
                heading,
                goal ?? string.Empty,
                chartIntent == null ? string.Empty : $"chart intent: {chartIntent}"
            }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Take(3)),
            AltText = needsImage
                ? $"Minh hoa cho slide {item.SlideIndex}: {heading}"
                : $"Slide {item.SlideIndex} uu tien text-only",
            RedactedPrompt = needsImage
                ? $"Tao mot hinh anh slide-style theo huong {visualRole}, minh hoa cho: {descriptor}."
                : null,
            SearchQueries = searchQueries
                .Where(query => !string.IsNullOrWhiteSpace(query))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList(),
            GenerationPrompt = needsImage
                ? $"Mot hinh anh ngang 16:9, bo cuc sach, phu hop voi slide trinh bay, minh hoa cho: {descriptor}."
                : null,
            NegativePrompt = needsImage
                ? "Khong watermark, khong text overlay, khong giao dien phan mem, khong noi dung nhay cam."
                : null,
            StatusHint = needsImage ? "queued" : "no-image-needed",
            LastResultMessage = needsImage
                ? "Image workflow se chay ngay sau khi slide duoc sinh xong."
                : "Slide nay uu tien text-only."
        };
    }

    private static string ResolveVisualRole(SlideItemType slideType)
    {
        return slideType switch
        {
            SlideItemType.Title => "hero",
            SlideItemType.Highlight => "background",
            SlideItemType.Stat => "side-accent-right",
            SlideItemType.Content => "side-accent-right",
            _ => "supporting"
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static List<SlideImageCandidate> NormalizeImageCandidates(
        IReadOnlyCollection<SlideImageCandidate> candidates,
        string? selectedImageKey)
    {
        var normalized = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Key))
            .Select(candidate => new SlideImageCandidate
            {
                Key = candidate.Key,
                SourceType = string.IsNullOrWhiteSpace(candidate.SourceType) ? "web" : candidate.SourceType,
                Provider = candidate.Provider,
                OriginUrl = candidate.OriginUrl,
                LocalAssetUrl = candidate.LocalAssetUrl,
                ThumbnailUrl = candidate.ThumbnailUrl,
                AltText = candidate.AltText,
                LicenseLabel = candidate.LicenseLabel,
                AttributionText = candidate.AttributionText,
                Width = candidate.Width,
                Height = candidate.Height,
                Score = candidate.Score,
                IsSelected = candidate.IsSelected,
                LayoutMode = candidate.LayoutMode
            })
            .ToList();

        var effectiveSelectedKey = string.IsNullOrWhiteSpace(selectedImageKey)
            ? normalized.FirstOrDefault(candidate => candidate.IsSelected)?.Key
            : selectedImageKey;

        if (string.IsNullOrWhiteSpace(effectiveSelectedKey))
        {
            return normalized;
        }

        foreach (var candidate in normalized)
        {
            candidate.IsSelected = string.Equals(candidate.Key, effectiveSelectedKey, StringComparison.OrdinalIgnoreCase);
        }

        return normalized;
    }

    private static SlideImageCandidate? ResolveSelectedImage(
        IReadOnlyCollection<SlideImageCandidate> candidates,
        string? selectedImageKey)
    {
        if (!string.IsNullOrWhiteSpace(selectedImageKey))
        {
            var match = candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, selectedImageKey, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return match;
            }
        }

        return candidates.FirstOrDefault(candidate => candidate.IsSelected);
    }

    private static SlideImageState BuildImageState(
        SlideItem item,
        SlideImagePlan imagePlan,
        IReadOnlyCollection<SlideImageCandidate> candidates,
        SlideImageCandidate? selectedImage)
    {
        var needsImage = imagePlan.NeedsImage;
        var status = ResolveImageStatus(item.Status, imagePlan, needsImage, candidates.Count, selectedImage);

        return new SlideImageState
        {
            NeedsImage = needsImage,
            Status = status,
            Message = imagePlan.LastResultMessage ?? ResolveImageMessage(status, candidates.Count),
            Detail = needsImage
                ? $"visualRole: {imagePlan.VisualRole ?? "supporting"}"
                : "Slide nay duoc de xuat giu text-only.",
            CandidateCount = candidates.Count,
            SelectedImageKey = selectedImage?.Key ?? item.SelectedImageKey,
            Error = status == "failed" ? "Image pipeline gap loi o pha truoc." : null
        };
    }

    private static string ResolveImageStatus(
        SlideItemStatus slideStatus,
        SlideImagePlan imagePlan,
        bool needsImage,
        int candidateCount,
        SlideImageCandidate? selectedImage)
    {
        if (!needsImage)
        {
            return string.Equals(imagePlan.StatusHint, "image-plan-invalid", StringComparison.OrdinalIgnoreCase)
                ? "image-plan-invalid"
                : "no-image-needed";
        }

        if (selectedImage != null || candidateCount > 0)
        {
            return "ready";
        }

        if (!string.IsNullOrWhiteSpace(imagePlan.StatusHint))
        {
            return imagePlan.StatusHint;
        }

        return slideStatus switch
        {
            SlideItemStatus.Pending or SlideItemStatus.Generating => "queued",
            SlideItemStatus.Failed => "failed",
            _ => "not-requested"
        };
    }

    private static string ResolveImageMessage(string status, int candidateCount)
    {
        return status switch
        {
            "no-image-needed" => "Slide nay uu tien text-only de giu nhip doc va de bao toan do ro cua thong diep.",
            "image-plan-invalid" => "Image plan khong hop le, slide nay tam thoi duoc giu text-only.",
            "ready" when candidateCount > 0 => $"Da co {candidateCount} image candidate cho slide nay.",
            "ready" => "Da co media duoc gan cho slide nay.",
            "queued" => "Image workflow se duoc noi vao o phase tiep theo sau khi slide on dinh.",
            "failed" => "Image workflow gap loi va can thu lai sau khi backend image pipeline san sang.",
            _ => "Image pipeline chua duoc chay cho slide nay."
        };
    }

    private IActionResult GenerationGateBlocked(DocumentGenerationReadiness readiness)
        => BadRequest(new
        {
            success = false,
            code = "generation_readiness_blocked",
            message = "Document extraction confidence is too low for automatic generation.",
            generationReadiness = readiness
        });

    private static string BuildReadinessDetail(DocumentGenerationReadiness readiness)
        => readiness.Reasons.Count == 0
            ? $"confidence={readiness.Confidence:P0}, status={readiness.Status}"
            : string.Join(" ", readiness.Reasons.Take(3));

    private static string? SanitizeImageText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(" ", value
            .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            .Trim();

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].Trim();
    }

    private async Task<IActionResult?> EnsureDeckAccessAsync(int deckId)
    {
        var deck = await _slideDeckRepository.GetByIdAsync(deckId);
        if (deck == null)
        {
            return NotFound("Slide deck not found");
        }

        var ownerUserId = deck.Document?.UploadedBy ?? deck.FolderProject?.UploadedBy;
        return EnsureOwnerAccess(ownerUserId);
    }

    private async Task<DeckExportAccessResult> GetDeckForExportAsync(int deckId)
    {
        var deck = await _slideDeckRepository.GetByIdAsync(deckId);
        if (deck == null)
        {
            return new DeckExportAccessResult(null, NotFound("Slide deck not found"));
        }

        var ownerUserId = deck.Document?.UploadedBy ?? deck.FolderProject?.UploadedBy;
        var authResult = EnsureOwnerAccess(ownerUserId);
        return authResult == null
            ? new DeckExportAccessResult(deck, null)
            : new DeckExportAccessResult(null, authResult);
    }

    private static string BuildExportFileName(SlideDeck deck, string extension)
    {
        var rawTitle = string.IsNullOrWhiteSpace(deck.Title)
            ? $"slide-deck-{deck.Id}"
            : deck.Title.Trim();
        var builder = new StringBuilder(rawTitle.Length);

        foreach (var character in rawTitle.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var safeTitle = builder.ToString();
        while (safeTitle.Contains("--", StringComparison.Ordinal))
        {
            safeTitle = safeTitle.Replace("--", "-", StringComparison.Ordinal);
        }

        safeTitle = safeTitle.Trim('-');
        if (string.IsNullOrWhiteSpace(safeTitle))
        {
            safeTitle = $"slide-deck-{deck.Id}";
        }

        if (safeTitle.Length > 80)
        {
            safeTitle = safeTitle[..80].Trim('-');
        }

        return $"{safeTitle}.{extension.TrimStart('.')}";
    }

    private Task<FolderProject?> GetFolderAsync(int folderId)
        => _folderProjectRepository.GetByIdAsync(folderId);
}

internal sealed record DeckExportAccessResult(SlideDeck? Deck, IActionResult? ErrorResult);

public class GenerateSlidesRequest
{
    public required int DocumentId { get; set; }
    public int DesiredSlideCount { get; set; } = 8;
    public string? ThemeKey { get; set; }
    public string? Audience { get; set; }
    public string? Tone { get; set; }
    public string? NarrativeGoal { get; set; }
    public string? LanguageStyle { get; set; }
    public List<int>? SourceIds { get; set; }
    public List<string>? SelectedSectionIds { get; set; }
    public string? Mode { get; set; }
    public string? SpeedMode { get; set; }
    public string? ScopePolicy { get; set; }
    public bool ConfirmLowConfidence { get; set; }
}

public class GenerateFolderSlidesRequest
{
    public int DesiredSlideCount { get; set; } = 8;
    public string? ThemeKey { get; set; }
    public string? Audience { get; set; }
    public string? Tone { get; set; }
    public string? NarrativeGoal { get; set; }
    public string? LanguageStyle { get; set; }
    public List<int>? SourceIds { get; set; }
    public List<string>? SelectedSectionIds { get; set; }
    public string? Mode { get; set; }
    public string? SpeedMode { get; set; } = "fast";
    public string? ScopePolicy { get; set; }
    public bool ConfirmLowConfidence { get; set; }
}

public class UpdateSlideItemRequest
{
    public string? Heading { get; set; }
    public string? Subheading { get; set; }
    public string? Goal { get; set; }
    public List<string>? BodyBlocks { get; set; }
    public string? SpeakerNotes { get; set; }
    public string? AccentTone { get; set; }
    public SlideEditorState? EditorState { get; set; }
}

public class SelectSlideImageRequest
{
    public string? CandidateKey { get; set; }
}

internal sealed class SlideGenerationTarget
{
    public int? DocumentId { get; init; }
    public int? FolderProjectId { get; init; }
    public int DesiredSlideCount { get; init; } = 8;
    public string? ThemeKey { get; init; }
    public string? Audience { get; init; }
    public string? Tone { get; init; }
    public string? NarrativeGoal { get; init; }
    public string? LanguageStyle { get; init; }
    public List<int>? SourceIds { get; init; }
    public List<string>? SelectedSectionIds { get; init; }
    public string? Mode { get; init; }
    public string? SpeedMode { get; init; }
    public string? ScopePolicy { get; init; }
    public bool ConfirmLowConfidence { get; init; }
}

internal sealed class SlideGenerationContext
{
    public bool Success { get; private init; }
    public int? DocumentId { get; private init; }
    public int? FolderProjectId { get; private init; }
    public string ExtractedText { get; private init; } = string.Empty;
    public ProcessedContent ProcessedContent { get; private init; } = new();
    public HashSet<string> AllowedChunkIds { get; private init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Error { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static SlideGenerationContext FromDocument(int documentId, string extractedText, ProcessedContent processedContent)
        => new()
        {
            Success = true,
            DocumentId = documentId,
            ExtractedText = extractedText,
            ProcessedContent = processedContent,
            AllowedChunkIds = processedContent.CoverageMap
                .Select(chunk => chunk.ChunkId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
        };

    public static SlideGenerationContext FromFolder(int folderProjectId, string extractedText, ProcessedContent processedContent)
        => new()
        {
            Success = true,
            FolderProjectId = folderProjectId,
            ExtractedText = extractedText,
            ProcessedContent = processedContent,
            AllowedChunkIds = processedContent.CoverageMap
                .Select(chunk => chunk.ChunkId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
        };

    public static SlideGenerationContext Fail(string error, string errorMessage)
        => new()
        {
            Success = false,
            Error = error,
            ErrorMessage = errorMessage
        };
}
