using AutoNate.Plugins.Abstractions;
using Npgsql;

namespace AutoNate.Web.Plugins;

// Stand-in IPluginDataAccess for plugin rows that pre-date the data-storage
// feature (no `code` / no `role_password_encrypted`). Lets the plugin enable
// for hook-only use; any data call throws so the developer notices.
internal sealed class UnprovisionedPluginDataAccess : IPluginDataAccess
{
    private const string Message =
        "Plugin has no provisioned database schema. Re-upload the plugin so the host generates its 8-char code and per-plugin role.";

    public Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException(Message);

    public Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default) =>
        throw new InvalidOperationException(Message);

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param = null, CancellationToken ct = default) =>
        throw new InvalidOperationException(Message);

    public Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? param = null, CancellationToken ct = default) =>
        throw new InvalidOperationException(Message);
}
