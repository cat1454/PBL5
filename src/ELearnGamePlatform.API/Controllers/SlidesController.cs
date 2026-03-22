using System.Text.Json;
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
    private readonly ISlideGenerationJobStore _jobStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlidesController> _logger;

    public SlidesController(
        IDocumentRepository documentRepository,
        ISlideDeckRepository slideDeckRepository,
        ISlideGenerator slideGenerator,
        ISlideGenerationJobStore jobStore,
        IServiceScopeFactory scopeFactory,
        ILogger<SlidesController> logger)
    {
        _documentRepository = documentRepository;
        _slideDeckRepository = slideDeckRepository;
        _slideGenerator = slideGenerator;
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
        _ = Task.Run(() => RunGenerateSlidesJobAsync(jobId, request));

        return Accepted(new
        {
            jobId,
            status = "queued",
            progressUrl = $"/api/slides/generate/progress/{jobId}",
            resultUrl = $"/api/slides/document/{request.DocumentId}"
        });
    }

    [HttpGet("generate/progress/{jobId}")]
    public IActionResult GetGenerateProgress(string jobId)
    {
        if (!_jobStore.TryGetJob(jobId, out var state) || state == null)
        {
            return NotFound("Job not found");
        }

        return Ok(state);
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

            item.Heading = string.IsNullOrWhiteSpace(request.Heading) ? item.Heading : request.Heading.Trim();
            item.Subheading = request.Subheading?.Trim();
            item.Goal = request.Goal?.Trim();
            item.SpeakerNotes = request.SpeakerNotes?.Trim();
            item.AccentTone = request.AccentTone?.Trim();
            item.SetBodyBlocks(request.BodyBlocks?
                .Where(block => !string.IsNullOrWhiteSpace(block))
                .Select(block => block.Trim())
                .ToList() ?? new List<string>());
            item.VerifierScore = null;
            item.SetVerifierIssues(new List<string>
            {
                "Slide da duoc chinh sua thu cong sau khi verifier chay.",
                "Can sinh lai hoac verifier lai neu muon diem tin cay moi."
            });

            await _slideDeckRepository.UpdateItemAsync(item);
            return Ok(BuildSlideItemPayload(item));
        }
        catch (PostgresException ex) when (IsSlideSchemaMissing(ex))
        {
            return SlideSchemaUnavailable();
        }
    }

    private async Task RunGenerateSlidesJobAsync(string jobId, GenerateSlidesRequest request)
    {
        SlideDeck? persistedDeck = null;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var documentRepository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var slideDeckRepository = scope.ServiceProvider.GetRequiredService<ISlideDeckRepository>();
            var slideGenerator = scope.ServiceProvider.GetRequiredService<ISlideGenerator>();

            UpdateJob(jobId, state =>
            {
                state.Status = "running";
                state.Percent = 3;
                state.Stage = "validating-document";
                state.StageLabel = "Kiem tra tai lieu";
                state.Message = "Dang kiem tra du lieu truoc khi tao slide";
                state.Detail = "Can document da OCR va phan tich xong";
                state.StageIndex = 1;
                state.StageCount = 6;
                state.Error = null;
                UpdateEta(state);
            });

            var document = await documentRepository.GetByIdAsync(request.DocumentId);
            if (document == null)
            {
                FailJob(jobId, "Document not found", "Khong tim thay tai lieu");
                return;
            }

            if (string.IsNullOrWhiteSpace(document.ExtractedText))
            {
                FailJob(jobId, "Document has not been processed yet", "Tai lieu chua co noi dung ExtractedText");
                return;
            }

            var processedContent = BuildProcessedContentFromDocument(document);
            var brief = BuildBrief(request);
            var outlineProgress = new Progress<SlideGenerationProgressUpdate>(update =>
            {
                UpdateJob(jobId, state =>
                {
                    ApplyGeneratorProgress(state, update, 2, 6);
                });
            });

            var outline = await slideGenerator.GenerateOutlineAsync(
                document.ExtractedText,
                processedContent,
                brief,
                request.DesiredSlideCount,
                outlineProgress);

            var deck = new SlideDeck
            {
                DocumentId = document.Id,
                Status = SlideDeckStatus.GeneratingSlides,
                Title = outline.Title,
                Subtitle = outline.Subtitle,
                ThemeKey = outline.ThemeKey,
                OutlineJson = JsonSerializer.Serialize(outline)
            };

            var placeholderItems = outline.Slides
                .OrderBy(slide => slide.SlideIndex)
                .Select(slide => CreatePlaceholderItem(slide))
                .ToList();

            persistedDeck = await slideDeckRepository.ReplaceForDocumentAsync(deck, placeholderItems);

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
                    document.ExtractedText,
                    processedContent,
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

    private object BuildDeckPayload(SlideDeck deck, SlideGenerationJobState? jobState)
    {
        return new
        {
            id = deck.Id,
            documentId = deck.DocumentId,
            status = deck.Status.ToString(),
            title = deck.Title,
            subtitle = deck.Subtitle,
            themeKey = deck.ThemeKey,
            outline = DeserializeOutline(deck.OutlineJson),
            createdAt = deck.CreatedAt,
            updatedAt = deck.UpdatedAt,
            completedAt = deck.CompletedAt,
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
            generationProgress = jobState == null ? null : new
            {
                jobState.JobId,
                jobState.Status,
                jobState.Percent,
                jobState.Stage,
                jobState.StageLabel,
                jobState.Message,
                jobState.Detail,
                jobState.Current,
                jobState.Total,
                jobState.UnitLabel,
                jobState.StageIndex,
                jobState.StageCount,
                jobState.SlidesGenerated,
                jobState.ElapsedSeconds,
                jobState.EstimatedRemainingSeconds,
                jobState.Error
            }
        };
    }

    private static object BuildSlideItemPayload(SlideItem item)
    {
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

    private static SlideDeckBrief BuildBrief(GenerateSlidesRequest request)
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
            message = "Slide feature schema is not initialized. Run database migrations to create slide_decks and slide_items."
        });
    }

    private static bool IsSlideSchemaMissing(PostgresException ex)
    {
        if (ex.SqlState != PostgresErrorCodes.UndefinedTable)
        {
            return false;
        }

        var messageText = ex.MessageText ?? string.Empty;
        return messageText.Contains("slide_decks", StringComparison.OrdinalIgnoreCase)
            || messageText.Contains("slide_items", StringComparison.OrdinalIgnoreCase);
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

public class UpdateSlideItemRequest
{
    public string? Heading { get; set; }
    public string? Subheading { get; set; }
    public string? Goal { get; set; }
    public List<string>? BodyBlocks { get; set; }
    public string? SpeakerNotes { get; set; }
    public string? AccentTone { get; set; }
}
