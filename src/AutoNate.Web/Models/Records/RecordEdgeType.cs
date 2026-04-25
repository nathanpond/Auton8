namespace AutoNate.Web.Models.Records;

public sealed record class RecordEdgeType
{
    public Guid Id { get; init; }

    public string ShortCode { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? InverseName { get; init; }

    public bool IsDirected { get; init; } = true;

    public bool AllowSelfReference { get; init; }

    public string Cardinality { get; init; } = RecordEdgeCardinality.ManyToMany;

    public IReadOnlyList<Guid>? FromRecordTypeIds { get; init; }

    public IReadOnlyList<Guid>? ToRecordTypeIds { get; init; }

    public bool IsArchived { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public static class RecordEdgeCardinality
{
    public const string OneToOne = "one_to_one";
    public const string OneToMany = "one_to_many";
    public const string ManyToOne = "many_to_one";
    public const string ManyToMany = "many_to_many";

    public static bool IsValid(string value) =>
        value is OneToOne or OneToMany or ManyToOne or ManyToMany;
}
