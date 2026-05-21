namespace AutoNate.Web.Services.Content;

// Bound from configuration section "ContentAttachments". The default
// allowlist covers common image, PDF, and office formats — operators
// can tighten or expand it in appsettings.{Environment}.json. Note that
// upload also enforces a strict magic-byte sniff via
// ContentTypeSniffer regardless of this allowlist, so HTML / SVG / JS
// can't slip through even if an operator sets "*/*".
public sealed class ContentAttachmentOptions
{
    public string RootPath { get; set; } = "data/content-attachments";

    public long MaxBytes { get; set; } = 25L * 1024 * 1024; // 25 MB

    // Glob patterns ("image/*", "application/pdf", "*/*"). Empty list ==
    // accept anything. Evaluated case-insensitively.
    public List<string> AllowedContentTypes { get; set; } = new()
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "application/pdf",
        "application/zip",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.oasis.opendocument.text",
        "application/vnd.oasis.opendocument.spreadsheet",
        "application/vnd.oasis.opendocument.presentation",
        "application/msword",
        "application/vnd.ms-excel",
        "application/vnd.ms-powerpoint"
    };
}
