namespace ELearnGamePlatform.Core.Options;

public class DocumentParsingSettings
{
    public const string SectionName = "DocumentParsing";

    public bool Enabled { get; set; } = false;
    public string Provider { get; set; } = "docling";
    public string DoclingCommand { get; set; } = "docling";
    public int TimeoutSeconds { get; set; } = 180;
    public int MinMarkdownLength { get; set; } = 500;
    public bool FallbackToLegacy { get; set; } = true;
    public bool PreferMarkdownForGeneration { get; set; } = true;
    public string OutputDirectory { get; set; } = "uploads/parsed";
}
