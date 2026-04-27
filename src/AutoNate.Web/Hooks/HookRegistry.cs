using System.Collections.Concurrent;
using System.Collections.Immutable;
using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Hooks;

// Copy-on-write registry: a ConcurrentDictionary keyed by hook name, holding
// ImmutableArrays sorted by (priority asc, registrationOrder asc). Dispatch reads
// a frozen snapshot — registrations during in-flight dispatch don't mutate it.
internal sealed class HookRegistry<TDelegate> where TDelegate : Delegate
{
    private readonly ConcurrentDictionary<string, ImmutableArray<HookSubscription<TDelegate>>> _byName =
        new(StringComparer.Ordinal);

    private long _nextOrder;

    public HookHandle Add(string name, int priority, TDelegate callback)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(callback);

        var handle = new HookHandle(Guid.NewGuid());
        var order = Interlocked.Increment(ref _nextOrder);
        var sub = new HookSubscription<TDelegate>(handle, priority, order, callback);

        _byName.AddOrUpdate(
            name,
            _ => ImmutableArray.Create(sub),
            (_, existing) => Insert(existing, sub));

        return handle;
    }

    public bool Remove(string name, HookHandle handle)
    {
        if (!_byName.TryGetValue(name, out var existing)) return false;

        var index = -1;
        for (var i = 0; i < existing.Length; i++)
        {
            if (existing[i].Handle.Id == handle.Id) { index = i; break; }
        }
        if (index < 0) return false;

        var updated = existing.RemoveAt(index);
        if (updated.IsEmpty)
        {
            _byName.TryRemove(name, out _);
            return true;
        }

        return _byName.TryUpdate(name, updated, existing) || RemoveSlow(name, handle);
    }

    public ImmutableArray<HookSubscription<TDelegate>> Snapshot(string name) =>
        _byName.TryGetValue(name, out var arr) ? arr : ImmutableArray<HookSubscription<TDelegate>>.Empty;

    public bool HasAny(string name) =>
        _byName.TryGetValue(name, out var arr) && !arr.IsEmpty;

    public IEnumerable<string> Names => _byName.Keys;

    public bool RemoveAnywhere(HookHandle handle)
    {
        foreach (var name in _byName.Keys)
        {
            if (Remove(name, handle)) return true;
        }
        return false;
    }

    private static ImmutableArray<HookSubscription<TDelegate>> Insert(
        ImmutableArray<HookSubscription<TDelegate>> existing,
        HookSubscription<TDelegate> sub)
    {
        var idx = 0;
        while (idx < existing.Length)
        {
            var cur = existing[idx];
            if (cur.Priority > sub.Priority) break;
            if (cur.Priority == sub.Priority && cur.RegistrationOrder > sub.RegistrationOrder) break;
            idx++;
        }
        return existing.Insert(idx, sub);
    }

    // Falls back to a re-read + retry loop when a concurrent mutation invalidated
    // our snapshot; small race window between TryGetValue and TryUpdate above.
    private bool RemoveSlow(string name, HookHandle handle)
    {
        while (true)
        {
            if (!_byName.TryGetValue(name, out var existing)) return false;
            var index = -1;
            for (var i = 0; i < existing.Length; i++)
            {
                if (existing[i].Handle.Id == handle.Id) { index = i; break; }
            }
            if (index < 0) return false;
            var updated = existing.RemoveAt(index);
            if (updated.IsEmpty)
            {
                if (((ICollection<KeyValuePair<string, ImmutableArray<HookSubscription<TDelegate>>>>)_byName)
                    .Remove(new KeyValuePair<string, ImmutableArray<HookSubscription<TDelegate>>>(name, existing)))
                    return true;
            }
            else if (_byName.TryUpdate(name, updated, existing))
            {
                return true;
            }
        }
    }
}
