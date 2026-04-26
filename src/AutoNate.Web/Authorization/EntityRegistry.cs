using System.Collections.Frozen;

namespace AutoNate.Web.Authorization;

public sealed class EntityRegistry : IEntityRegistry
{
    private readonly FrozenDictionary<string, IEntityType> _byKind;

    public EntityRegistry(IEnumerable<IEntityType> entityTypes)
    {
        ArgumentNullException.ThrowIfNull(entityTypes);

        var map = new Dictionary<string, IEntityType>(StringComparer.Ordinal);
        foreach (var entityType in entityTypes)
        {
            if (!map.TryAdd(entityType.Kind, entityType))
            {
                throw new InvalidOperationException(
                    $"Entity kind '{entityType.Kind}' is registered more than once.");
            }
        }

        _byKind = map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public IEntityType Get(string kind)
    {
        if (!_byKind.TryGetValue(kind, out var entityType))
        {
            throw new KeyNotFoundException($"Unknown entity kind: '{kind}'.");
        }

        return entityType;
    }

    public bool TryGet(string kind, out IEntityType? type)
    {
        if (_byKind.TryGetValue(kind, out var entityType))
        {
            type = entityType;
            return true;
        }

        type = null;
        return false;
    }

    public IReadOnlyCollection<IEntityType> All => _byKind.Values;
}
