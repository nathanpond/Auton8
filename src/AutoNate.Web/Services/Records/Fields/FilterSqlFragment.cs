namespace AutoNate.Web.Services.Records.Fields;

public enum FilterOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains,
    In
}

/// <summary>
/// A parameterized SQL fragment that filters records on a JSONB field. The
/// <see cref="Sql"/> string references parameter names in the form
/// <c>@p{index}</c> starting from the <paramref name="ParameterOffset"/> supplied
/// by the caller. Field-type implementations return these fragments; a query
/// composer stitches them together and binds parameters in order.
/// </summary>
public sealed record class FilterSqlFragment(string Sql, IReadOnlyList<object?> Parameters)
{
    public static readonly FilterSqlFragment Empty = new("TRUE", Array.Empty<object?>());
}
