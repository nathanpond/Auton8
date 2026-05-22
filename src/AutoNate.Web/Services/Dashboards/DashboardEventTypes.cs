namespace AutoNate.Web.Services.Dashboards;

// Topic + event-type names for the dashboards.events bus topic. Mutation
// events fire post-commit. Widget config payloads are never inlined in
// events — only widget id + widget_type + dashboard id are surfaced so a
// runaway widget config can't bloat the stream.
public static class DashboardEventTopic
{
    public const string TopicRoot = "dashboards";
    public const string TopicName = "dashboards.events";
}

public static class DashboardResourceKinds
{
    public const string Dashboard = "dashboard";
    public const string DashboardWidget = "dashboard.widget";
}

public static class DashboardEventTypes
{
    // Dashboard lifecycle
    public const string DashboardCreated = "dashboards.dashboard.created";
    public const string DashboardUpdated = "dashboards.dashboard.updated";
    public const string DashboardDeleted = "dashboards.dashboard.deleted";
    public const string DashboardListViewed = "dashboards.dashboard.list.viewed";
    public const string DashboardViewed = "dashboards.dashboard.viewed";

    // Widget lifecycle
    public const string WidgetAdded = "dashboards.widget.added";
    public const string WidgetUpdated = "dashboards.widget.updated";
    public const string WidgetRemoved = "dashboards.widget.removed";

    // Bulk-position updates fire one event regardless of how many widgets
    // moved, since the drag-end handler always sends the whole grid.
    public const string LayoutUpdated = "dashboards.layout.updated";
}
