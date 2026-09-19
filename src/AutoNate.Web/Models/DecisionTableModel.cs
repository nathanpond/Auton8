namespace AutoNate.Web.Models;

/// <summary>
/// A decision table as an author edits it (#110).
/// </summary>
/// <remarks>
/// Structured, not DMN. The XML is generated at publish, which is what makes
/// "a rule whose cells do not satisfy their declared types is rejected at save"
/// enforceable at all — you cannot validate cells you did not model.
/// </remarks>
public sealed record DecisionTableModel
{
    public Guid Id { get; init; }

    public string DecisionKey { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string HitPolicy { get; init; } = DecisionHitPolicies.First;

    public IReadOnlyList<DecisionColumn> Inputs { get; init; } = [];

    public IReadOnlyList<DecisionColumn> Outputs { get; init; } = [];

    public IReadOnlyList<DecisionRule> Rules { get; init; } = [];

    public bool IsDraft { get; init; } = true;

    public int DraftVersionNumber { get; init; } = 1;

    public int? PublishedVersionNumber { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public DecisionDeploymentSummary? LastDeployment { get; init; }
}

/// <summary>One typed input or output column.</summary>
/// <param name="Name">
/// The process variable this column reads (input) or writes (output). It is the
/// join between a table and the process that uses it, which is why it is required
/// and validated rather than derived from the label.
/// </param>
public sealed record DecisionColumn(
    string Id,
    string Label,
    string Name,
    string TypeRef);

/// <summary>
/// One rule: a cell per input, a cell per output.
/// </summary>
/// <remarks>
/// The arrays are positional and must match the column counts. That is checked at
/// save rather than at publish, because a ragged rule is the kind of thing that
/// deploys fine and then decides something nobody wrote.
/// </remarks>
public sealed record DecisionRule(
    string Id,
    IReadOnlyList<string> InputEntries,
    IReadOnlyList<string> OutputEntries);

public sealed record DecisionDeploymentSummary(
    string DeploymentId,
    string DecisionId,
    string DecisionKey,
    int DecisionVersion,
    DateTimeOffset DeployedAtUtc);

/// <summary>
/// The hit policies an author may choose, and what each means.
/// </summary>
/// <remarks>
/// <para>
/// Restricted deliberately to the four that are explainable in one sentence. DMN
/// defines more — RULE ORDER, OUTPUT ORDER, PRIORITY — and each needs an ordering
/// concept the editor does not yet express; offering one an author cannot control
/// would be worse than not offering it.
/// </para>
/// <para>
/// Stored as the engine's spelling, so adding one later is a validation change
/// rather than a schema change.
/// </para>
/// </remarks>
public static class DecisionHitPolicies
{
    public const string First = "FIRST";

    public const string Unique = "UNIQUE";

    public const string Any = "ANY";

    public const string Collect = "COLLECT";

    public static readonly IReadOnlyList<string> All = [First, Unique, Any, Collect];

    public static string Describe(string policy) => policy switch
    {
        First => "The first matching rule wins. Later matches are ignored.",
        Unique => "Exactly one rule may match. The engine fails if two do.",
        Any => "Several rules may match, and they must all give the same output.",
        Collect => "Every matching rule contributes an output.",
        _ => "Unknown hit policy."
    };
}

/// <summary>
/// The FEEL types a column may declare.
/// </summary>
/// <remarks>
/// These are the spellings Flowable's DMN engine reads out of
/// <c>inputExpression/@typeRef</c> — measured in #106 against a live engine, not
/// taken from the DMN specification, because the two do not entirely agree.
/// </remarks>
public static class DecisionTypeRefs
{
    public const string String = "string";

    public const string Number = "number";

    public const string Boolean = "boolean";

    public const string Date = "date";

    public static readonly IReadOnlyList<string> All = [String, Number, Boolean, Date];
}
