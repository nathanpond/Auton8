using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

// Stand-in for IPluginMenus used by test setups that don't wire a DbContext
// factory into PluginRuntime. Mirrors the shape of UnprovisionedPluginDataAccess.
internal sealed class NoopPluginMenus : IPluginMenus
{
    private const string Message =
        "Plugin menus are not available in this context (no DbContext factory was supplied to PluginRuntime).";

    public IReadOnlyList<MenuInfo> ListMenus() => throw new InvalidOperationException(Message);
    public Guid AddPluginMenuItem(NewMenuItem item) => throw new InvalidOperationException(Message);
    public Guid AddSiteConfigGroup(string displayName, string? icon, IEnumerable<NewMenuItem> children) =>
        throw new InvalidOperationException(Message);
    public Guid AddMenuItem(string menuKey, Guid? parentId, NewMenuItem item) =>
        throw new InvalidOperationException(Message);
}
