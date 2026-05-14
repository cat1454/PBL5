using ELearnGamePlatform.Core.Entities;
using ELearnGamePlatform.Core.Interfaces;
using ELearnGamePlatform.Services.AI;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class SlideGeneratorServiceDesignTests
{
    [Fact]
    public void BuildDebugOutlinePrompt_IncludesCompactDesignContract()
    {
        var prompt = SlideGeneratorService.DebugBuildOutlinePrompt(
            CreateProcessedContent(),
            new SlideDeckBrief { NarrativeGoal = "Teach the core idea clearly." },
            new List<SlideGeneratorService.DebugDocumentChunkInput>
            {
                new("C01", "Nguon", "Boi canh", "Bang chung cu the tu tai lieu.")
            },
            targetCount: 6);

        Assert.Contains("Slide design contract:", prompt);
        Assert.Contains("Each slide is one focused learning card", prompt);
        Assert.Contains("Every slide must stay grounded in SOURCE_TEXT", prompt);
        Assert.DoesNotContain("Reference Audit", prompt);
    }

    [Fact]
    public void DetermineSuggestedSlideStatus_FallbackIsNeedsReview()
    {
        var result = CreateContent(score: 92);
        result.UsedFallback = true;

        var status = SlideGeneratorService.DebugDetermineSuggestedSlideStatus(result, CreateEvidence());

        Assert.Equal(SlideItemStatus.NeedsReview, status);
    }

    [Fact]
    public void DetermineSuggestedSlideStatus_LowScoreIsNeedsReview()
    {
        var result = CreateContent(score: 79);

        var status = SlideGeneratorService.DebugDetermineSuggestedSlideStatus(result, CreateEvidence());

        Assert.Equal(SlideItemStatus.NeedsReview, status);
    }

    [Fact]
    public void ShouldRepairSlide_GenericOrDenseContentReturnsTrue()
    {
        var generic = CreateContent(score: 90);
        generic.Heading = "Tom tat noi dung chinh";
        generic.BodyBlocks = new List<string> { "Noi dung chinh cua tai lieu", "Nang cao hieu qua hoc tap" };

        var dense = CreateContent(score: 90);
        dense.BodyBlocks = new List<string>
        {
            "Day la mot body block qua dai gom nhieu y khac nhau, vua noi ve boi canh, vua noi ve nguyen nhan, vua noi ve he qua, vua noi ve bai hoc va khien slide kho doc tren canvas.",
            "Block thu hai tiep tuc qua dai voi nhieu menh de, nhieu dau phay, nhieu tang thong tin va khong con giong mot learning card ngan gon de xem truoc.",
            "Block thu ba them qua nhieu chi tiet khien slide tro thanh mot trang PowerPoint day chu thay vi mot thong diep day hoc ro rang.",
            "Block thu tu lam slide nang hon nua va vuot qua muc can thiet."
        };

        Assert.True(SlideGeneratorService.DebugShouldRepairSlide(generic));
        Assert.True(SlideGeneratorService.DebugShouldRepairSlide(dense));
    }

    [Fact]
    public void ApplyLocalVerifier_AddsDesignAwareIssues()
    {
        var result = CreateContent(score: null);
        result.Heading = "Chuong 1";
        result.BodyBlocks = new List<string>
        {
            "Noi dung chinh cua tai lieu.",
            "Thong tin nay rat quan trong.",
            "Can nam vung cac noi dung lien quan.",
            "Block thu tu lam slide qua day va thieu thong tin cu the."
        };

        SlideGeneratorService.DebugApplyLocalSlideVerifierMetadata(
            result,
            SlideItemType.Content,
            CreateEvidence(),
            usedFallback: false);

        Assert.Contains(result.VerifierIssues, issue => issue.Contains("Heading", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.VerifierIssues, issue => issue.Contains("one-card-one-message", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.VerifierScore < 100);
    }

    private static ProcessedContent CreateProcessedContent()
        => new()
        {
            Title = "Bai hoc",
            Language = "vi",
            DocumentType = "lesson",
            Summary = "Tom tat bai hoc.",
            MainTopics = new List<string> { "Chu de" },
            KeyPoints = new List<string> { "Y chinh" }
        };

    private static SlideContentResult CreateContent(int? score)
        => new()
        {
            Heading = "Bai hoc co thong diep ro rang",
            Goal = "explanation: Giai thich y chinh",
            KeyMessage = "Mot y chinh ro rang",
            BodyBlocks = new List<string>
            {
                "Chi tiet cu the tu bang chung.",
                "Y nghia cua chi tiet voi bai hoc."
            },
            EvidenceFromText = "Bang chung cu the tu tai lieu.",
            SpeakerNotes = "Giai thich ngan gon va chuyen tiep sang y tiep theo.",
            VerifierScore = score,
            SuggestedStatus = SlideItemStatus.Completed
        };

    private static List<SlideGeneratorService.DebugDocumentChunkInput> CreateEvidence()
        => new()
        {
            new("C01", "Nguon", "Boi canh", "Bang chung cu the tu tai lieu.")
        };
}
