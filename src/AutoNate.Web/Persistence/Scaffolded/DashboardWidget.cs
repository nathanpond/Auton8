using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class DashboardWidget
{
    public Guid Id { get; set; }

    public Guid DashboardId { get; set; }

    // Registry key — matches WidgetDefinition.type in the SPA registry, e.g.
    // 'data-table', 'mantine-chart'. Validation is client-side because the
    // backend has no notion of which widget types exist; row stays as-is if
    // the SPA can't find a definition (renders as "Unknown widget").
    public string WidgetType { get; set; } = null!;

    public string? Title { get; set; }

    // Widget-specific config blob. Shape is owned by the widget's Zod schema
    // in the SPA. Backend stores it opaquely.
    public string ConfigJsonb { get; set; } = "{}";

    // Mirrors of react-grid-layout's per-item x/y/w/h. Storing them as columns
    // (rather than on a dashboard-level layout JSON) makes a single-widget
    // drag/resize a one-row UPDATE instead of rewriting the whole layout
    // array.
    public int GridX { get; set; }

    public int GridY { get; set; }

    public int GridW { get; set; }

    public int GridH { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
