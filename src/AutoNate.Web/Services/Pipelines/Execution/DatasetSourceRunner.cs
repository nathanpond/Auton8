using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Datasets;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;

namespace AutoNate.Web.Services.Pipelines.Execution;

// Reads a dataset's full content by running an empty AQL query against it
// through the existing IDatasetExecutor. The dataset name lives in
// node.Key; node.Config can carry future projection / filter hints.
public sealed class DatasetSourceRunner(
    IDatasetStore datasetStore,
    IDatasetExecutor executor) : INodeRunner
{
    public string Kind => PipelineNodeKinds.DatasetSource;

    public async Task<DataFrame?> RunAsync(NodeRunnerContext context, CancellationToken cancellationToken = default)
    {
        var datasetName = context.Node.Key;
        if (string.IsNullOrWhiteSpace(datasetName))
            throw new InvalidOperationException(
                $"Pipeline node '{context.Node.Id}' (dataset-source) is missing a dataset name.");

        var dataset = await datasetStore.GetByNameAsync(datasetName, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Pipeline node '{context.Node.Id}' references dataset '{datasetName}', which does not exist.");

        var schema = DatasetSchemaCodec.Decode(dataset.ColumnSchemaJson);
        var queryColumns = schema.Select(c => new QueryColumn(
            c.Name,
            MapType(c.PostgresType),
            c.PostgresType is "bigint" or "double precision",
            true)).ToList();

        // Synthesize a bare `FROM Dataset("name")` AST so the executor's
        // routing matrix (Virtual vs Cached, SQL vs File vs Connector)
        // applies the same way it does from the AQL playground.
        var ast = new AqlQuery(
            Entity: "Dataset",
            Where: null,
            OrderBy: Array.Empty<AqlOrderItem>(),
            Columns: null,
            Group: null,
            Limit: null,
            EntityArgument: datasetName);

        var result = await executor.ExecuteAsync(dataset, ast, queryColumns, context.Actor, hardCap: null, cancellationToken);

        var columns = result.Columns.Select(c => new DataColumn(c.Name, MapResultType(c.DataType))).ToList();
        return new DataFrame(columns, result.Rows);
    }

    private static QueryDataType MapType(string pg) => pg switch
    {
        "bigint" or "double precision" => QueryDataType.Number,
        "boolean" => QueryDataType.Bool,
        "timestamptz" => QueryDataType.Date,
        _ => QueryDataType.String,
    };

    private static DataColumnType MapResultType(QueryDataType t) => t switch
    {
        QueryDataType.Number => DataColumnType.Number,
        QueryDataType.Bool => DataColumnType.Boolean,
        QueryDataType.Date => DataColumnType.Date,
        QueryDataType.Json => DataColumnType.Json,
        _ => DataColumnType.Text,
    };
}
