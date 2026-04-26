namespace AutoNate.Web.Authorization;

public sealed class EntityTypeDefinition : IEntityType
{
    private readonly Func<string, string> _idParser;

    public EntityTypeDefinition(
        string kind,
        Type clrType,
        Type idClrType,
        IEnumerable<string> actions,
        IEnumerable<string> tags,
        Func<string, string>? idParser = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(clrType);
        ArgumentNullException.ThrowIfNull(idClrType);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(tags);

        Kind = kind;
        ClrType = clrType;
        IdClrType = idClrType;
        Actions = new HashSet<string>(actions, StringComparer.Ordinal);
        Tags = new HashSet<string>(tags, StringComparer.Ordinal);
        _idParser = idParser ?? DefaultIdParser(idClrType);
    }

    public string Kind { get; }

    public Type ClrType { get; }

    public Type IdClrType { get; }

    public IReadOnlySet<string> Actions { get; }

    public IReadOnlySet<string> Tags { get; }

    public string ParseId(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        return _idParser(raw);
    }

    private static Func<string, string> DefaultIdParser(Type idClrType)
    {
        if (idClrType == typeof(Guid))
        {
            return raw => Guid.Parse(raw).ToString();
        }

        if (idClrType == typeof(string))
        {
            return raw => raw;
        }

        return raw => raw;
    }
}
