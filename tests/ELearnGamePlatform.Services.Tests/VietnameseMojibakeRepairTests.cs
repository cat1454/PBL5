using ELearnGamePlatform.Services.DocumentProcessing;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class VietnameseMojibakeRepairTests
{
    [Fact]
    public void TryRepair_RepairsReadableVietnamese()
    {
        const string mojibake =
            "D\u00C3\u00B9ng \u00C4\u2018\u00E1\u00BB\u0192 ki\u00E1\u00BB\u0192m " +
            "th\u00E1\u00BB\u00AD Docling, sinh c\u00C3\u00A2u h\u00E1\u00BB\u008Fi";

        var repaired = VietnameseMojibakeRepair.TryRepair(mojibake);

        Assert.Equal(
            "D\u00F9ng \u0111\u1EC3 ki\u1EC3m th\u1EED Docling, sinh c\u00E2u h\u1ECFi",
            repaired);
        Assert.True(VietnameseMojibakeRepair.IsRepairSuccessful(mojibake, repaired));
    }

    [Fact]
    public void TryRepair_LeavesCleanVietnameseMarkdownUnchanged()
    {
        const string markdown = "# H\u1EC7 th\u1ED1ng\n\n- D\u00F9ng Docling \u0111\u1EC3 sinh c\u00E2u h\u1ECFi.";

        Assert.False(VietnameseMojibakeRepair.IsLikelyMojibake(markdown));
        Assert.Equal(markdown, VietnameseMojibakeRepair.TryRepair(markdown));
    }

    [Fact]
    public void TryRepair_LeavesCleanEnglishMarkdownUnchanged()
    {
        const string markdown = "# System\n\n- Use Docling to generate questions.";

        Assert.False(VietnameseMojibakeRepair.IsLikelyMojibake(markdown));
        Assert.Equal(markdown, VietnameseMojibakeRepair.TryRepair(markdown));
    }

    [Fact]
    public void TryRepair_PreservesMarkdownHeadingsTablesAndLineBreaks()
    {
        const string mojibake =
            "# H\u00E1\u00BB\u2021 th\u00E1\u00BB\u2018ng\n\n" +
            "| T\u00C3\u00AAn | Gi\u00C3\u00A1 tr\u00E1\u00BB\u2039 |\n" +
            "| --- | --- |\n" +
            "| C\u00C3\u00A2u h\u00E1\u00BB\u008Fi | 10 |\n\n" +
            "\u00EF\u201A\u00B7 M\u00E1\u00BB\u00A5c m\u00E1\u00BB\u2122t";

        var repaired = VietnameseMojibakeRepair.TryRepair(mojibake);

        Assert.StartsWith("# H\u1EC7 th\u1ED1ng\n\n", repaired);
        Assert.Contains("| T\u00EAn | Gi\u00E1 tr\u1ECB |\n| --- | --- |", repaired);
        Assert.Contains("| C\u00E2u h\u1ECFi | 10 |", repaired);
        Assert.EndsWith("- M\u1EE5c m\u1ED9t", repaired);
    }
}
