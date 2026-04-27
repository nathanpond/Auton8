using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Hooks;

// Per-plugin facade over HookRegistrar. Records (handle, name, kind) so the
// loader can yank every subscription a plugin registered when it's disabled or
// deleted, without forcing the plugin to track its own handles.
public sealed class ScopedHookRegistrar : IHookRegistrar
{
    private readonly HookRegistrar _root;
    private readonly object _lock = new();
    private readonly List<Entry> _entries = new();

    public ScopedHookRegistrar(HookRegistrar root) { _root = root; }

    private enum Kind { ActionSync, ActionAsync, FilterSync, FilterAsync }
    private readonly record struct Entry(HookHandle Handle, string Name, Kind Kind);

    public HookHandle AddAction(string hookName, int priority, Action<object?[]> callback)
    {
        var h = _root.Actions.Sync.Add(hookName, priority, callback);
        Track(h, hookName, Kind.ActionSync);
        return h;
    }

    public HookHandle AddActionAsync(string hookName, int priority, Func<object?[], CancellationToken, Task> callback)
    {
        var h = _root.Actions.Async.Add(hookName, priority, callback);
        Track(h, hookName, Kind.ActionAsync);
        return h;
    }

    public void RemoveAction(HookHandle handle) => RemoveTracked(handle);

    public HookHandle AddFilter<T>(string hookName, int priority, Func<T, object?[], T> callback)
    {
        Func<object?, object?[], object?> erased = (val, args) => callback((T)val!, args);
        var h = _root.Filters.Sync.Add(hookName, priority, erased);
        Track(h, hookName, Kind.FilterSync);
        return h;
    }

    public HookHandle AddFilterAsync<T>(string hookName, int priority, Func<T, object?[], CancellationToken, Task<T>> callback)
    {
        Func<object?, object?[], CancellationToken, Task<object?>> erased =
            async (val, args, ct) => (object?)await callback((T)val!, args, ct).ConfigureAwait(false);
        var h = _root.Filters.Async.Add(hookName, priority, erased);
        Track(h, hookName, Kind.FilterAsync);
        return h;
    }

    public void RemoveFilter(HookHandle handle) => RemoveTracked(handle);

    public void RemoveAllForPlugin()
    {
        Entry[] snapshot;
        lock (_lock)
        {
            snapshot = _entries.ToArray();
            _entries.Clear();
        }
        foreach (var e in snapshot) RemoveByEntry(e);
    }

    private void Track(HookHandle handle, string name, Kind kind)
    {
        lock (_lock) _entries.Add(new Entry(handle, name, kind));
    }

    private void RemoveTracked(HookHandle handle)
    {
        Entry? toRemove = null;
        lock (_lock)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Handle.Id == handle.Id)
                {
                    toRemove = _entries[i];
                    _entries.RemoveAt(i);
                    break;
                }
            }
        }
        if (toRemove is { } e) RemoveByEntry(e);
    }

    private void RemoveByEntry(Entry e)
    {
        switch (e.Kind)
        {
            case Kind.ActionSync: _root.Actions.Sync.Remove(e.Name, e.Handle); break;
            case Kind.ActionAsync: _root.Actions.Async.Remove(e.Name, e.Handle); break;
            case Kind.FilterSync: _root.Filters.Sync.Remove(e.Name, e.Handle); break;
            case Kind.FilterAsync: _root.Filters.Async.Remove(e.Name, e.Handle); break;
        }
    }
}
