namespace AutoNate.Web.Persistence.Scaffolded;

public partial class DataConnector
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    // String-keyed because plugins extend with their own connector kinds.
    // Built-in values live in AutoNate.Web.Services.DataConnectors.DataConnectorKinds.
    public string Kind { get; set; } = null!;

    // Connector-specific configuration. Handler decides the schema; the row
    // is opaque to host CRUD code.
    public string ConfigJson { get; set; } = "{}";

    // Per-connector incremental-fetch bookmark managed by handlers. Null
    // until the first fetch completes.
    public DateTime? LastFetchedAtUtc { get; set; }

    public string? Cursor { get; set; }

    public Guid OwnerUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid UpdatedBy { get; set; }
}
