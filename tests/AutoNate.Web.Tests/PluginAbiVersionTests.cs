using System.Reflection;
using AutoNate.Plugins.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests;

// The plugin ABI's assembly identity.
//
// A plugin compiles against AutoNate.Plugin.Abstractions and ships without it
// (Private=false in plugins/Directory.Build.props), so the host's loaded copy
// defines type identity across the AssemblyLoadContext boundary. The reference
// baked into a plugin's DLL names a version — so moving this number means the
// host can no longer satisfy what every already-built third-party plugin asks
// for.
//
// This exists because it already happened: adding <Version>0.1.0</Version> to
// the repo-root Directory.Build.props silently swept the ABI along with it and
// broke plugin loading. The symptom does not look like a binding failure —
// enable returns 400 with "Type 'X' not found in 'X.dll'", which reads as a
// badly-built plugin — so the cost of rediscovering it is high and the cost of
// this test is one assertion.
//
// CLAUDE.md lists the plugin ABI among the identifiers that must not be
// renamed. Its version is the same invariant by another name.
public sealed class PluginAbiVersionTests
{
    [Fact]
    public void Abstractions_assembly_version_is_pinned()
    {
        var version = typeof(IAutoNatePlugin).Assembly.GetName().Version;

        Assert.Equal(new Version(1, 0, 0, 0), version);
    }

    [Fact]
    public void Abstractions_assembly_version_does_not_follow_the_product_version()
    {
        // The product version is free to move; the ABI's is not. If these ever
        // agree by accident the guard above still holds, but this states the
        // relationship the pin exists to preserve.
        var abi = typeof(IAutoNatePlugin).Assembly.GetName().Version!;
        var product = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.NotNull(product);
        Assert.StartsWith("0.1.0", product);
        Assert.NotEqual("0.1.0.0", abi.ToString());
    }
}
