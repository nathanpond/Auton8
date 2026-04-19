namespace AutoNate.Web.Configuration;

public sealed class DevelopmentAutoLoginOptions
{
    public const string SectionName = "DevelopmentAutoLogin";

    public bool Enabled { get; set; }

    public string Username { get; set; } = string.Empty;
}
