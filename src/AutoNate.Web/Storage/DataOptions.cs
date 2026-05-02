namespace AutoNate.Web.Storage;

public sealed class DataOptions
{
    public const string SectionName = "Data";

    // Root of the runtime data tree. Resolved against IHostEnvironment.ContentRootPath
    // when relative; in containers set Data__Root=/app/data via env so the volume mount
    // wins over the relative default. Mirrors how PluginOptions.Folder is resolved.
    public string Root { get; set; } = "data";

    // URL prefix where /Root/wwwroot is exposed publicly. /assets is already taken by
    // the Vite-built React bundle baked into wwwroot at build time, so the runtime
    // data has its own prefix.
    public string PublicUrlPrefix { get; set; } = "/files";
}
