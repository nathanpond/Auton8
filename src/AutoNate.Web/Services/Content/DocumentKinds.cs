namespace AutoNate.Web.Services.Content;

// Discriminator values for the `documents.kind` column. A document and a
// template share the same EF entity + EntityKind; this class spells the
// strings so endpoint handlers, services, and tests don't sprinkle
// magic-string literals.
public static class DocumentKinds
{
    public const string Document = "document";
    public const string Template = "template";

    public static bool IsValid(string kind) =>
        kind == Document || kind == Template;
}
