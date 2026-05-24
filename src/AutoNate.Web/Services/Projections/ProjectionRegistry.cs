namespace AutoNate.Web.Services.Projections;

public sealed class ProjectionRegistry : IProjectionRegistry
{
    private readonly Dictionary<string, IProjection> _byName;

    public ProjectionRegistry(IEnumerable<IProjection> projections)
    {
        _byName = new Dictionary<string, IProjection>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projections)
        {
            if (!_byName.TryAdd(p.Name, p))
            {
                throw new InvalidOperationException(
                    $"Duplicate projection registration for name '{p.Name}'.");
            }
        }

        Projections = _byName.Values.ToList();
    }

    public IReadOnlyList<IProjection> Projections { get; }

    public IProjection? TryGet(string name) =>
        _byName.TryGetValue(name, out var p) ? p : null;
}
