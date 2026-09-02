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
        // Volatile so the fast path can't observe a partially-published
        // NatsConnection written by another thread inside the gate (archived-78);
        // AgentModelCatalog.GetOrLoad uses the same shape.
        var existing = Volatile.Read(ref _connection);
        if (IsUsable(existing)) return existing!;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            existing = Volatile.Read(ref _connection);
            if (IsUsable(existing)) return existing!;

            // A connection that has terminally closed is worse than none: it
            // is reused for every subsequent code-node run until the process
            // restarts. Drop it and reconnect instead.
            if (existing is not null)
            {
                try { await existing.DisposeAsync(); } catch { /* already broken */ }
                Volatile.Write(ref _connection, null);
            }

            var conn = new NatsConnection(new NatsOpts { Url = _options.Url ?? string.Empty });
            // NatsConnection.ConnectAsync takes no CancellationToken in this
            // NATS.Net version, so honour the caller's token around it rather
            // than letting a hung connect ignore it entirely (archived-78).
            await conn.ConnectAsync().AsTask().WaitAsync(cancellationToken);
            Volatile.Write(ref _connection, conn);
            return conn;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsUsable(NatsConnection? connection) =>
        connection is not null && connection.ConnectionState != NatsConnectionState.Closed;

    public async ValueTask DisposeAsync()
    {
        var connection = Volatile.Read(ref _connection);
        if (connection is not null)
        {
            await connection.DisposeAsync();
            Volatile.Write(ref _connection, null);
        }
        _gate.Dispose();
    }
}
