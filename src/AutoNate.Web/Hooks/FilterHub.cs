using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Hooks;

// Typed filter chain. AddFilter<T> wraps the typed callback into an erased
// Func<object?, object?[], object?>; Apply<T> casts at the boundary. One
// boxing per chain step for value-type T; zero for reference types.
//
// Filters fail loud: a callback that throws stops the chain and the exception
// propagates. Callers that need fail-secure semantics wrap the call themselves
// (see Authorizer.ApplyAuthorizeFilterAsync).
public sealed class FilterHub : IFilterHub
{
    private readonly HookRegistry<Func<object?, object?[], object?>> _sync = new();
    private readonly HookRegistry<Func<object?, object?[], CancellationToken, Task<object?>>> _async = new();

    internal HookRegistry<Func<object?, object?[], object?>> Sync => _sync;
    internal HookRegistry<Func<object?, object?[], CancellationToken, Task<object?>>> Async => _async;

    public bool HasFilter(string hookName) => _sync.HasAny(hookName) || _async.HasAny(hookName);

    public T Apply<T>(string hookName, T value, params object?[] args)
    {
        var snapshot = _sync.Snapshot(hookName);
        if (snapshot.IsEmpty) return value;
        object? current = value;
        foreach (var sub in snapshot)
        {
            current = sub.Callback(current, args);
        }
        return (T)current!;
    }

    public async Task<T> ApplyAsync<T>(string hookName, T value, CancellationToken cancellationToken = default, params object?[] args)
    {
        var snapshot = _async.Snapshot(hookName);
        if (snapshot.IsEmpty) return value;
        object? current = value;
        foreach (var sub in snapshot)
        {
            current = await sub.Callback(current, args, cancellationToken).ConfigureAwait(false);
        }
        return (T)current!;
    }
}
