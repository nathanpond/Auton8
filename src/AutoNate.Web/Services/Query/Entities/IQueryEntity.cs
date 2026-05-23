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
