namespace AutoNate.Web.Endpoints;

// Centralizes the "this content field is Yjs-managed, REST may not write it"
// rule. Page bodies are unconditionally Yjs-managed. All three note kinds
// are Yjs-managed: `richtext` (BlockNote) shipped in Phase 1; `drawing`
// (Excalidraw / Napkin) and `diagram` (draw.io) joined in Phase 4.
//
// On a forbidden write the route handler returns a loud 409 Conflict rather
// than silently dropping the field — silent ignores lead to "I saved my
// edit and it didn't stick" bug reports.
internal static class YjsManagedContentGuard
{
    public const string ConflictMessage =
        "Content is managed by the Yjs collaboration session for this document. "
        + "Connect via Hocuspocus to edit body content; REST PATCH may only "
        + "modify metadata (title, sort order, archive flag, etc.).";

    public static bool IsYjsManagedNoteKind(string noteKind) =>
        string.Equals(noteKind, "richtext", StringComparison.Ordinal)
        || string.Equals(noteKind, "drawing", StringComparison.Ordinal)
        || string.Equals(noteKind, "diagram", StringComparison.Ordinal);

    public static IResult? RejectPageBodyWrite(string? incomingBodyJsonb) =>
        incomingBodyJsonb is null
            ? null
            : Results.Conflict(new { error = ConflictMessage, field = "bodyJsonb" });

    public static IResult? RejectYjsManagedNoteContentWrite(
        string noteKind, string? incomingContentJsonb) =>
        incomingContentJsonb is null || !IsYjsManagedNoteKind(noteKind)
            ? null
            : Results.Conflict(new { error = ConflictMessage, field = "contentJsonb" });
}
