namespace AutoNate.Plugins.Abstractions;

// Plugin-facing surface for contributing projections to the host's cache
// framework. The host exposes the full IProjection<T>/IChangeFeed<T>
// machinery to its own code, but plugins use this leaner contract: a name,
// an interval, and a tick delegate. Plugin projections show up on
// /api/admin/projections alongside host projections, with the same
// pause/resume/health surface.
//
// Limitation: jobs registered after host startup don't begin draining until
// the next app restart. Tick scheduling is driven by a HostedService that
// snapshots the registry at boot. Runtime dynamic registration is a Phase 5
// enhancement and will keep this same signature.
public interface IPluginProjections
{
    // Run `tick` every `interval`. Failures are logged + counted but never
    // propagate; the next tick fires on schedule regardless. Names should
    // be globally unique across plugins — collisions throw at registration.
    void RegisterScheduled(string name, TimeSpan interval, Func<CancellationToken, Task> tick);

    // Sweep every job this plugin registered. Mirrors the menu / behavior
    // helpers' RemoveAll pattern. Called by the host on plugin disable.
    int RemoveAll();
}
