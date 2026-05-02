using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Workflow.Behaviors;

// Aggregate of every IWorkflowBehavior in the system: built-ins resolved
// from DI plus plugin-contributed ones registered at enable time. The
// HTTP endpoint and the studio catalog both read through this surface.
public interface IWorkflowBehaviorRegistry
{
    IWorkflowBehavior? Get(string key);

    IReadOnlyList<IWorkflowBehavior> GetAll();

    // Plugin contribution path. The plugin id tag lets us sweep by plugin
    // on disable. Returns false (and logs a warning) when a plugin tries
    // to register a key that's already taken — built-ins win, prior
    // plugin registrations win against later ones.
    bool RegisterFromPlugin(Guid pluginId, IWorkflowBehavior behavior);

    int RemoveAllForPlugin(Guid pluginId);
}
