namespace AutoNate.Web.Persistence.Scaffolded;

public partial class RecordActivityRollupCache
{
    public Guid RecordTypeId { get; set; }

    public DateOnly BucketDay { get; set; }

    public int RecordsCreated { get; set; }

    public int RecordsUpdated { get; set; }

    public int RecordsArchived { get; set; }

    public int ProjectionVersion { get; set; }

    public DateTime LastSyncAtUtc { get; set; }
}
