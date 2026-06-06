namespace ELearnGamePlatform.Core.Models;

public sealed class ExternalDocumentParseResult
{
    public bool Success { get; set; }
    public string Provider { get; set; } = "docling";
    public string Markdown { get; set; } = string.Empty;
    public string PlainText { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public string? Error { get; set; }
    public long ElapsedMs { get; set; }
    public bool TimedOut { get; set; }
    public bool CommandMissing { get; set; }
}
