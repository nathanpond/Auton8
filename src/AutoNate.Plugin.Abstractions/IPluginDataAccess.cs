using Npgsql;

namespace AutoNate.Plugins.Abstractions;

// Plugin-scoped data access. Connections opened through this interface are
// authenticated as the plugin's per-plugin Postgres role, so:
//   * INSERT/UPDATE/DELETE/CREATE/ALTER on the plugin's own schema succeed,
//   * SELECT on app tables and other plugins' schemas succeeds,
//   * any other write attempt is rejected by the database itself.
//
// The `search_path` of opened connections is `plg_<code>,public`, so unqualified
// references hit the plugin's own schema first and fall back to public for app
// tables. Cross-plugin reads must use fully-qualified names (`plg_<other>.t`).
//
// The async helpers are convenience wrappers over Dapper; for anything richer
// (scripts, COPY, prepared statements, etc.) call OpenConnectionAsync and use
// Npgsql/Dapper directly.
public interface IPluginDataAccess
{
    Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct = default);

    Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default);

    Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param = null, CancellationToken ct = default);

    Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? param = null, CancellationToken ct = default);
}
