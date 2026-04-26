namespace AutoNate.Web.Authorization;

public interface IEntityType
{
    string Kind { get; }

    Type ClrType { get; }

    Type IdClrType { get; }

    IReadOnlySet<string> Actions { get; }

    IReadOnlySet<string> Tags { get; }

    string ParseId(string raw);
}
