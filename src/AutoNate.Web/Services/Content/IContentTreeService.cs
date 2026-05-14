using AutoNate.Web.Persistence;

namespace AutoNate.Web.Services.Content;

// Maintains the content_ancestors closure used by IContentAuthorizer for
// inheritance-aware permission checks. Every mutation that creates a content
// entity, deletes one, or changes its parent must call into this service from
// inside the same transaction so the closure never drifts from the entity
// tables.
public interface IContentTreeService
{
    // Writes the depth-0 self-row and the full ancestor chain for a newly
    // created entity. For a project the chain is just the self-row; for a
    // cabinet/notebook/page it walks up to the root via the FK in db. The
    // entity (and its parents) must already exist in the relevant table.
    Task InsertSelfWithAncestorsAsync(AutoNateDbContext db, string kind, Guid id, CancellationToken ct);

    // Recomputes content_ancestors rows for the entity AND all of its
    // descendants. Called after a move (parent changes) so descendants pick
    // up the new ancestor chain. Implementation deletes existing rows where
    // descendant_id is in the subtree, then re-inserts from the current
    // entity-table state.
    Task RebuildAncestorsForSubtreeAsync(AutoNateDbContext db, string kind, Guid rootId, CancellationToken ct);

    // Deletes all closure rows that reference the entity as descendant or
    // ancestor. Called when the entity itself is hard-deleted. Descendants
    // are expected to have been cascade-deleted via the entity-table FKs
    // before this is invoked, so their own closure rows are gone too.
    Task DeleteEntityAsync(AutoNateDbContext db, string kind, Guid id, CancellationToken ct);
}
