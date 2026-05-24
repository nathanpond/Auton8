namespace AutoNate.Web.Persistence.Scaffolded;

public partial class ProcessRetentionConfig
{
    public string ProcessDefinitionKey { get; set; } = null!;

    public int RetainDays { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid? UpdatedBy { get; set; }
}
