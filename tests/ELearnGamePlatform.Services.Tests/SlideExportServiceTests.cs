using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Services.Slides;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class SlideExportServiceTests
{
    [Fact]
    public void RenderPrintHtml_UsesEditorStateCoordinatesAndSelectedPdfRegionImage()
    {
        var item = new SlideItem
        {
            Id = 21,
            SlideDeckId = 3,
            SlideIndex = 1,
            SlideType = SlideItemType.Content,
            Heading = "Fallback heading"
        };
        item.SetImageCandidates(new List<SlideImageCandidate>
        {
            new()
            {
                Key = "pdf-region-21-1-1",
                SourceType = "pdf-region",
                LocalAssetUrl = "/uploads/slide-assets/deck-3/slide-1/pdf-region-1-1.png",
                AltText = "PDF figure",
                IsSelected = true
            }
        });
        item.SelectedImageKey = "pdf-region-21-1-1";
        item.SetEditorState(new SlideEditorState
        {
            Canvas = new SlideCanvasState { Width = 1280, Height = 720 },
            Elements = new List<SlideElementState>
            {
                new()
                {
                    Id = "placeholder",
                    Type = "text",
                    Role = "placeholder",
                    Text = "Empty text",
                    Width = 120,
                    Height = 80,
                    ZIndex = 1
                },
                new()
                {
                    Id = "title",
                    Type = "text",
                    Role = "title",
                    Text = "Canvas title",
                    X = 128,
                    Y = 72,
                    Width = 512,
                    Height = 120,
                    FontSize = 48,
                    Bold = true,
                    Color = "#ffffff",
                    ZIndex = 2
                },
                new()
                {
                    Id = "image",
                    Type = "image",
                    Role = "image",
                    X = 640,
                    Y = 180,
                    Width = 320,
                    Height = 240,
                    ZIndex = 3
                }
            }
        });
        var service = new SlideExportService(new FakeSlideGenerator());

        var html = service.RenderPrintHtml(new SlideDeck { Title = "Deck" }, new[] { item });

        Assert.Contains("left:10%", html);
        Assert.Contains("top:10%", html);
        Assert.Contains("Canvas title", html);
        Assert.Contains("/uploads/slide-assets/deck-3/slide-1/pdf-region-1-1.png", html);
        Assert.DoesNotContain("Empty text", html);
        Assert.DoesNotContain("slide-meta", html);
        Assert.DoesNotContain("speaker-notes", html);
    }

    private sealed class FakeSlideGenerator : ISlideGenerator
    {
        public Task<SlideOutlineResult> GenerateOutlineAsync(
            string content,
            ProcessedContent? processedContent,
            SlideDeckBrief? brief,
            int desiredSlideCount,
            IProgress<SlideGenerationProgressUpdate>? progress = null,
            int? documentId = null,
            string? correlationId = null,
            string? speedMode = null)
            => Task.FromResult(new SlideOutlineResult());

        public Task<SlideContentResult> GenerateSlideAsync(
            string content,
            ProcessedContent? processedContent,
            SlideDeckBrief? brief,
            SlideOutlineSlide outlineSlide,
            int slideNumber,
            int totalSlides,
            IProgress<SlideGenerationProgressUpdate>? progress = null,
            int? documentId = null,
            string? correlationId = null,
            string? speedMode = null)
            => Task.FromResult(new SlideContentResult());

        public string RenderDeckHtml(SlideDeck deck, IReadOnlyList<SlideItem> items) => string.Empty;
    }
}
