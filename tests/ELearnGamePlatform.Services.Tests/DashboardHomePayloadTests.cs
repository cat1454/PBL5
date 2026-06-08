using System.Security.Claims;
using ELearnGamePlatform.API.Controllers;
using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Extensions;
using ELearnGamePlatform.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class DashboardHomePayloadTests
{
    [Fact]
    public async Task BuildDashboardHomePayloadAsync_ReturnsEmptyWorkspaceStats()
    {
        var service = CreatePayloadService();
        var workspace = CreateWorkspace();

        var payload = await service.BuildDashboardHomePayloadAsync(workspace, Array.Empty<Document>());

        Assert.Equal(workspace.Id, payload.Workspace.Id);
        Assert.Empty(payload.Sources);
        Assert.Equal(0, payload.Stats.SourceCount);
        Assert.False(payload.Stats.HasDeck);
    }

    [Fact]
    public async Task BuildDashboardHomePayloadAsync_CountsProcessingCompletedAndFailedSources()
    {
        var service = CreatePayloadService();
        var workspace = CreateWorkspace();
        var completed = CreateDocument(10, DocumentStatus.Completed, questions: 2);
        var processing = CreateDocument(11, DocumentStatus.Analyzing);
        var failed = CreateDocument(12, DocumentStatus.Failed);

        var payload = await service.BuildDashboardHomePayloadAsync(workspace, new[] { completed, processing, failed });

        Assert.Equal(3, payload.Stats.SourceCount);
        Assert.Equal(1, payload.Stats.CompletedSourceCount);
        Assert.Equal(1, payload.Stats.StudyReadySourceCount);
        Assert.Equal(1, payload.Stats.ProcessingSourceCount);
        Assert.Equal(1, payload.Stats.FailedSourceCount);
        Assert.True(payload.Sources.Single(source => source.Id == completed.Id).StudyReady);
        Assert.NotNull(payload.Sources.Single(source => source.Id == completed.Id).GenerationReadiness);
    }

    [Fact]
    public async Task BuildDashboardHomePayloadAsync_MarksLatestDeckStaleWhenSourceChangedAfterDeck()
    {
        var service = CreatePayloadService();
        var now = DateTime.UtcNow;
        var workspace = CreateWorkspace();
        workspace.SlideDecks.Add(new SlideDeck
        {
            Id = 30,
            FolderProjectId = workspace.Id,
            Status = SlideDeckStatus.Completed,
            Title = "Deck",
            CreatedAt = now.AddHours(-3),
            UpdatedAt = now.AddHours(-2),
            CompletedAt = now.AddHours(-2),
            Items = { new SlideItem { Id = 1, SlideDeckId = 30, SlideIndex = 1 } }
        });
        var source = CreateDocument(20, DocumentStatus.Completed);
        source.UpdatedAt = now.AddMinutes(-10);
        workspace.Documents.Add(source);

        var payload = await service.BuildDashboardHomePayloadAsync(workspace, new[] { source });

        Assert.True(payload.Stats.HasDeck);
        Assert.True(payload.Stats.DeckReady);
        Assert.True(payload.Stats.DeckStale);
        Assert.True(payload.Workspace.LatestDeck?.IsStale);
    }

    [Fact]
    public async Task DashboardController_GetHome_ReturnsAuthenticatedHomePayload()
    {
        var workspace = CreateWorkspace();
        var document = CreateDocument(10, DocumentStatus.Completed, questions: 1);
        var workspaceService = new FakeWorkspaceService(workspace);
        var documentRepository = new FakeDocumentRepository(new[] { document });
        var payloadService = CreatePayloadService(workspaceService);
        var controller = new DashboardController(documentRepository, payloadService, workspaceService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "5")
                    }, "Test"))
                }
            }
        };

        var result = await controller.GetHome();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<ELearnGamePlatform.API.Contracts.DashboardHomePayload>(ok.Value);
        Assert.Equal(workspace.Id, payload.Workspace.Id);
        Assert.Single(payload.Sources);
        Assert.True(workspaceService.EnsureCalled);
        Assert.True(workspaceService.AttachCalled);
    }

    private static WorkspacePayloadService CreatePayloadService(IWorkspaceService? workspaceService = null)
        => new(
            new DocumentProcessingJobStore(),
            new FakeReadinessService(),
            workspaceService ?? new FakeWorkspaceService(CreateWorkspace()));

    private static FolderProject CreateWorkspace()
        => new()
        {
            Id = 7,
            Name = WorkspaceService.DefaultWorkspaceLabel,
            Description = "Default",
            UploadedBy = "5",
            CreatedAt = DateTime.UtcNow.AddDays(-3),
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        };

    private static Document CreateDocument(int id, DocumentStatus status, int questions = 0)
    {
        var document = new Document
        {
            Id = id,
            FileName = $"source-{id}.pdf",
            FileType = "PDF",
            FilePath = $"source-{id}.pdf",
            FileSize = 1024,
            FolderProjectId = 7,
            UploadedBy = "5",
            Status = status,
            CreatedAt = DateTime.UtcNow.AddHours(-4),
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
            Summary = "Summary",
        };
        document.SetProcessingMetadata(new DocumentProcessingMetadata
        {
            Structure =
            {
                new DocumentSectionDescriptor
                {
                    SectionKey = "s1",
                    Heading = "Section 1",
                }
            }
        });

        for (var index = 0; index < questions; index++)
        {
            document.Questions.Add(new Question
            {
                Id = 100 + index,
                DocumentId = document.Id,
                QuestionText = "Question?"
            });
        }

        return document;
    }

    private sealed class FakeReadinessService : IDocumentGenerationReadinessService
    {
        public Task<DocumentGenerationReadiness> GetReadinessAsync(Document document, bool confirmed = false)
            => Task.FromResult(new DocumentGenerationReadiness
            {
                DocumentId = document.Id,
                Status = DocumentGenerationReadinessStatuses.Good,
                Action = DocumentGenerationReadinessActions.Allow,
                Confidence = 0.9,
                NeedsReview = false
            });

        public DocumentGenerationReadiness GetAggregateReadiness(IEnumerable<DocumentGenerationReadiness> readinessResults, bool confirmed = false)
            => readinessResults.FirstOrDefault() ?? new DocumentGenerationReadiness();
    }

    private sealed class FakeWorkspaceService : IWorkspaceService
    {
        private readonly FolderProject _workspace;

        public FakeWorkspaceService(FolderProject workspace)
        {
            _workspace = workspace;
        }

        public string DefaultWorkspaceName => WorkspaceService.DefaultWorkspaceLabel;
        public bool EnsureCalled { get; private set; }
        public bool AttachCalled { get; private set; }

        public Task<FolderProject> EnsureDefaultWorkspaceAsync(string userId)
        {
            EnsureCalled = true;
            return Task.FromResult(_workspace);
        }

        public Task<IReadOnlyCollection<Document>> AttachOrphanDocumentsAsync(string userId, int workspaceId)
        {
            AttachCalled = true;
            return Task.FromResult<IReadOnlyCollection<Document>>(Array.Empty<Document>());
        }
    }

    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        private readonly IReadOnlyCollection<Document> _documents;

        public FakeDocumentRepository(IReadOnlyCollection<Document> documents)
        {
            _documents = documents;
        }

        public Task<Document> CreateAsync(Document document) => Task.FromResult(document);
        public Task<Document?> GetByIdAsync(int id) => Task.FromResult(_documents.FirstOrDefault(document => document.Id == id));
        public Task<IEnumerable<Document>> GetAllAsync() => Task.FromResult<IEnumerable<Document>>(_documents);
        public Task<IEnumerable<Document>> GetByUserAsync(string userId) => Task.FromResult<IEnumerable<Document>>(Array.Empty<Document>());
        public Task<IEnumerable<Document>> GetByFolderProjectIdAsync(int folderProjectId) => Task.FromResult<IEnumerable<Document>>(_documents);
        public Task<int> GetNextFolderSourceOrderAsync(int folderProjectId) => Task.FromResult(1);
        public Task<bool> UpdateAsync(int id, Document document) => Task.FromResult(true);
        public Task<bool> DeleteAsync(int id) => Task.FromResult(true);
    }
}
