namespace AutoNate.Web.Services.Content;

// Lowercase string constants matching EntityKinds.* — used as discriminator
// values in content_ancestors, permission_grants selectors, and audit events.
// Kept here so service-layer code doesn't need to import the authorization
// namespace just to spell a kind name.
public static class ContentKinds
{
    public const string Project = "project";
    public const string Cabinet = "cabinet";
    public const string Notebook = "notebook";
    public const string Page = "page";

    // Documents subsystem — Project → Folder (self-nesting) → Document.
    // Same authorizer / closure-row plumbing as the notes hierarchy.
    public const string Folder = "folder";
    public const string Document = "document";

    public static bool IsContentKind(string kind) =>
        kind == Project || kind == Cabinet || kind == Notebook || kind == Page
        || kind == Folder || kind == Document;
}
