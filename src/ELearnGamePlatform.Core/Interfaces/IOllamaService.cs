namespace ELearnGamePlatform.Core.Interfaces;

public interface IOllamaService
{
    Task<string> GenerateResponseAsync(
        string prompt,
        string? systemPrompt = null,
        OllamaModelProfile profile = OllamaModelProfile.Generation);

    Task<T?> GenerateStructuredResponseAsync<T>(
        string prompt,
        string? systemPrompt = null,
        OllamaModelProfile profile = OllamaModelProfile.Generation) where T : class;

    Task<bool> IsAvailableAsync();
}

public enum OllamaModelProfile
{
    Analysis,
    Generation
}
