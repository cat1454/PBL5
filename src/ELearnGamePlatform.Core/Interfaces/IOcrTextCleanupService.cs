namespace ELearnGamePlatform.Core.Interfaces;

public interface IOcrTextCleanupService
{
    Task<string?> CleanupAsync(string rawText, CancellationToken cancellationToken = default);
}
