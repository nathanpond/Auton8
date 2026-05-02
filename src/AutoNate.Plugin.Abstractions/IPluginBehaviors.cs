namespace AutoNate.Plugins.Abstractions;

// Plugin-facing helper for contributing IWorkflowBehavior implementations to
// the host's workflow behavior registry. Mirrors IPluginMenus: behaviors
// registered through this surface are tagged with the plugin's id and
// auto-removed on disable, so plugin authors re-register them every time
// their Configure(IPluginContext) runs.
//
// In-flight behavior executions on disable: the registry removal happens
// synchronously, but any awaited ExecuteAsync calls finish (the plugin's
// AssemblyLoadContext stays loaded). New invocations after disable will
// 404 from the host endpoint, which the Flowable bridge surfaces as a
// system failure (job retry).
public interface IPluginBehaviors
{
    void Register(IWorkflowBehavior behavior);

    IReadOnlyList<IWorkflowBehavior> Registered { get; }

    // Removes every behavior this plugin previously registered. The host
    // calls this on disable; plugins can call it explicitly from Cleanup()
    // when they want them gone before host-driven teardown runs. Returns
    // the number of behaviors removed.
    int RemoveAll();
}
