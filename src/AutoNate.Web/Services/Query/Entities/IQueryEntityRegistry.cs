namespace AutoNate.Web.Services.Query.Entities;

public interface IQueryEntityRegistry
{
    IReadOnlyList<string> EntityNames { get; }
    bool TryGet(string name, out IQueryEntity entity);
}

public sealed class QueryEntityRegistry : IQueryEntityRegistry
{
    private readonly IReadOnlyDictionary<string, IQueryEntity> _byName;

    public QueryEntityRegistry(IEnumerable<IQueryEntity> entities)
    {
        _byName = entities.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        EntityNames = _byName.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<string> EntityNames { get; }

    public bool TryGet(string name, out IQueryEntity entity)
    {
        if (_byName.TryGetValue(name, out var found))
        {
            entity = found;
            return true;
        }
        entity = null!;
        return false;
    }
}
