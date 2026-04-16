using System.Text.Json;
using ELearnGamePlatform.API.Contracts;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace ELearnGamePlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SlidesController : ControllerBase
{
    private readonly IDocumentRepository _documentRepository;
    private readonly ISlideDeckRepository _slideDeckRepository;
    private readonly ISlideGenerator _slideGenerator;
    private readonly ISlideImageService _slideImageService;
    private readonly ISlideGenerationJobStore _jobStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlidesController> _logger;

    public SlidesController(
        IDocumentRepository documentRepository,
        ISlideDeckRepository slideDeckRepository,
        ISlideGenerator slideGenerator,
        ISlideImageService slideImageService,
        ISlideGenerationJobStore jobStore,
        IServiceScopeFactory scopeFactory,
        ILogger<SlidesController> logger)
    {
        _documentRepository = documentRepository;
        _slideDeckRepository = slideDeckRepository;
        _slideGenerator = slideGenerator;
        _slideImageService = slideImageService;
        _jobStore = jobStore;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [HttpPost("generate/start")]
    public async Task<IActionResult> StartGenerateSlides([FromBody] GenerateSlidesRequest request)
    {
        if (request.DesiredSlideCount is < 5 or > 12)
        {
            return BadRequest("DesiredSlideCount must be between 5 and 12");
        }

        try
        {
            _ = await _slideDeckRepository.GetLatestByDocumentIdAsync(request.DocumentId);
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }

        var jobId = _jobStore.CreateJob(request.DocumentId, request.DesiredSlideCount);
        _jobStore.TryGetJob(jobId, out var state);
        _ = Task.Run(() => RunGenerateSlidesJobAsync(jobId, new SlideGenerationTarget
        {
            DocumentId = request.DocumentId,
            DesiredSlideCount = request.DesiredSlideCount,
            ThemeKey = request.ThemeKey,
            Audience = request.Audience,
            Tone = request.Tone,
            NarrativeGoal = request.NarrativeGoal,
            LanguageStyle = request.LanguageStyle
        }));

        return Accepted(new
        {
            jobId,
            status = "queued",
            progressUrl = $"/api/slides/generate/progress/{jobId}",
            resultUrl = $"/api/slides/document/{request.DocumentId}",
            progress = state == null ? null : JobProgressPayloadFactory.BuildSlide(state)
        });
    }

    [HttpGet("generate/progress/{jobId}")]
    public IActionResult GetGenerateProgress(string jobId)
    {
        if (!_jobStore.TryGetJob(jobId, out var state) || state == null)
        {
            return NotFound("Job not found");
        }

        return Ok(JobProgressPayloadFactory.BuildSlide(state));
    }

    [HttpGet("document/{documentId}")]
    public async Task<IActionResult> GetDeckByDocument(int documentId)
    {
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

    [HttpPut("{deckId}/items/{itemId}")]
    public async Task<IActionResult> UpdateSlideItem(int deckId, int itemId, [FromBody] UpdateSlideItemRequest request)
    {
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

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var documentRepository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var folderProjectRepository = scope.ServiceProvider.GetRequiredService<IFolderProjectRepository>();
            var slideDeckRepository = scope.ServiceProvider.GetRequiredService<ISlideDeckRepository>();
            var slideGenerator = scope.ServiceProvider.GetRequiredService<ISlideGenerator>();
            var slideImageService = scope.ServiceProvider.GetRequiredService<ISlideImageService>();

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

            var context = await ResolveGenerationContextAsync(target, documentRepository, folderProjectRepository);
            if (!context.Success)
            {
                FailJob(jobId, context.Error ?? "Khong the tao slide deck", context.ErrorMessage ?? "Khong the tao slide deck");
                return;
            }

            var brief = BuildBrief(target);
            var outlineProgress = new Progress<SlideGenerationProgressUpdate>(update =>
            {
                UpdateJob(jobId, state =>
                {
                    ApplyGeneratorProgress(state, update, 2, 6);
                });
            });

            var outline = await slideGenerator.GenerateOutlineAsync(
                context.ExtractedText,
                context.ProcessedContent,
                brief,
                target.DesiredSlideCount,
                outlineProgress);

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

            persistedDeck = context.FolderProjectId.HasValue
                ? await slideDeckRepository.ReplaceForFolderAsync(deck, placeholderItems)
                : await slideDeckRepository.ReplaceForDocumentAsync(deck, placeholderItems);

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
                var item = slideItems[index];
                var outlineSlide = outline.Slides.First(slide => slide.SlideIndex == item.SlideIndex);

                item.Status = SlideItemStatus.Generating;
                await slideDeckRepository.UpdateItemAsync(item);

                UpdateJob(jobId, state =>
                {
                    state.Percent = MapProgress(26, 88, index, slideItems.Count);
                    state.Stage = "generating-slides";
                    state.StageLabel = "Dang sinh slide";
                    state.Message = $"Dang tao slide {index + 1}/{slideItems.Count}";
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
                    });
                });

                var content = await slideGenerator.GenerateSlideAsync(
                    context.ExtractedText,
                    context.ProcessedContent,
                    brief,
                    outlineSlide,
                    index + 1,
                    slideItems.Count,
                    slideProgress);

                item.Heading = content.Heading ?? item.Heading;
                item.Subheading = content.Subheading;
                item.Goal = content.Goal;
                item.SpeakerNotes = content.SpeakerNotes;
                item.AccentTone = content.AccentTone;
                item.VerifierScore = content.VerifierScore;
                item.SetVerifierIssues(content.VerifierIssues);
                item.SetBodyBlocks(content.BodyBlocks);
                item.SetEditorState(item.BuildDefaultEditorState());
                RefreshImageScaffold(item);
                await slideImageService.SourceImagesForItemAsync(item);
                item.Status = SlideItemStatus.Completed;
                await slideDeckRepository.UpdateItemAsync(item);

                UpdateJob(jobId, state =>
                {
                    state.SlidesGenerated = index + 1;
                    state.Percent = MapProgress(30, 90, index + 1, slideItems.Count);
                    state.Stage = "generating-slides";
                    state.StageLabel = "Dang sinh slide";
                    state.Message = $"Da xong slide {index + 1}/{slideItems.Count}";
                    state.Detail = item.Heading;
                    state.Current = index + 1;
                    state.Total = slideItems.Count;
                    state.UnitLabel = "slide";
                    state.StageIndex = 4;
                    state.StageCount = 6;
                    UpdateEta(state);
                });
            }

            persistedDeck.Status = SlideDeckStatus.Completed;
            persistedDeck.CompletedAt = DateTime.UtcNow;
            persistedDeck.UpdatedAt = DateTime.UtcNow;
            await slideDeckRepository.UpdateDeckAsync(persistedDeck);

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
        }
        catch (Exception ex)
        {
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
                lowConfidenceCount = deck.Items.Count(item => (item.VerifierScore ?? 100) < 70),
                unknownCount = deck.Items.Count(item => !item.VerifierScore.HasValue)
            },
            generationProgress = JobProgressPayloadFactory.BuildSlide(jobState, deck)
        };
    }

    private static object BuildSlideItemPayload(SlideItem item)
    {
        var imagePlan = item.GetImagePlan() ?? BuildDefaultImagePlan(item);
        var imageCandidates = NormalizeImageCandidates(item.GetImageCandidates(), item.SelectedImageKey);
        var selectedImage = ResolveSelectedImage(imageCandidates, item.SelectedImageKey);
        var imageState = BuildImageState(item, imagePlan, imageCandidates, selectedImage);

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
            bodyBlocks = item.GetBodyBlocks(),
            item.SpeakerNotes,
            item.AccentTone,
            editorState = item.GetEditorState(),
            imageState,
            selectedImage,
            imageCandidates,
            quality = BuildQualityPayload(item.VerifierScore, item.GetVerifierIssues()),
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
            isLowConfidence = score.HasValue && score.Value < 70,
            isUnknown = !score.HasValue
        };
    }

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
            Goal = slide.Goal
        };
        item.SetBodyBlocks(new List<string>());
        item.SetEditorState(item.BuildDefaultEditorState());
        RefreshImageScaffold(item, preserveExistingCandidates: false);
        return item;
    }

    private static ProcessedContent BuildProcessedContentFromDocument(Document document)
    {
        return new ProcessedContent
        {
            MainTopics = document.GetMainTopics(),
            KeyPoints = document.GetKeyPoints(),
            Summary = document.Summary,
            Language = document.Language,
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
                : request.LanguageStyle.Trim()
        };
    }

    private static ProcessedContent BuildProcessedContentFromSources(IReadOnlyCollection<Document> sources)
    {
        var orderedSources = sources
            .Where(source => !string.IsNullOrWhiteSpace(source.ExtractedText))
            .OrderBy(source => source.FolderSourceOrder)
            .ThenBy(source => source.CreatedAt)
            .ToList();

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
                .Select(source => source.Language)
                .FirstOrDefault(language => !string.IsNullOrWhiteSpace(language)),
            CoverageMap = orderedSources
                .SelectMany((source, sourceIndex) => source.GetCoverageMap().Select((chunk, chunkIndex) => new DocumentCoverageChunk
                {
                    ChunkNumber = sourceIndex * 1000 + chunkIndex + 1,
                    ChunkId = $"{source.Id}-{chunk.ChunkId}",
                    Zone = chunk.Zone,
                    Label = $"{source.FileName}: {chunk.Label}",
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

    private async Task<SlideGenerationContext> ResolveGenerationContextAsync(
        SlideGenerationTarget target,
        IDocumentRepository documentRepository,
        IFolderProjectRepository folderProjectRepository)
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

            return SlideGenerationContext.FromDocument(
                document.Id,
                document.ExtractedText,
                BuildProcessedContentFromDocument(document));
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

            return SlideGenerationContext.FromFolder(
                folder.Id,
                BuildCombinedExtractedText(readySources),
                BuildProcessedContentFromSources(readySources));
        }

        return SlideGenerationContext.Fail("Missing slide generation owner", "Khong xac dinh duoc owner cua slide deck");
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

    private void UpdateJob(string jobId, Action<SlideGenerationJobState> updater)
        => _jobStore.UpdateJob(jobId, updater);

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
        if (request.DesiredSlideCount is < 5 or > 12)
        {
            return BadRequest("DesiredSlideCount must be between 5 and 12");
        }

        try
        {
            _ = await _slideDeckRepository.GetLatestByFolderIdAsync(folderId);
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }

        var jobId = _jobStore.CreateFolderJob(folderId, request.DesiredSlideCount);
        _jobStore.TryGetJob(jobId, out var state);
        _ = Task.Run(() => RunGenerateSlidesJobAsync(jobId, new SlideGenerationTarget
        {
            FolderProjectId = folderId,
            DesiredSlideCount = request.DesiredSlideCount,
            ThemeKey = request.ThemeKey,
            Audience = request.Audience,
            Tone = request.Tone,
            NarrativeGoal = request.NarrativeGoal,
            LanguageStyle = request.LanguageStyle
        }));

        return Accepted(new
        {
            jobId,
            status = "queued",
            progressUrl = $"/api/slides/generate/progress/{jobId}",
            resultUrl = $"/api/slides/folders/{folderId}",
            progress = state == null ? null : JobProgressPayloadFactory.BuildSlide(state)
        });
    }

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
        var heading = SanitizeImageText(item.Heading, 160) ?? $"Slide {item.SlideIndex}";
        var subheading = SanitizeImageText(item.Subheading, 220);
        var goal = SanitizeImageText(item.Goal, 180);
        var visualRole = needsImage ? ResolveVisualRole(item.SlideType) : "none";
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
            VisualRole = visualRole,
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
            return "no-image-needed";
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
            "ready" when candidateCount > 0 => $"Da co {candidateCount} image candidate cho slide nay.",
            "ready" => "Da co media duoc gan cho slide nay.",
            "queued" => "Image workflow se duoc noi vao o phase tiep theo sau khi slide on dinh.",
            "failed" => "Image workflow gap loi va can thu lai sau khi backend image pipeline san sang.",
            _ => "Image pipeline chua duoc chay cho slide nay."
        };
    }

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
}

public class GenerateSlidesRequest
{
    public required int DocumentId { get; set; }
    public int DesiredSlideCount { get; set; } = 8;
    public string? ThemeKey { get; set; }
    public string? Audience { get; set; }
    public string? Tone { get; set; }
    public string? NarrativeGoal { get; set; }
    public string? LanguageStyle { get; set; }
}

public class GenerateFolderSlidesRequest
{
    public int DesiredSlideCount { get; set; } = 8;
    public string? ThemeKey { get; set; }
    public string? Audience { get; set; }
    public string? Tone { get; set; }
    public string? NarrativeGoal { get; set; }
    public string? LanguageStyle { get; set; }
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
}

internal sealed class SlideGenerationContext
{
    public bool Success { get; private init; }
    public int? DocumentId { get; private init; }
    public int? FolderProjectId { get; private init; }
    public string ExtractedText { get; private init; } = string.Empty;
    public ProcessedContent ProcessedContent { get; private init; } = new();
    public string? Error { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static SlideGenerationContext FromDocument(int documentId, string extractedText, ProcessedContent processedContent)
        => new()
        {
            Success = true,
            DocumentId = documentId,
            ExtractedText = extractedText,
            ProcessedContent = processedContent
        };

    public static SlideGenerationContext FromFolder(int folderProjectId, string extractedText, ProcessedContent processedContent)
        => new()
        {
            Success = true,
            FolderProjectId = folderProjectId,
            ExtractedText = extractedText,
            ProcessedContent = processedContent
        };

    public static SlideGenerationContext Fail(string error, string errorMessage)
        => new()
        {
            Success = false,
            Error = error,
            ErrorMessage = errorMessage
        };
}
