using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

// Host-side IPluginContext. Built once per Enable, handed to the plugin's
// Configure(). Properties are pre-resolved at construction time; the host
// services pointer is a SafePluginServiceProvider wrapping the root provider
// so only an allowlisted set of cross-cutting types (logger factories,
// TimeProvider, etc.) resolves through it — see SafePluginServiceProvider.
internal sealed class PluginContext : IPluginContext
{
    public PluginContext(
        Guid pluginId,
        string code,
        IHookRegistrar hooks,
        IPluginDataAccess data,
        IPluginMenus menus,
        IPluginBehaviors behaviors,
        IPluginProjections projections,
        IPluginAgentSkills agentSkills,
        IPluginConnectors connectors,
        IServiceProvider hostServices)
    {
        PluginId = pluginId;
        Code = code;
        // Empty Code means the plugin has no provisioned schema (see the
        // UnprovisionedPluginDataAccess branch in PluginRuntime). Don't run
        // it through SchemaNameFor — the validator there rejects anything
        // that isn't [a-z][a-z0-9]{7} now, which is the right behaviour for
        // every real call site but would break the unprovisioned path.
        SchemaName = string.IsNullOrEmpty(code)
            ? string.Empty
            : PluginSchemaProvisioner.SchemaNameFor(code);
        Hooks = hooks;
        Data = data;
        Menus = menus;
        Behaviors = behaviors;
        Projections = projections;
        AgentSkills = agentSkills;
        Connectors = connectors;
        HostServices = hostServices;
    }

    public Guid PluginId { get; }
    public string Code { get; }
    public string SchemaName { get; }
    public IHookRegistrar Hooks { get; }
    public IPluginDataAccess Data { get; }
    public IPluginMenus Menus { get; }
    public IPluginBehaviors Behaviors { get; }
    public IPluginProjections Projections { get; }
    public IPluginAgentSkills AgentSkills { get; }
    public IPluginConnectors Connectors { get; }
    public IServiceProvider HostServices { get; }
}
