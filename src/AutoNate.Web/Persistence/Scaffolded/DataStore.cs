namespace AutoNate.Web.Persistence.Scaffolded;

public partial class DataStore
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    // Persisted as smallint. See AutoNate.Web.Services.DataStores.DataStoreKind
    // for the enum mapping; reordering values is a breaking change because
    // the int is the on-disk identity.
    public short Kind { get; set; }

    public Guid OwnerUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid UpdatedBy { get; set; }
}
