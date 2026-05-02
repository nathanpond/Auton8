using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Storage;

public sealed class DataPaths : IDataPaths
{
    public DataPaths(IOptions<DataOptions> options, IHostEnvironment env)
    {
        var configured = options.Value.Root;
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = "data";
        }
        Root = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(env.ContentRootPath, configured);

        PublicRoot = Path.Combine(Root, "wwwroot");
        PluginsRoot = Path.Combine(Root, "plugins");
        UploadsRoot = Path.Combine(Root, "uploads");
        RepositoriesRoot = Path.Combine(Root, "repositories");
        TempRoot = Path.Combine(Root, "tmp");

        // Create eagerly: PhysicalFileProvider throws if PublicRoot is missing
        // when the static-files middleware is wired, and callers shouldn't have
        // to defensively CreateDirectory() on every write. Idempotent.
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(PublicRoot);
        Directory.CreateDirectory(PluginsRoot);
        Directory.CreateDirectory(UploadsRoot);
        Directory.CreateDirectory(RepositoriesRoot);
        Directory.CreateDirectory(TempRoot);
    }

    public string Root { get; }
    public string PublicRoot { get; }
    public string PluginsRoot { get; }
    public string UploadsRoot { get; }
    public string RepositoriesRoot { get; }
    public string TempRoot { get; }
}
