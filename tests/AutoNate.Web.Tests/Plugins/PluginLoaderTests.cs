using System.Security.Claims;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Hooks;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests.Plugins;

// Exercises the real PluginAssemblyLoadContext + PluginRuntime path against
// the sample plugin assembly that the test project's MSBuild target stages
// under test-plugins/SamplePlugin/. This is the test that proves shared-
// assembly type identity (IAutoNatePlugin) unifies across host + plugin ALCs.
public sealed class PluginLoaderTests : IDisposable
{
    private readonly string _tempRoot;

    public PluginLoaderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "autonate-plugin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task EnableDisable_RegistersAndRevokesHooks()
    {
        var (runtime, registrar) = BuildRuntime();
        var row = StageSamplePlugin();

        Assert.False(registrar.Filters.HasFilter(HookPoints.AuthorizeAuthorize));

        var enabled = await runtime.EnableAsync(row, CancellationToken.None);
        Assert.True(enabled.Success);
        Assert.True(registrar.Filters.HasFilter(HookPoints.AuthorizeAuthorize));

        // Filter should turn an Allow into Deny with reason "sample-plugin"
        var raw = new AuthorizeFilterContext
        {
            Actor = new ClaimsPrincipal(new ClaimsIdentity()),
            Action = "view",
            Target = new EntityRefDto("record", Guid.NewGuid().ToString()),
            CurrentDecision = new AuthDecisionDto { Effect = AuthEffectDto.Allow, Reason = "raw" }
        };
        var filtered = await registrar.Filters.ApplyAsync(HookPoints.AuthorizeAuthorize, raw);
        Assert.Equal(AuthEffectDto.Deny, filtered.CurrentDecision.Effect);
        Assert.Equal("sample-plugin", filtered.CurrentDecision.Reason);

        await runtime.DisableAsync(row.Id, CancellationToken.None);
        Assert.False(registrar.Filters.HasFilter(HookPoints.AuthorizeAuthorize));
    }

    [Fact]
    public async Task Enable_WrongEntryAssembly_ReturnsFailureWithMessage()
    {
        var (runtime, _) = BuildRuntime();
        var row = StageSamplePlugin();
        row.EntryAssembly = "DoesNotExist.dll";

        var result = await runtime.EnableAsync(row, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Disable_IsIdempotent()
    {
        var (runtime, _) = BuildRuntime();
        var row = StageSamplePlugin();

        await runtime.EnableAsync(row, CancellationToken.None);
        await runtime.DisableAsync(row.Id, CancellationToken.None);
        // Second disable on a not-loaded plugin should be a no-op.
        await runtime.DisableAsync(row.Id, CancellationToken.None);
    }

    private (PluginRuntime, HookRegistrar) BuildRuntime()
    {
        var registrar = new HookRegistrar(NullLogger<ActionHub>.Instance);
        var services = new ServiceCollection().BuildServiceProvider();
        var options = Options.Create(new PluginOptions { Folder = _tempRoot });
        var runtime = new PluginRuntime(registrar, services, options, NullLogger<PluginRuntime>.Instance);
        return (runtime, registrar);
    }

    private Plugin StageSamplePlugin()
    {
        // Copy the staged sample plugin (built by SamplePlugin csproj + the
        // StageSamplePluginForTests msbuild target) into a per-plugin folder
        // under our test root, keyed by a fresh GUID — same shape PluginRuntime
        // expects in production.
        var stagedRoot = Path.Combine(AppContext.BaseDirectory, "test-plugins", "SamplePlugin");
        if (!Directory.Exists(stagedRoot))
        {
            throw new InvalidOperationException(
                $"Sample plugin output not found at '{stagedRoot}'. The test project's StageSamplePluginForTests target should have copied it; check the AutoNate.Web.Tests.SamplePlugin build.");
        }

        var id = Guid.NewGuid();
        var dest = Path.Combine(_tempRoot, id.ToString("D"));
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(stagedRoot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(stagedRoot, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }

        return new Plugin
        {
            Id = id,
            Name = "AutoNate.Web.Tests.SamplePlugin",
            Version = "1.0.0",
            EntryAssembly = "AutoNate.Web.Tests.SamplePlugin.dll",
            EntryType = "AutoNate.Web.Tests.SamplePlugin.SamplePlugin",
            Status = (int)PluginStatus.Disabled,
            UploadedAt = DateTime.UtcNow,
            UploadedBy = Guid.NewGuid(),
        };
    }
}
