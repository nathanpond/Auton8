using System.Security.Claims;

namespace AutoNate.Web.Services.Query.Entities;

// Schema column for the validator: name, data type, whether the column can
// be used as an aggregate target (numerics + dates), and whether it's a
// built-in system column or a user-defined (RecordType-scoped) field.
public sealed record QueryColumn(
    string Name,
    QueryDataType DataType,
    bool IsAggregable,
    bool IsSystem);

// An entity adapter knows how to validate references against its own schema
// and how to translate a validated AST into a QueryResult. Each entity also
// declares which AQL functions (NUMNODES, USESNODE, etc.) it supports so the
// validator can reject everything else with a friendly message.
//
// Entities expose a "prepare then execute" surface so any state resolved at
// validation time (e.g. RecordType name → id lookups) can flow into execution
// without leaking back through the validator or the executor.
public interface IQueryEntity
{
    string Name { get; }

    IReadOnlyList<QueryColumn> StaticSchema { get; }

    IReadOnlyList<string> AllowedFunctions { get; }

    // Function names that may appear in COLUMNS()/ORDER BY as per-row scalar
    // calls (e.g. COUNTCHILDREN(), FULLPATH()) — like aggregates in syntax
    // but evaluated row-by-row, so no GROUP(...) is required. The entity is
    // responsible for computing them and declaring their result data type via
    // RowFunctionDataType. Empty by default.
    IReadOnlyList<string> RowFunctions => Array.Empty<string>();

    // Result data type of a row function (must be one declared in RowFunctions).
    // Called by the validator/executor to label result columns. Defaults to Number.
    QueryDataType RowFunctionDataType(string functionName) => QueryDataType.Number;

    // Whether a row function accepts an argument. Most row functions are
    // zero-arg (COUNTCHILDREN(), NUMNODES()), but some take a single field-
    // shaped arg the entity uses to pick which value to return per row
    // (e.g. CURRENTSTEP(Name) vs CURRENTSTEP(Assignee) on Flows). The
    // default is `false` so existing entities don't change behavior; the
    // validator emits "FN() does not take an argument" when false and
    // the caller passes one anyway. Entities that opt in still own the
    // arg's semantics — there's no validator-side allowlist of arg values.
    bool RowFunctionAcceptsArgument(string functionName) => false;

    // Closed-set value suggestions for a column. Keys are column names;
    // values are the legal labels (e.g. Flows.Status → "In-progress",
    // "Completed", ...). Powers the autocomplete UI's value dropdown after
    // `Status = `. Default empty — entities opt in only for columns whose
    // domain is fixed at compile time.
    IReadOnlyDictionary<string, IReadOnlyList<string>> ColumnEnums =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    // DB-backed variant. Records overrides this to return the live list of
    // RecordType names (and may use the recordTypeFilter to scope further).
    // Defaults to whatever ColumnEnums returns so non-dynamic entities get
    // the right behavior for free.
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetDynamicColumnEnumsAsync(
        string? recordTypeFilter,
        CancellationToken cancellationToken) =>
        Task.FromResult(ColumnEnums);

    // Resolve the full schema (static + dynamic), resolve literal references
    // (e.g. RecordType names), and return a prepared query the validator can
    // run generic checks against and the executor can fire.
    Task<IPreparedQuery> PrepareAsync(AqlQuery query, CancellationToken cancellationToken);
}

public interface IPreparedQuery
{
    IReadOnlyList<QueryColumn> Schema { get; }
    IReadOnlyList<string> ValidationErrors { get; }
    IQueryEntity Entity { get; }
    AqlQuery Query { get; }

    Task<QueryResult> ExecuteAsync(
        ClaimsPrincipal actor,
        int? hardCap,
        CancellationToken cancellationToken);
}
