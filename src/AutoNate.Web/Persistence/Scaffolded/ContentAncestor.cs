using System;

namespace AutoNate.Web.Persistence.Scaffolded;

// Materialised ancestor closure across the four permissionable content kinds
// (project, cabinet, notebook, page). Includes a depth-0 self-row per entity
// so authorization joins handle "this row" and "any ancestor of this row"
// with one expression. Maintained transactionally by ContentTreeService.
public partial class ContentAncestor
{
    public string DescendantKind { get; set; } = null!;

    public Guid DescendantId { get; set; }

    public string AncestorKind { get; set; } = null!;

    public Guid AncestorId { get; set; }

    public int Depth { get; set; }
}
