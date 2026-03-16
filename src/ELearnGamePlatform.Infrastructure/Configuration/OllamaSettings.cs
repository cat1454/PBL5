namespace ELearnGamePlatform.Infrastructure.Configuration;

public class OllamaSettings
{
    public required string BaseUrl { get; set; }
    public required string Model { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
    public double Temperature { get; set; } = 0.7;
}
