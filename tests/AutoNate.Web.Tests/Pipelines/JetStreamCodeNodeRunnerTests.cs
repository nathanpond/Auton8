using System.Text;
using System.Text.Json;
using AutoNate.Web.Configuration;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Nats;
using AutoNate.Web.Services.Pipelines;
using AutoNate.Web.Services.Pipelines.Execution;
using AutoNate.Web.Services.Transformers.Code;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Xunit;

namespace AutoNate.Web.Tests.Pipelines;

/// <summary>
/// Regression guard for #141. Runs against the real NATS in the test infra
/// (same server the app provisions streams on). A throw-away JetStream stream
/// captures <c>pipeline-code-run.&gt;</c> so the server answers every request
/// with a PubAck first — the exact condition that made the old
/// <c>RequestAsync</c>-based runner parse the ack as a failed reply.
/// </summary>
public sealed class JetStreamCodeNodeRunnerTests : IAsyncLifetime
{
    private const string NatsUrl = "nats://127.0.0.1:4222";
    private const string TestStreamName = "test-pipeline-code-run-acks";

    private NatsConnection? _nats;
    private bool _createdStream;

    public async Task InitializeAsync()
    {
        _nats = new NatsConnection(new NatsOpts { Url = NatsUrl });
        await _nats.ConnectAsync();
        var js = new NatsJSContext(_nats);
        try
        {
            await js.CreateStreamAsync(new StreamConfig(TestStreamName, ["pipeline-code-run.>"])
            {
                MaxAge = TimeSpan.FromMinutes(5)
            });
            _createdStream = true;
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 400)
        {
            // Subjects overlap an existing stream (e.g. the legacy
            // `pipeline-code-runs` on a dev server the app hasn't restarted on
            // yet). That stream produces the same PubAck, so the scenario still
            // holds; just don't try to delete someone else's stream later.
        }
    }

    public async Task DisposeAsync()
    {
        if (_nats is null) return;
        if (_createdStream)
        {
            try { await new NatsJSContext(_nats).DeleteStreamAsync(TestStreamName); } catch { /* best effort */ }
        }
        await _nats.DisposeAsync();
    }

    [Fact]
    public async Task RunCodeAsync_ReturnsTheSidecarReply_WhenJetStreamAcksTheRequestFirst()
    {
        var runId = Guid.NewGuid();
        var marker = "ok-" + runId.ToString("N")[..8];

        // Fake executor: a plain (non-queue) subscriber so it answers even when
        // the real compose-managed sidecar is also listening — both produce the
        // same output for this code, so whichever answers first is correct.
        var executorReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var executorCts = new CancellationTokenSource();
        var executor = Task.Run(async () =>
        {
            await using var sub = await _nats!.SubscribeCoreAsync<byte[]>(
                $"pipeline-code-run.{runId:N}.>", cancellationToken: executorCts.Token);
            executorReady.SetResult();
            await foreach (var msg in sub.Msgs.ReadAllAsync(executorCts.Token))
            {
                // Let the PubAck win the race deliberately.
                await Task.Delay(50, executorCts.Token);
                var reply = new CodeNodeReply(
                    Success: true,
                    ErrorMessage: null,
                    Output: new CodeNodeFrame([new CodeNodeColumn("marker", 0)], [new Dictionary<string, object?> { ["marker"] = marker }]));
                await msg.ReplyAsync(JsonSerializer.SerializeToUtf8Bytes(reply, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            }
        }, executorCts.Token);
        await executorReady.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var runner = new JetStreamCodeNodeRunner(
            new NatsConnectionProvider(Options.Create(new NatsOptions { Url = NatsUrl })),
            codeStore: null!,
            NullLogger<JetStreamCodeNodeRunner>.Instance);

        var node = new PipelineNode("node1", PipelineNodeKinds.Transformer, "custom", null, null);
        var code = new CodeTransformer
        {
            Id = Guid.NewGuid(), Name = "marker", Kind = "transformer", Language = "js",
            Code = $"function transform(inputs, config) {{ return [{{ marker: \"{marker}\" }}]; }}"
        };

        var frame = await runner.RunCodeAsync(runId, node, code, [], CancellationToken.None);

        executorCts.Cancel();
        Assert.NotNull(frame);
        var rows = frame!.Rows;
        Assert.Single(rows);
        Assert.Equal(marker, rows[0]["marker"]?.ToString());
    }

    [Theory]
    [InlineData("""{"stream":"pipeline-code-runs","seq":7}""", false)]
    [InlineData("""{"success":true,"errorMessage":null,"output":null}""", true)]
    [InlineData("""{"success":false,"errorMessage":"boom","output":null}""", true)]
    [InlineData("not json", false)]
    [InlineData("[]", false)]
    public void LooksLikeCodeNodeReply_DistinguishesRepliesFromAcks(string payload, bool expected)
    {
        Assert.Equal(expected, JetStreamCodeNodeRunner.LooksLikeCodeNodeReply(Encoding.UTF8.GetBytes(payload)));
    }
}
