using System.Text.Json;
using ELearnGamePlatform.API.Configuration;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class SlidePdfImageAssetServiceTests
{
    [Fact]
    public async Task TryCreateCandidateAsync_ValidPdfRegion_CropsAssetIntoSlideAssets()
    {
        var root = CreateTempRoot();
        var sourcePdf = Path.Combine(root, "source.pdf");
        await File.WriteAllBytesAsync(sourcePdf, new byte[] { 1, 2, 3 });
        var renderedPage = Path.Combine(root, "page.png");
        await SaveTestImageAsync(renderedPage, 200, 100);
        var item = CreateSlideItem(sourcePdf);
        var service = CreateService(
            root,
            renderedPage,
            new[]
            {
                new DocumentRegion
                {
                    PageNumber = 1,
                    RegionType = "FigureCandidate",
                    Text = "Source figure",
                    NormalizedX = 0.25,
                    NormalizedY = 0.2,
                    NormalizedWidth = 0.5,
                    NormalizedHeight = 0.4,
                    VisionConfidence = 0.92
                }
            });

        var candidate = await service.TryCreateCandidateAsync(item, new SlideImagePlan { NeedsImage = true, VisualRole = "figure" });

        Assert.NotNull(candidate);
        Assert.Equal("pdf-region", candidate!.SourceType);
        Assert.Equal("Source PDF", candidate.Provider);
        Assert.Equal(1, candidate.PageNumber);
        Assert.Equal("FigureCandidate", candidate.RegionType);
        var assetPath = Path.Combine(root, candidate.LocalAssetUrl!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(assetPath));
    }

    [Fact]
    public async Task TryCreateCandidateAsync_MissingBoundingBox_SkipsWithoutCandidate()
    {
        var root = CreateTempRoot();
        var sourcePdf = Path.Combine(root, "source.pdf");
        await File.WriteAllBytesAsync(sourcePdf, new byte[] { 1, 2, 3 });
        var renderedPage = Path.Combine(root, "page.png");
        await SaveTestImageAsync(renderedPage, 200, 100);
        var item = CreateSlideItem(sourcePdf);
        var service = CreateService(
            root,
            renderedPage,
            new[] { new DocumentRegion { PageNumber = 1, RegionType = "FigureCandidate", Text = "No bbox" } });

        var candidate = await service.TryCreateCandidateAsync(item, new SlideImagePlan { NeedsImage = true });

        Assert.Null(candidate);
    }

    private static SlidePdfImageAssetService CreateService(string root, string renderedPage, IEnumerable<DocumentRegion> regions)
    {
        var payload = JsonSerializer.Serialize(new { regions }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new SlidePdfImageAssetService(
            new FixedUnderstandingRunRepository(payload),
            new FixedVisionPageImageProvider(renderedPage),
            Options.Create(new ImagePipelineSettings { AssetStorageRoot = "uploads/slide-assets" }),
            new TestWebHostEnvironment(root),
            NullLogger<SlidePdfImageAssetService>.Instance);
    }

    private static SlideItem CreateSlideItem(string filePath)
        => new()
        {
            Id = 11,
            SlideDeckId = 7,
            SlideIndex = 2,
            SlideDeck = new SlideDeck
            {
                Id = 7,
                DocumentId = 5,
                Document = new ELearnGamePlatform.Core.Entities.Document
                {
                    Id = 5,
                    FileName = "source.pdf",
                    FileType = "pdf",
                    FilePath = filePath,
                    UploadedBy = "test"
                }
            }
        };

    private static async Task SaveTestImageAsync(string path, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(40, 90, 160));
        await image.SaveAsPngAsync(path);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pbl5-pdf-region-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FixedUnderstandingRunRepository : IDocumentUnderstandingRunRepository
    {
        private readonly string _resultJson;

        public FixedUnderstandingRunRepository(string resultJson)
        {
            _resultJson = resultJson;
        }

        public Task<DocumentUnderstandingRun> CreateAsync(DocumentUnderstandingRun run) => Task.FromResult(run);

        public Task<DocumentUnderstandingRun?> GetLatestByDocumentIdAsync(int documentId)
            => Task.FromResult<DocumentUnderstandingRun?>(new DocumentUnderstandingRun
            {
                DocumentId = documentId,
                Status = "Completed",
                ResultJson = _resultJson
            });
    }

    private sealed class FixedVisionPageImageProvider : IVisionPageImageProvider
    {
        private readonly string _imagePath;

        public FixedVisionPageImageProvider(string imagePath)
        {
            _imagePath = imagePath;
        }

        public Task<VisionPageImageSource?> GetPageImageAsync(
            string filePath,
            string fileType,
            int pageNumber,
            CancellationToken cancellationToken = default)
            => Task.FromResult<VisionPageImageSource?>(new VisionPageImageSource
            {
                ImagePath = _imagePath,
                PageNumber = pageNumber
            });
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
