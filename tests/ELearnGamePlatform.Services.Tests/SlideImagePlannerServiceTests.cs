using System.Net;
using System.Text;
using System.Text.Json;
using ELearnGamePlatform.API.Configuration;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class SlideImagePlannerServiceTests
{
    [Fact]
    public async Task PlanAsync_TextOnlyResponse_ReturnsNoImagePlan()
    {
        var ollama = new QueueOllamaService("""
{
  "needsImage": false,
  "reason": "This slide is a dense bullet summary and should remain text-only.",
  "visualRole": "none",
  "generationPrompt": null,
  "negativePrompt": null,
  "altText": null,
  "searchQueries": []
}
""");
        var planner = CreatePlanner(ollama);
        var item = CreateSlideItem(SlideItemType.Content, new[] { "Point one", "Point two", "Point three", "Point four" });

        var plan = await planner.PlanAsync(item);

        Assert.False(plan.NeedsImage);
        Assert.Equal("no-image-needed", plan.StatusHint);
        Assert.Equal("none", plan.VisualRole);
        Assert.Null(plan.GenerationPrompt);
        Assert.Empty(plan.SearchQueries);
    }

    [Fact]
    public async Task PlanAsync_ImageResponse_ReturnsGenerationFields()
    {
        var ollama = new QueueOllamaService("""
{
  "needsImage": true,
  "reason": "This workflow benefits from a visual process illustration.",
  "visualRole": "process",
  "generationPrompt": "Create a 16:9 academic presentation slide illustration showing a workflow layout with connected cards, arrows, and a central process diagram on a white background.",
  "negativePrompt": "No text, no logos, no watermark.",
  "altText": "Workflow process illustration.",
  "searchQueries": []
}
""");
        var planner = CreatePlanner(ollama);
        var item = CreateSlideItem(SlideItemType.Content, new[] { "Input", "Processing", "Output" });

        var plan = await planner.PlanAsync(item);

        Assert.True(plan.NeedsImage);
        Assert.Equal("process", plan.VisualRole);
        Assert.Contains("workflow", plan.GenerationPrompt);
        Assert.Equal("Workflow process illustration.", plan.AltText);
        Assert.Equal("This workflow benefits from a visual process illustration.", plan.Reason);
    }

    [Fact]
    public async Task PlanAsync_InvalidPromptRepairsOnceThenFallsBackToInvalidPlan()
    {
        var ollama = new QueueOllamaService(
            """
{
  "needsImage": true,
  "reason": "Needs an image.",
  "visualRole": "conceptual",
  "generationPrompt": "Make a logo with readable text.",
  "negativePrompt": null,
  "altText": "Bad prompt.",
  "searchQueries": []
}
""",
            """
{
  "needsImage": true,
  "reason": "Still invalid.",
  "visualRole": "conceptual",
  "generationPrompt": "A logo and watermark with text labels.",
  "negativePrompt": null,
  "altText": "Still bad.",
  "searchQueries": []
}
""");
        var planner = CreatePlanner(ollama);

        var plan = await planner.PlanAsync(CreateSlideItem());

        Assert.False(plan.NeedsImage);
        Assert.Equal("image-plan-invalid", plan.StatusHint);
        Assert.Equal(2, ollama.StructuredCallCount);
    }

    [Fact]
    public async Task SourceImagesForItemAsync_NoImagePlan_DoesNotCallHttpAndClearsCandidates()
    {
        var item = CreateSlideItem();
        item.SelectedImageKey = "existing";
        item.SetImageCandidates(new List<SlideImageCandidate>
        {
            new() { Key = "existing", LocalAssetUrl = "/old.png", IsSelected = true }
        });

        var planner = new FixedPlanner(new SlideImagePlan
        {
            NeedsImage = false,
            VisualRole = "none",
            Reason = "Text-only slide.",
            StatusHint = "no-image-needed",
            LastResultMessage = "Text-only slide."
        });
        var handler = new CapturingHttpHandler();
        var service = CreateImageService(planner, handler);

        await service.SourceImagesForItemAsync(item);

        Assert.Equal(0, handler.RequestCount);
        Assert.Null(item.SelectedImageKey);
        Assert.Empty(item.GetImageCandidates());
        Assert.False(item.GetImagePlan()!.NeedsImage);
        Assert.Equal("no-image-needed", item.GetImagePlan()!.StatusHint);
    }

    [Fact]
    public async Task SourceImagesForItemAsync_ValidImagePlan_CallsOpenAiGenerationOnly()
    {
        var item = CreateSlideItem();
        item.Id = 42;
        item.SlideDeckId = 7;
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        var planner = new FixedPlanner(new SlideImagePlan
        {
            NeedsImage = true,
            VisualRole = "process",
            GenerationPrompt = "Create a 16:9 academic presentation slide illustration showing a process workflow layout with connected cards and a central diagram.",
            NegativePrompt = "No text.",
            AltText = "Process workflow.",
            StatusHint = "queued"
        });
        var handler = new CapturingHttpHandler();
        var service = CreateImageService(planner, handler);

        await service.SourceImagesForItemAsync(item);

        Assert.Single(handler.Requests);
        Assert.Equal("api.openai.com", handler.Requests[0].RequestUri!.Host);
        Assert.Single(item.GetImageCandidates());
        Assert.Equal("generated", item.GetImageCandidates()[0].SourceType);
        Assert.Equal("generated-42", item.SelectedImageKey);
    }

    [Fact]
    public async Task SourceImagesForItemAsync_GptImageModel_DoesNotSendUnsupportedResponseFormat()
    {
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        try
        {
            var item = CreateSlideItem();
            item.Id = 44;
            item.SlideDeckId = 7;
            var planner = new FixedPlanner(new SlideImagePlan
            {
                NeedsImage = true,
                VisualRole = "process",
                GenerationPrompt = "Create a 16:9 academic presentation slide illustration showing a process workflow layout with connected cards and a central diagram.",
                NegativePrompt = "No text.",
                AltText = "Process workflow.",
                StatusHint = "queued"
            });
            var handler = new CapturingHttpHandler();
            var settings = CreateImagePipelineSettings();
            settings.Generation.Model = "gpt-image-1.5";
            var service = CreateImageService(planner, handler, settings);

            await service.SourceImagesForItemAsync(item);

            var requestBody = Assert.Single(handler.RequestBodies);
            using var document = JsonDocument.Parse(requestBody);
            Assert.False(document.RootElement.TryGetProperty("response_format", out _));
            Assert.Equal("png", document.RootElement.GetProperty("output_format").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
        }
    }

    [Fact]
    public async Task SourceImagesForItemAsync_ConfiguredOpenAiApiKey_IsUsedWhenEnvironmentKeyMissing()
    {
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        try
        {
            var item = CreateSlideItem();
            item.Id = 43;
            item.SlideDeckId = 7;
            var planner = new FixedPlanner(new SlideImagePlan
            {
                NeedsImage = true,
                VisualRole = "process",
                GenerationPrompt = "Create a 16:9 academic presentation slide illustration showing a process workflow layout with connected cards and a central diagram.",
                NegativePrompt = "No text.",
                AltText = "Process workflow.",
                StatusHint = "queued"
            });
            var handler = new CapturingHttpHandler();
            var settings = CreateImagePipelineSettings();
            settings.Generation.ApiKey = "configured-test-key";
            var service = CreateImageService(planner, handler, settings);

            await service.SourceImagesForItemAsync(item);

            Assert.Single(handler.Requests);
            Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization?.Scheme);
            Assert.Equal("configured-test-key", handler.Requests[0].Headers.Authorization?.Parameter);
            Assert.Single(item.GetImageCandidates());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
        }
    }

    [Fact]
    public async Task SourceImagesForItemAsync_HttpClientTimeoutDoesNotOverrideConfiguredGenerationTimeout()
    {
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        try
        {
            var item = CreateSlideItem();
            item.Id = 45;
            item.SlideDeckId = 7;
            var planner = new FixedPlanner(new SlideImagePlan
            {
                NeedsImage = true,
                VisualRole = "process",
                GenerationPrompt = "Create a 16:9 academic presentation slide illustration showing a process workflow layout with connected cards and a central diagram.",
                NegativePrompt = "No text.",
                AltText = "Process workflow.",
                StatusHint = "queued"
            });
            var handler = new CapturingHttpHandler { ResponseDelay = TimeSpan.FromMilliseconds(50) };
            var settings = CreateImagePipelineSettings();
            settings.Generation.TimeoutSeconds = 1;
            var service = CreateImageService(
                planner,
                handler,
                settings,
                clientTimeout: TimeSpan.FromMilliseconds(1));

            await service.SourceImagesForItemAsync(item);

            Assert.Single(item.GetImageCandidates());
            Assert.Equal("ready", item.GetImagePlan()!.StatusHint);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
        }
    }

    private static SlideImagePlannerService CreatePlanner(IOllamaService ollama)
        => new(
            ollama,
            Options.Create(new ImagePipelineSettings()),
            NullLogger<SlideImagePlannerService>.Instance);

    private static SlideImageService CreateImageService(
        ISlideImagePlannerService planner,
        CapturingHttpHandler handler,
        ImagePipelineSettings? settings = null,
        TimeSpan? clientTimeout = null)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"pbl5-slide-image-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        settings ??= CreateImagePipelineSettings();
        var httpClient = new HttpClient(handler);
        if (clientTimeout.HasValue)
        {
            httpClient.Timeout = clientTimeout.Value;
        }

        return new SlideImageService(
            httpClient,
            new NoopSlideDeckRepository(),
            planner,
            Options.Create(settings),
            new TestWebHostEnvironment(tempRoot),
            NullLogger<SlideImageService>.Instance);
    }

    private static ImagePipelineSettings CreateImagePipelineSettings()
        => new()
        {
            Enabled = true,
            AssetStorageRoot = "slide-assets",
            Generation = new ImageGenerationSettings
            {
                Provider = "openai",
                Model = "gpt-image-1",
                Size = "1024x1024",
                Quality = "low",
                TimeoutSeconds = 30
            },
            WebSources = new ImageWebSourceSettings
            {
                Enabled = true,
                AllowedDomains = new List<string> { "commons.wikimedia.org" }
            }
        };

    private static SlideItem CreateSlideItem(SlideItemType slideType = SlideItemType.Content, IEnumerable<string>? bodyBlocks = null)
    {
        var item = new SlideItem
        {
            Id = 1,
            SlideDeckId = 1,
            SlideIndex = 1,
            SlideType = slideType,
            Status = SlideItemStatus.Completed,
            Heading = "Learning pipeline",
            Goal = "Explain the workflow",
            KeyMessage = "Inputs become useful study outputs"
        };
        item.SetBodyBlocks((bodyBlocks ?? new[] { "Input", "Process", "Output" }).ToList());
        return item;
    }

    private sealed class QueueOllamaService : IOllamaService
    {
        private readonly Queue<string> _responses;

        public QueueOllamaService(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public int StructuredCallCount { get; private set; }

        public Task<string> GenerateResponseAsync(string prompt, string? systemPrompt = null, OllamaModelProfile profile = OllamaModelProfile.Generation)
            => Task.FromResult(_responses.Dequeue());

        public async Task<T?> GenerateStructuredResponseAsync<T>(string prompt, string? systemPrompt = null, OllamaModelProfile profile = OllamaModelProfile.Generation) where T : class
        {
            StructuredCallCount += 1;
            var response = await GenerateResponseAsync(prompt, systemPrompt, profile);
            return JsonSerializer.Deserialize<T>(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        public async Task<StructuredGenerationResult<T>> GenerateStructuredResponseWithMetadataAsync<T>(string prompt, string? systemPrompt = null, OllamaModelProfile profile = OllamaModelProfile.Generation) where T : class
        {
            var value = await GenerateStructuredResponseAsync<T>(prompt, systemPrompt, profile);
            return new StructuredGenerationResult<T>
            {
                Value = value,
                Model = "test-model",
                RawOutputValid = value != null,
                ErrorType = value == null ? AutoRepairJsonErrorType.SchemaMismatch : AutoRepairJsonErrorType.None,
                ErrorMessage = value == null ? "empty test response" : string.Empty,
                AutoRepairTriggered = false,
                RepairSuccess = false,
                FinalOutputValid = value != null,
                ElapsedMs = 0,
                RawOutputPreview = string.Empty,
                RepairedOutputPreview = string.Empty
            };
        }

        public Task<bool> IsAvailableAsync() => Task.FromResult(true);
    }

    private sealed class FixedPlanner : ISlideImagePlannerService
    {
        private readonly SlideImagePlan _plan;

        public FixedPlanner(SlideImagePlan plan)
        {
            _plan = plan;
        }

        public Task<SlideImagePlan> PlanAsync(SlideItem item, string? documentTopic = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_plan);
    }

    private sealed class CapturingHttpHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();
        public int RequestCount => Requests.Count;
        public TimeSpan ResponseDelay { get; set; } = TimeSpan.Zero;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            if (ResponseDelay > TimeSpan.Zero)
            {
                await Task.Delay(ResponseDelay, cancellationToken);
            }

            var payload = JsonSerializer.Serialize(new
            {
                data = new[]
                {
                    new { b64_json = Convert.ToBase64String(Encoding.UTF8.GetBytes("fake-png")) }
                }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class NoopSlideDeckRepository : ISlideDeckRepository
    {
        public Task<SlideDeck> ReplaceForDocumentAsync(SlideDeck deck, IEnumerable<SlideItem>? items = null) => Task.FromResult(deck);
        public Task<SlideDeck> ReplaceForFolderAsync(SlideDeck deck, IEnumerable<SlideItem>? items = null) => Task.FromResult(deck);
        public Task<SlideDeck?> GetByIdAsync(int id) => Task.FromResult<SlideDeck?>(null);
        public Task<SlideDeck?> GetLatestByDocumentIdAsync(int documentId) => Task.FromResult<SlideDeck?>(null);
        public Task<SlideDeck?> GetLatestByFolderIdAsync(int folderProjectId) => Task.FromResult<SlideDeck?>(null);
        public Task<bool> UpdateDeckAsync(SlideDeck deck) => Task.FromResult(true);
        public Task<bool> ReplaceItemsAsync(int deckId, IEnumerable<SlideItem> items) => Task.FromResult(true);
        public Task<SlideItem?> GetItemAsync(int deckId, int itemId) => Task.FromResult<SlideItem?>(null);
        public Task<bool> UpdateItemAsync(SlideItem item) => Task.FromResult(true);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            WebRootPath = contentRootPath;
            ContentRootFileProvider = new NullFileProvider();
            WebRootFileProvider = new NullFileProvider();
        }

        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
