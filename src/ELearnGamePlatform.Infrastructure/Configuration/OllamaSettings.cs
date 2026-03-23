namespace ELearnGamePlatform.Infrastructure.Configuration;

public class OllamaSettings
{
    public required string BaseUrl { get; set; }
    public required string Model { get; set; }
    public string? AnalysisModel { get; set; }
    public string? GenerationModel { get; set; }
    public string? VerificationModel { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
    public double Temperature { get; set; } = 0.7;
    public double? AnalysisTemperature { get; set; }
    public double? GenerationTemperature { get; set; }
    public double? VerificationTemperature { get; set; }
}
