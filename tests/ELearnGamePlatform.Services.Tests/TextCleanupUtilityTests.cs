using ELearnGamePlatform.Core.Utilities;
using Xunit;

namespace ELearnGamePlatform.Services.Tests;

public class TextCleanupUtilityTests
{
    [Fact]
    public void NormalizeForDisplay_CollapsesRepeatedPunctuationSeparators()
    {
        var normalized = TextCleanupUtility.NormalizeForDisplay(
            "Văn hóa Ấn Độ có ba giai đoạn;; Giúp hiểu biết về văn minh Ấn Độ.");

        Assert.Equal(
            "Văn hóa Ấn Độ có ba giai đoạn; Giúp hiểu biết về văn minh Ấn Độ.",
            normalized);
    }

    [Fact]
    public void NormalizeForAi_PreservesMarkdownStructure()
    {
        var input = """
        # Main heading
        explanatory paragraph continues here
        ## Details
        | Name | Value |
        | --- | --- |
        | Alpha | 10 |
        - first item
        * second item
        1. numbered item
        """;

        var normalized = TextCleanupUtility.NormalizeForAi(input, preserveLineBreaks: true);
        var lines = normalized.Split(Environment.NewLine);

        Assert.Contains("# Main heading", lines);
        Assert.Contains("## Details", lines);
        Assert.Contains("| Name | Value |", lines);
        Assert.Contains("| --- | --- |", lines);
        Assert.Contains("| Alpha | 10 |", lines);
        Assert.Contains("- first item", lines);
        Assert.Contains("* second item", lines);
        Assert.Contains("1. numbered item", lines);
        Assert.DoesNotContain("# Main heading explanatory paragraph", normalized);
    }
}
