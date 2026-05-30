namespace AutoNate.Web.Services.Content.Bindings;

// Discriminator strings stored in `document_bindings.kind` and shipped
// over the wire to the SPA. Each kind maps to exactly one
// IDocumentBindingResolver via DocumentBindingResolverRegistry.
public static class DocumentBindingKinds
{
    public const string RecordField = "record-field";
    public const string AqlTable = "aql-table";

    public static bool IsValid(string kind) =>
        kind == RecordField || kind == AqlTable;
}
