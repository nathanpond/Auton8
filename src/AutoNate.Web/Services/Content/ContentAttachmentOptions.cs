namespace AutoNate.Web.Services.Content;

// Bound from configuration section "ContentAttachments". Defaults are
// intentionally permissive — operators can tighten the allowlist in
// appsettings.{Environment}.json once they know what the team uploads.
public sealed class ContentAttachmentOptions
{
    public string RootPath { get; set; } = "data/content-attachments";

    public long MaxBytes { get; set; } = 25L * 1024 * 1024; // 25 MB

    // Glob patterns ("image/*", "application/pdf", "*/*"). Empty list ==
    // accept anything. Evaluated case-insensitively.
    public List<string> AllowedContentTypes { get; set; } = new() { "*/*" };
}
