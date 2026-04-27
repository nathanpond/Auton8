using AutoNate.Plugins.Abstractions;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Hooks;

// Sync and async action callbacks live in separate registries — Do() iterates
// only the sync list, DoAsync() only the async list. Callbacks run isolated:
// one throw is logged and swallowed, peers still execute.
public sealed class ActionHub : IActionHub
{
    private readonly HookRegistry<Action<object?[]>> _sync = new();
    private readonly HookRegistry<Func<object?[], CancellationToken, Task>> _async = new();
    private readonly ILogger<ActionHub> _log;

    public ActionHub(ILogger<ActionHub> log) { _log = log; }

    internal HookRegistry<Action<object?[]>> Sync => _sync;
    internal HookRegistry<Func<object?[], CancellationToken, Task>> Async => _async;

    public bool HasAction(string hookName) => _sync.HasAny(hookName) || _async.HasAny(hookName);

    public void Do(string hookName, params object?[] args)
    {
        var snapshot = _sync.Snapshot(hookName);
        if (snapshot.IsEmpty) return;
        foreach (var sub in snapshot)
        {
            try { sub.Callback(args); }
            catch (Exception ex)
            {
                _log.LogError(ex, "action callback for {Hook} (handle {Handle}) threw; continuing", hookName, sub.Handle.Id);
            }
        }
    }

    public async Task DoAsync(string hookName, CancellationToken cancellationToken = default, params object?[] args)
    {
        var snapshot = _async.Snapshot(hookName);
        if (snapshot.IsEmpty) return;
        foreach (var sub in snapshot)
        {
            try { await sub.Callback(args, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _log.LogError(ex, "async action callback for {Hook} (handle {Handle}) threw; continuing", hookName, sub.Handle.Id);
            }
        }
    }
}
