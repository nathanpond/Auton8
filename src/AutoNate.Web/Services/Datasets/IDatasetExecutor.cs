using System.Security.Claims;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;

namespace AutoNate.Web.Services.Datasets;

// Single executor surface. The dataset's Mode + SourceKind decides routing:
//   Virtual + datastore(SQL) → SQL pushdown against ds_<datastoreid>.<table>
//   Virtual + datastore(File) → in-memory scan of datastore_files metadata
//   Virtual + dataconnector → rejected in PrepareAsync (REST/SMB belong in Cached)
//   Cached + any source → SQL pushdown against autonate_datastores.cache_<datasetid>
public interface IDatasetExecutor
{
    Task<QueryResult> ExecuteAsync(
        Dataset dataset,
        AqlQuery query,
        IReadOnlyList<QueryColumn> schema,
        ClaimsPrincipal actor,
        int? hardCap,
        CancellationToken cancellationToken);
}

public sealed class DatasetExecutionException(string message) : Exception(message);

public static class DatasetSourceKinds
{
    public const string DataStore = "datastore";
    public const string DataConnector = "dataconnector";
}
