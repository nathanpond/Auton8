using AutoNate.Plugins.Abstractions;
using Dapper;
using Npgsql;

namespace AutoNate.Web.Plugins;

// Plugin-facing data API. Wraps a per-plugin NpgsqlDataSource (already
// authenticated as `plg_<code>` with search_path = `plg_<code>,public`) and
// exposes Dapper helpers so plugins don't need to take a Dapper dep
// themselves — Plugin.Abstractions ships it.
//
// Isolation is enforced at the database layer: even if a plugin manages to
// run arbitrary SQL through the raw `OpenConnectionAsync` accessor, Postgres
// will refuse any write to objects the role doesn't own.
internal sealed class PluginDataAccess : IPluginDataAccess
{
    private readonly NpgsqlDataSource _dataSource;

    public PluginDataAccess(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        var connection = _dataSource.CreateConnection();
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(sql, param, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param = null, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        var result = await connection.QueryAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct));
        return result.AsList();
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? param = null, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<T>(
            new CommandDefinition(sql, param, cancellationToken: ct));
    }
}
