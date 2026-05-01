using System.Collections.Concurrent;
using AutoNate.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace AutoNate.Web.Plugins;

// One per-plugin NpgsqlDataSource per loaded plugin. Built lazily on first
// access (typically inside Configure or a hook callback) and cached so
// connection pooling actually helps. Disposed when the plugin is disabled or
// deleted — the data source's pooled connections close, releasing any
// session-level state on the database side.
public sealed class PluginDataAccessRegistry : IAsyncDisposable
{
    private readonly string _baseConnectionString;
    private readonly PluginSchemaProvisioner _provisioner;
    private readonly ConcurrentDictionary<string, NpgsqlDataSource> _byCode = new(StringComparer.Ordinal);

    public PluginDataAccessRegistry(IConfiguration configuration, PluginSchemaProvisioner provisioner)
    {
        _baseConnectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is required for plugin data access.");
        _provisioner = provisioner;
    }

    public IPluginDataAccess GetOrCreate(string code, byte[] encryptedPassword)
    {
        var dataSource = _byCode.GetOrAdd(code, c => Build(c, encryptedPassword));
        return new PluginDataAccess(dataSource);
    }

    public NpgsqlDataSource GetDataSource(string code, byte[] encryptedPassword) =>
        _byCode.GetOrAdd(code, c => Build(c, encryptedPassword));

    public async Task RemoveAsync(string code)
    {
        if (_byCode.TryRemove(code, out var existing))
        {
            await existing.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (_, source) in _byCode)
        {
            await source.DisposeAsync();
        }
        _byCode.Clear();
    }

    private NpgsqlDataSource Build(string code, byte[] encryptedPassword)
    {
        var schema = PluginSchemaProvisioner.SchemaNameFor(code);
        var role = PluginSchemaProvisioner.RoleNameFor(code);
        var password = _provisioner.DecryptPassword(encryptedPassword);

        var builder = new NpgsqlConnectionStringBuilder(_baseConnectionString)
        {
            Username = role,
            Password = password,
            // Unqualified writes hit the plugin's own schema; unqualified reads
            // fall back to public for app tables. Cross-plugin reads must use
            // fully-qualified names by design.
            SearchPath = schema + ",public",
        };

        return new NpgsqlDataSourceBuilder(builder.ConnectionString).Build();
    }
}
