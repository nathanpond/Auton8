namespace AutoNate.Web.Persistence.Scaffolded;

// History of fetches for a DataConnector. Each FetchAsync invocation
// writes a row. Status: "running" while in-flight, "succeeded" or
// "failed" on completion. Rows are append-only; retention is a follow-up.
public partial class ConnectorRun
{
    public Guid Id { get; set; }

    public Guid DataConnectorId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string Status { get; set; } = "running";

    public long RowsFetched { get; set; }

    public string? ErrorMessage { get; set; }

    public string? CursorBefore { get; set; }

    public string? CursorAfter { get; set; }

    public Guid TriggeredBy { get; set; }
}
