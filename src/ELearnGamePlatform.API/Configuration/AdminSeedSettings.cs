namespace ELearnGamePlatform.API.Configuration;

public class AdminSeedSettings
{
    public const string SectionName = "AdminSeed";

    public bool Enabled { get; set; } = true;
    public string FullName { get; set; } = "System Admin";
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
