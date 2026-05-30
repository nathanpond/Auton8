using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Datasets;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Query.Entities;

// AQL entity surfaced as `FROM Dataset("name")` per Phase 2 of the Data
// Stores plan. Resolves the dataset by name at PrepareAsync time, threads
// the persisted ColumnSchemaJson into the validator as the entity's
// effective schema, and dispatches execution to IDatasetExecutor (which
// routes by Mode + SourceKind).
//
// Permissions: a dataset grant fully overrides the underlying source's
// grant. PrepareAsync checks `dataset:view:<id>` on the resolved dataset;
// the validator path never sees the source datastore/connector, by design.
public sealed class DatasetQueryEntity(
    IDatasetStore datasetStore,
    IDatasetExecutor executor,
    IAuthorizer authorizer) : IQueryEntity
{
    public string Name => "Dataset";

    // The bare-FROM schema is empty — every column comes from the resolved
    // dataset's persisted ColumnSchemaJson, which the suggestion service
    // doesn't reach for at catalog time. The /api/aql/schema endpoint shows
    // no static columns; the editor fetches the dataset's schema after the
    // argument is filled in (Phase 2.1: per-argument column hint endpoint).
    public IReadOnlyList<QueryColumn> StaticSchema { get; } = Array.Empty<QueryColumn>();

    public IReadOnlyList<string> AllowedFunctions { get; } = Array.Empty<string>();

    public bool AcceptsEntityArgument => true;

    public bool RequiresEntityArgument => true;

    public string? EntityArgumentHint => "dataset name";

    public async Task<IPreparedQuery> PrepareAsync(AqlQuery query, CancellationToken cancellationToken)
    {
        var datasetName = query.EntityArgument
            ?? throw new AqlValidationException("Dataset(...) requires a dataset name argument.");
        var dataset = await datasetStore.GetByNameAsync(datasetName, cancellationToken)
            ?? throw new AqlValidationException($"No dataset named '{datasetName}' exists.");
        var schema = BuildSchema(dataset);
        return new DatasetPreparedQuery(this, query, schema, dataset, executor, authorizer);
    }

    private static IReadOnlyList<QueryColumn> BuildSchema(Dataset dataset)
    {
        var columns = DatasetSchemaCodec.Decode(dataset.ColumnSchemaJson);
        return columns
            .Select(c => new QueryColumn(
                Name: c.Name,
                DataType: MapPostgresType(c.PostgresType),
                IsAggregable: c.PostgresType is "bigint" or "double precision" or "timestamptz",
                IsSystem: true))
            .ToList();
    }

    private static QueryDataType MapPostgresType(string pgType) => pgType switch
    {
        "bigint" or "double precision" => QueryDataType.Number,
        "boolean" => QueryDataType.Bool,
        "timestamptz" => QueryDataType.Date,
        _ => QueryDataType.String,
    };
}

internal sealed class DatasetPreparedQuery : IPreparedQuery
{
    private readonly Dataset _dataset;
    private readonly IDatasetExecutor _executor;
    private readonly IAuthorizer _authorizer;

    public DatasetPreparedQuery(
        IQueryEntity entity,
        AqlQuery query,
        IReadOnlyList<QueryColumn> schema,
        Dataset dataset,
        IDatasetExecutor executor,
        IAuthorizer authorizer)
    {
        Entity = entity;
        Query = query;
        Schema = schema;
        ValidationErrors = Array.Empty<string>();
        _dataset = dataset;
        _executor = executor;
        _authorizer = authorizer;
    }

    public IQueryEntity Entity { get; }
    public AqlQuery Query { get; }
    public IReadOnlyList<QueryColumn> Schema { get; }
    public IReadOnlyList<string> ValidationErrors { get; }

    public async Task<QueryResult> ExecuteAsync(
        ClaimsPrincipal actor,
        int? hardCap,
        CancellationToken cancellationToken)
    {
        // Curated-product gate: dataset:view:<id> alone is sufficient. The
        // underlying datastore/connector grants are not consulted, by design.
        var target = new EntityRef(EntityKinds.Dataset, _dataset.Id.ToString());
        var decision = await _authorizer.AuthorizeAsync(actor, Actions.View, target, cancellationToken);
        if (!decision.IsAllowed)
        {
            throw new AqlValidationException(
                $"You do not have view access to dataset '{_dataset.Name}'.");
        }
        return await _executor.ExecuteAsync(_dataset, Query, Schema, actor, hardCap, cancellationToken);
    }
}
