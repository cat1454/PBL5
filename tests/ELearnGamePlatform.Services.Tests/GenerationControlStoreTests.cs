using ELearnGamePlatform.API.Services;
using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Infrastructure.Data;
using ELearnGamePlatform.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class GenerationControlStoreTests
{
    [Fact]
    public async Task SlideJob_CanPauseResumeAndCancel()
    {
        var store = new SlideGenerationJobStore();
        var jobId = store.CreateFolderJob(42, 10, "user-1");

        Assert.True(store.PauseJob(jobId));
        Assert.True(store.TryGetLatestActiveJobForFolder(42, out var paused));
        Assert.Equal("paused", paused?.Status);

        var waitTask = store.WaitForExecutionAsync(jobId);
        Assert.False(waitTask.IsCompleted);

        Assert.True(store.ResumeJob(jobId));
        Assert.True(await waitTask);
        Assert.True(store.TryGetJob(jobId, out var resumed));
        Assert.Equal("running", resumed?.Status);

        Assert.True(store.CancelJob(jobId));
        Assert.False(await store.WaitForExecutionAsync(jobId));
        Assert.True(store.TryGetJob(jobId, out var cancelled));
        Assert.Equal("cancelled", cancelled?.Status);
        Assert.False(store.ResumeJob(jobId));
    }

    [Fact]
    public void SlideJob_TerminalStateCannotBePaused()
    {
        var store = new SlideGenerationJobStore();
        var jobId = store.CreateJob(7, 8, "user-1");
        store.UpdateJob(jobId, state => state.Status = "completed");

        Assert.False(store.PauseJob(jobId));
        Assert.False(store.CancelJob(jobId));
    }

    [Fact]
    public void SlideJob_CompletionSealRejectsLateControlsAndSupersede()
    {
        var store = new SlideGenerationJobStore();
        var jobId = store.CreateFolderJob(42, 10, "user-1");
        store.UpdateJob(jobId, state => state.Status = "running");

        Assert.True(store.TrySealCompletion(jobId));
        Assert.False(store.PauseJob(jobId));
        Assert.False(store.CancelJob(jobId));

        var newerJobId = store.CreateFolderJob(42, 10, "user-1");

        Assert.True(store.TryGetJob(jobId, out var sealedJob));
        Assert.Equal("running", sealedJob?.Status);
        Assert.True(sealedJob?.IsCompletionSealed == true);
        Assert.False(sealedJob?.IsSuperseded == true);
        Assert.True(store.TryGetLatestJobForFolder(42, out var latestJob));
        Assert.Equal(newerJobId, latestJob?.JobId);

        store.UpdateJob(jobId, state => state.Status = "completed");

        Assert.True(store.TryGetJob(jobId, out var completedJob));
        Assert.Equal("completed", completedJob?.Status);
    }

    [Fact]
    public async Task QuestionStudioRunControl_PauseResumeAndCancelReleaseWaiters()
    {
        var store = new QuestionStudioRunControlStore();
        store.RegisterRun(15);

        Assert.True(store.PauseRun(15));
        var waitTask = store.WaitForExecutionAsync(15);
        Assert.False(waitTask.IsCompleted);

        Assert.True(store.ResumeRun(15));
        Assert.True(await waitTask);

        Assert.True(store.PauseRun(15));
        var cancelledWaitTask = store.WaitForExecutionAsync(15);
        Assert.True(store.CancelRun(15));
        Assert.False(await cancelledWaitTask);
        Assert.False(store.ResumeRun(15));
    }

    [Fact]
    public void QuestionStudioRunControl_CompletedRunRejectsFurtherTransitions()
    {
        var store = new QuestionStudioRunControlStore();
        store.RegisterRun(21);
        store.CompleteRun(21);

        Assert.False(store.PauseRun(21));
        Assert.False(store.ResumeRun(21));
        Assert.False(store.CancelRun(21));
    }

    [Fact]
    public void QuestionStudioRunControl_SealedRunRejectsLateControls()
    {
        var store = new QuestionStudioRunControlStore();
        store.RegisterRun(22);

        Assert.True(store.SealRun(22));
        Assert.False(store.PauseRun(22));
        Assert.False(store.ResumeRun(22));
        Assert.False(store.CancelRun(22));
    }

    [Fact]
    public async Task DeleteDeck_RemovesDeckAndItems()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var context = new ApplicationDbContext(options);
        var deck = new SlideDeck
        {
            FolderProjectId = 4,
            Title = "Generated deck",
            Items = new List<SlideItem>
            {
                new() { SlideIndex = 1, Heading = "First" }
            }
        };
        context.SlideDecks.Add(deck);
        await context.SaveChangesAsync();
        var repository = new SlideDeckRepository(context);

        Assert.True(await repository.DeleteDeckAsync(deck.Id));
        Assert.False(await context.SlideDecks.AnyAsync());
        Assert.False(await context.SlideItems.AnyAsync());
    }

    [Fact]
    public async Task DeleteQuestionBank_ArchivesOnlyActiveQuestions()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.Questions.AddRange(
            new Question
            {
                DocumentId = 8,
                QuestionText = "Active",
                QuestionType = QuestionType.ShortAnswer
            },
            new Question
            {
                DocumentId = 8,
                QuestionText = "Already archived",
                QuestionType = QuestionType.ShortAnswer,
                IsArchived = true
            });
        await context.SaveChangesAsync();
        var repository = new QuestionRepository(context);

        Assert.True(await repository.DeleteByDocumentIdAsync(8));

        var questions = await context.Questions.OrderBy(question => question.Id).ToListAsync();
        Assert.All(questions, question => Assert.True(question.IsArchived));
        Assert.Equal(2, questions.Count);
    }
}
