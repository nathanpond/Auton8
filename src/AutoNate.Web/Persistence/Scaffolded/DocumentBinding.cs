using System;

namespace AutoNate.Web.Persistence.Scaffolded;

// Live data binding embedded in a document body. The document carries
// only a placeholder marker (e.g. `{{binding:<id>}}`) in its text; the
// resolved value lives here in `LastResolvedValueJsonb`. The editor's
// in-document rendering plugin reads from this table (via the REST
// hooks) to paint the value over the placeholder.
//
// Refresh model (per the plan §1 + the user's plan-mode answer):
// snapshot-on-open with explicit per-binding refresh + a global
// "Refresh all bindings" header action. No background polling. Yjs
// collaborators see the same render because the resolved value lives
// outside the Y.Doc — every client reads the same row.
public partial class DocumentBinding
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    // Discriminator. v1 ships "record-field" and "aql-table". Future
    // kinds (e.g. "chart", "workflow-data") add a new
    // IDocumentBindingResolver implementation and a new constant in
    // DocumentBindingKinds.
    public string Kind { get; set; } = null!;

    // Resolver input. Shape depends on Kind:
    //   record-field → { recordId: guid, fieldKey: string }
    //   aql-table    → { queryText: string, limit?: int }
    // Stored as raw JSON so the resolver can deserialize to its own
    // strongly-typed input record without an EF discriminator dance.
    public string ConfigJsonb { get; set; } = null!;

    // Snapshot of the most recent resolved value. Shape depends on
    // Kind (see resolver implementations). Null until first resolve.
    public string? LastResolvedValueJsonb { get; set; }

    // Null until first resolve; set on every successful refresh.
    public DateTime? LastResolvedAtUtc { get; set; }

    // The user whose permissions were used to compute LastResolvedValueJsonb.
    // Important for audit: a record-field binding can show different values
    // depending on who refreshed it (per-row authorization filters out
    // rows the user can't see). Surface in the UI so reviewers know whose
    // view they're looking at.
    public Guid? LastResolvedByUserId { get; set; }

    // Optional human-readable label shown in the side panel + the in-doc
    // widget hover. Falls back to a kind-specific summary if null
    // (e.g. "Field: customer.name" for record-field).
    public string? Label { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid UpdatedBy { get; set; }
}
