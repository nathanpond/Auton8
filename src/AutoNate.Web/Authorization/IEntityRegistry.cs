namespace AutoNate.Web.Authorization;

public interface IEntityRegistry
{
    IEntityType Get(string kind);

    bool TryGet(string kind, out IEntityType? type);

    IReadOnlyCollection<IEntityType> All { get; }
}
