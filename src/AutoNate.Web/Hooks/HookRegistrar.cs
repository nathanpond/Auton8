using AutoNate.Plugins.Abstractions;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Hooks;

// Singleton root that owns both hubs. Plugins receive IHookRegistrar (write
// surface); host services receive IActionHub / IFilterHub (read/dispatch
// surface). Same instance underneath, two seams.
public sealed class HookRegistrar : IHookRegistrar
{
    public ActionHub Actions { get; }
    public FilterHub Filters { get; }

    public HookRegistrar(ILogger<ActionHub> actionLog)
    {
        Actions = new ActionHub(actionLog);
        Filters = new FilterHub();
    }

    public HookHandle AddAction(string hookName, int priority, Action<object?[]> callback) =>
        Actions.Sync.Add(hookName, priority, callback);

    public HookHandle AddActionAsync(string hookName, int priority, Func<object?[], CancellationToken, Task> callback) =>
        Actions.Async.Add(hookName, priority, callback);

    public void RemoveAction(HookHandle handle)
    {
        // Caller didn't tell us which hook name the handle belongs to; this is
        // intentionally O(N hook names). N stays small (we expect tens of hook
        // points), so the simple sweep is fine.
        RemoveFromAll(Actions.Sync, handle);
        RemoveFromAll(Actions.Async, handle);
    }

    public HookHandle AddFilter<T>(string hookName, int priority, Func<T, object?[], T> callback)
    {
        Func<object?, object?[], object?> erased = (val, args) => callback((T)val!, args);
        return Filters.Sync.Add(hookName, priority, erased);
    }

    public HookHandle AddFilterAsync<T>(string hookName, int priority, Func<T, object?[], CancellationToken, Task<T>> callback)
    {
        Func<object?, object?[], CancellationToken, Task<object?>> erased =
            async (val, args, ct) => (object?)await callback((T)val!, args, ct).ConfigureAwait(false);
        return Filters.Async.Add(hookName, priority, erased);
    }

    public void RemoveFilter(HookHandle handle)
    {
        RemoveFromAll(Filters.Sync, handle);
        RemoveFromAll(Filters.Async, handle);
    }

    private static void RemoveFromAll<TDelegate>(HookRegistry<TDelegate> registry, HookHandle handle)
        where TDelegate : Delegate => registry.RemoveAnywhere(handle);
}
