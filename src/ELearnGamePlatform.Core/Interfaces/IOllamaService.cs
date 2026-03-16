namespace ELearnGamePlatform.Core.Interfaces;

public interface IOllamaService
{
    Task<string> GenerateResponseAsync(string prompt, string? systemPrompt = null);
    Task<T?> GenerateStructuredResponseAsync<T>(string prompt, string? systemPrompt = null) where T : class;
    Task<bool> IsAvailableAsync();
}
