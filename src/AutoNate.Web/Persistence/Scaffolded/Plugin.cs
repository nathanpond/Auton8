namespace AutoNate.Web.Persistence.Scaffolded;

public partial class Plugin
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string Version { get; set; } = null!;

    public string EntryAssembly { get; set; } = null!;

    public string? EntryType { get; set; }

    public int Status { get; set; }

    public DateTime UploadedAt { get; set; }

    public Guid UploadedBy { get; set; }

    public DateTime? LastEnabledAt { get; set; }

    public DateTime? LastDisabledAt { get; set; }

    public string? LastError { get; set; }
}
