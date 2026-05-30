namespace AutoNate.Web.Services.Content;

// Bound from configuration section "DocumentImports". Holds the stash for
// uploaded .docx / .dotx files between the import POST and the editor's
// first autosave. Once the editor parses the bytes via docx-editor and
// the first Hocuspocus snapshot lands in `documents.body_jsonb`, the
// stash is discarded — the JSON-in-Postgres mirror becomes the source
// of truth and the original .docx is no longer needed.
public sealed class DocumentImportOptions
{
    public string RootPath { get; set; } = "data/document-imports";

    // .docx/.dotx files in the wild are typically 50 KB - 5 MB. The 25 MB
    // ceiling matches ContentAttachmentOptions for one consistent ops
    // limit; operators can lift it via appsettings if a real document
    // (large embedded images) trips it.
    public long MaxBytes { get; set; } = 25L * 1024 * 1024;
}
