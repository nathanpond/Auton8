namespace AutoNate.Web.Plugins;

public sealed class PluginOptions
{
    public const string SectionName = "Plugins";

    // Optional override for where plugin folders are extracted. When unset,
    // plugins live under IDataPaths.PluginsRoot (i.e. {DataRoot}/plugins) so
    // installed plugins persist across container redeploys via the data volume.
    // Relative values are resolved against AppContext.BaseDirectory; absolute
    // values are used as-is. Leave empty in normal deployments.
    public string Folder { get; set; } = string.Empty;

    // Cap on uncompressed size of an uploaded zip. 50 MB default — large
    // enough for plugins with private deps, small enough to bound an upload's
    // disk impact.
    public long MaxUploadBytes { get; set; } = 52_428_800;

    // When true, a plugin's Configure() throwing during startup brings down
    // the host. Otherwise the failure is logged, the row's status is flipped
    // to Disabled with last_error populated, and the host continues.
    public bool FailFastOnStartup { get; set; } = false;
}
