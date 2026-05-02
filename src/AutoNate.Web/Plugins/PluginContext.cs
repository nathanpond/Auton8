using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

// Host-side IPluginContext. Built once per Enable, handed to the plugin's
// Configure(). Properties are pre-resolved at construction time; the host
// services pointer is the global root so plugins can opt into anything
// registered in DI (logger factories, event publishers, etc.).
internal sealed class PluginContext : IPluginContext
{
    public PluginContext(
        Guid pluginId,
        string code,
        IHookRegistrar hooks,
        IPluginDataAccess data,
        IPluginMenus menus,
        IPluginBehaviors behaviors,
        IServiceProvider hostServices)
    {
        PluginId = pluginId;
        Code = code;
        SchemaName = PluginSchemaProvisioner.SchemaNameFor(code);
        Hooks = hooks;
        Data = data;
        Menus = menus;
        Behaviors = behaviors;
        HostServices = hostServices;
    }

    public Guid PluginId { get; }
    public string Code { get; }
    public string SchemaName { get; }
    public IHookRegistrar Hooks { get; }
    public IPluginDataAccess Data { get; }
    public IPluginMenus Menus { get; }
    public IPluginBehaviors Behaviors { get; }
    public IServiceProvider HostServices { get; }
}
