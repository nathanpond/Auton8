using Microsoft.Extensions.Configuration;
using Npgsql;

namespace AutoNate.Web.Services.DataStores.Sql;

// Tiny wrapper around the `Datastores` connection string. Services that
// need to talk to the second cluster DB take this instead of reading
// IConfiguration directly so we can centralize feature-disabled checks
// (IsEnabled) and connection lifecycle.
public interface IDatastoresConnectionFactory
{
    bool IsEnabled { get; }

    // Throws if IsEnabled is false. Callers gate on IsEnabled first and
    // surface a 503 to API consumers.
    Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default);
}

public sealed class DatastoresConnectionFactory(IConfiguration configuration) : IDatastoresConnectionFactory
{
    private readonly string? _connectionString = configuration.GetConnectionString("Datastores");

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_connectionString);

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException(
                "SqlType DataStores feature is disabled — ConnectionStrings:Datastores is not configured.");
        }
        var conn = new NpgsqlConnection(_connectionString);
        try
        {
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }
}
