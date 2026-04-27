namespace AutoNate.Web.Plugins;

public sealed class PluginOptions
{
    public const string SectionName = "Plugins";

    // Resolved against AppContext.BaseDirectory at startup; relative paths
    // make dev/prod symmetric.
    public string Folder { get; set; } = "plugins";

    // Cap on uncompressed size of an uploaded zip. 50 MB default — large
    // enough for plugins with private deps, small enough to bound an upload's
    // disk impact.
    public long MaxUploadBytes { get; set; } = 52_428_800;

    // When true, a plugin's Configure() throwing during startup brings down
    // the host. Otherwise the failure is logged, the row's status is flipped
    // to Disabled with last_error populated, and the host continues.
    public bool FailFastOnStartup { get; set; } = false;
}
