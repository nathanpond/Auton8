using AutoNate.Web.Services.Nats;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The startup probe #636 added: publish one message, count what the stream
/// stored. Runs against the real NATS in the test infra, like
/// <c>JetStreamCodeNodeRunnerTests</c>, on a throwaway stream of its own.
/// </summary>
/// <remarks>
/// A healthy stream stores one. The defect the probe exists for -- a stream
/// that stores each message four times -- could not be produced on a throwaway
/// stream by any configuration operation (identical updates, reorders,
/// remove/re-add cycles, live consumers); it is server state that only
/// recreating the stream has cleared. So this pins the healthy reading and the
/// arithmetic; the unhealthy reading was measured by hand on the shared server
/// and is recorded in #636.
/// </remarks>
public sealed class NatsStreamFidelityProbeTests : IAsyncLifetime
{
    private const string NatsUrl = "nats://127.0.0.1:4222";
    private readonly string _stream = $"n8fidelity{Guid.NewGuid():N}"[..20];
    private NatsConnection? _nats;

    public async Task InitializeAsync()
    {
        _nats = new NatsConnection(new NatsOpts { Url = NatsUrl });
        await _nats.ConnectAsync();
        await new NatsJSContext(_nats).CreateStreamAsync(new StreamConfig(_stream, [$"{_stream}.>"])
        {
            Storage = StreamConfigStorage.Memory
        });
    }

    public async Task DisposeAsync()
    {
        if (_nats is null) return;
        try { await new NatsJSContext(_nats).DeleteStreamAsync(_stream); } catch (NatsJSApiException) { }
        await _nats.DisposeAsync();
    }

    [Fact]
    public async Task A_healthy_stream_stores_one_copy_of_a_probe()
    {
        var js = new NatsJSContext(_nats!);

        var copies = await NatsStreamProvisioner.MeasureStorageFidelityAsync(js, _stream, $"{_stream}.probe", CancellationToken.None);

        Assert.Equal(1, copies);
    }

    /// <summary>
    /// The count is a DELTA, not a total: a second probe on the same subject
    /// still reads one, so the check cannot drift red as probes accumulate.
    /// </summary>
    [Fact]
    public async Task The_probe_measures_the_delta_not_the_subjects_total()
    {
        var js = new NatsJSContext(_nats!);
        await NatsStreamProvisioner.MeasureStorageFidelityAsync(js, _stream, $"{_stream}.probe", CancellationToken.None);
        await NatsStreamProvisioner.MeasureStorageFidelityAsync(js, _stream, $"{_stream}.probe", CancellationToken.None);

        var copies = await NatsStreamProvisioner.MeasureStorageFidelityAsync(js, _stream, $"{_stream}.probe", CancellationToken.None);

        Assert.Equal(1, copies);
    }
}
