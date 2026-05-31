using AutoNate.Web.Configuration;
using Microsoft.Extensions.Options;
using NATS.Client.Core;

namespace AutoNate.Web.Services.Nats;

// Single shared NATS connection for callers that don't already own one
// (Phase 6 of the Data Stores plan — JetStreamCodeNodeRunner). The
// provisioner and the system-health probe each open a short-lived
// connection because their flow is one-shot; the code-node runner is hot,
// so the connection is opened once and reused for every publish/reply
// cycle.
public interface INatsConnectionProvider
{
    Task<INatsConnection> GetAsync(CancellationToken cancellationToken = default);
}

internal sealed class NatsConnectionProvider(IOptions<NatsOptions> options) : INatsConnectionProvider, IAsyncDisposable
{
    private readonly NatsOptions _options = options.Value;
    private NatsConnection? _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<INatsConnection> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null) return _connection;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is null)
            {
                var conn = new NatsConnection(new NatsOpts { Url = _options.Url ?? string.Empty });
                await conn.ConnectAsync();
                _connection = conn;
            }
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
        _gate.Dispose();
    }
}
