using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Hooks;

namespace AutoNate.Web.Plugins;

internal sealed record LoadedPlugin(
    Guid Id,
    string Name,
    string Version,
    PluginAssemblyLoadContext Alc,
    ScopedHookRegistrar ScopedRegistrar,
    IAutoNatePlugin Instance);
