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
}
