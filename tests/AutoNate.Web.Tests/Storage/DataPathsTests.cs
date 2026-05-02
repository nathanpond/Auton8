using AutoNate.Web.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests.Storage;

public sealed class DataPathsTests : IDisposable
{
    private readonly string _contentRoot;

    public DataPathsTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), "autonate-datapaths-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void RelativeRoot_IsResolvedAgainstContentRoot_AndAllSubdirsAreCreated()
    {
        var paths = new DataPaths(
            Options.Create(new DataOptions { Root = "data", PublicUrlPrefix = "/files" }),
            new TestEnv(_contentRoot));

        Assert.Equal(Path.Combine(_contentRoot, "data"), paths.Root);
        Assert.Equal(Path.Combine(_contentRoot, "data", "wwwroot"), paths.PublicRoot);
        Assert.Equal(Path.Combine(_contentRoot, "data", "plugins"), paths.PluginsRoot);
        Assert.Equal(Path.Combine(_contentRoot, "data", "uploads"), paths.UploadsRoot);
        Assert.Equal(Path.Combine(_contentRoot, "data", "repositories"), paths.RepositoriesRoot);
        Assert.Equal(Path.Combine(_contentRoot, "data", "tmp"), paths.TempRoot);

        Assert.True(Directory.Exists(paths.Root));
        Assert.True(Directory.Exists(paths.PublicRoot));
        Assert.True(Directory.Exists(paths.PluginsRoot));
        Assert.True(Directory.Exists(paths.UploadsRoot));
        Assert.True(Directory.Exists(paths.RepositoriesRoot));
        Assert.True(Directory.Exists(paths.TempRoot));
    }

    [Fact]
    public void AbsoluteRoot_IsUsedAsIs()
    {
        var absolute = Path.Combine(_contentRoot, "absolute-data");

        var paths = new DataPaths(
            Options.Create(new DataOptions { Root = absolute }),
            new TestEnv(_contentRoot));

        Assert.Equal(absolute, paths.Root);
        Assert.True(Directory.Exists(absolute));
    }

    [Fact]
    public void EmptyRoot_FallsBackToDefaultRelativePath()
    {
        var paths = new DataPaths(
            Options.Create(new DataOptions { Root = string.Empty }),
            new TestEnv(_contentRoot));

        Assert.Equal(Path.Combine(_contentRoot, "data"), paths.Root);
    }

    private sealed class TestEnv : IHostEnvironment
    {
        public TestEnv(string contentRoot)
        {
            ContentRootPath = contentRoot;
        }
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "AutoNate.Web.Tests";
        public string ContentRootPath { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
