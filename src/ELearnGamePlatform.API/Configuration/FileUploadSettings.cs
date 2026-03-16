namespace ELearnGamePlatform.API.Configuration;

public class FileUploadSettings
{
    public const string SectionName = "FileUpload";

    public int MaxFileSizeInMB { get; set; } = 50;
    public List<string> AllowedExtensions { get; set; } = new()
    {
        ".pdf",
        ".docx",
        ".png",
        ".jpg",
        ".jpeg"
    };

    public long MaxFileSizeInBytes => Math.Max(1, MaxFileSizeInMB) * 1024L * 1024L;

    public bool IsExtensionAllowed(string extension)
    {
        return AllowedExtensions.Any(allowedExtension =>
            string.Equals(allowedExtension, extension, StringComparison.OrdinalIgnoreCase));
    }
}
