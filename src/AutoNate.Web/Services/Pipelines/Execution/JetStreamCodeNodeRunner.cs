using System.Text;
using System.Text.Json;
using NATS.Client.Core;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Nats;
using AutoNate.Web.Services.Transformers.Code;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Services.Pipelines.Execution;

// Publishes a CodeNodeRequest to NATS subject `pipeline-code-run.<runId>.<nodeId>`
// and awaits the sidecar's reply on a per-call inbox. The `services/executor/`
// Node.js sidecar is the canonical subscriber (core NATS queue group
// `executor`); NATS correlates the reply via the inbox subject.
//
// Why not a plain RequestAsync: a request takes the FIRST message on the
// inbox, and if any JetStream stream captures the request subject the server
// answers first with a PubAck (`{"stream":…,"seq":…}`), which then parses as
// a failed CodeNodeReply while the sidecar's real answer is discarded (archived-141).
// The stream that used to do exactly that (`pipeline-code-runs`) is gone, but
// the runner stays defensive: it reads the inbox until it sees a message
// shaped like a CodeNodeReply.
//
// Timeouts are conservative: 30s wall-clock for the whole invocation, 128MB
// sidecar memory cap. Hard-coded today; lift to IOptions when an operator
// needs to tune them.
public sealed class JetStreamCodeNodeRunner(
    INatsConnectionProvider natsProvider,
    ICodeTransformerStore codeStore,
    ILogger<JetStreamCodeNodeRunner> log)
{
    // Hard-coded today; lift to IOptions when the operator wants to tune.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private const int DefaultMemoryMb = 128;

    public async Task<DataFrame?> RunCodeAsync(
        Guid pipelineRunId,
        PipelineNode pipelineNode,
        CodeTransformer codeRow,
        IReadOnlyList<DataFrame> inputs,
        CancellationToken cancellationToken)
    {
        var config = pipelineNode.Config ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var request = CodeNodeWireFormat.From(
            nodeId: pipelineNode.Id,
            language: codeRow.Language,
            kind: codeRow.Kind,
            code: codeRow.Code,
            isUnsafe: codeRow.IsUnsafe,
            config: config,
            inputs: inputs,
            timeoutMs: (int)DefaultTimeout.TotalMilliseconds,
            memoryMb: DefaultMemoryMb);

        var subject = $"pipeline-code-run.{pipelineRunId:N}.{pipelineNode.Id}";
        var payload = JsonSerializer.SerializeToUtf8Bytes(request, SerializerOptions);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultTimeout);

        try
        {
            log.LogDebug("Dispatching code-node {NodeId} for run {RunId} to executor sidecar.",
                pipelineNode.Id, pipelineRunId);
            var nats = await natsProvider.GetAsync(timeoutCts.Token);
            var inbox = nats.NewInbox();
            await using var replies = await nats.SubscribeCoreAsync<byte[]>(
                inbox, cancellationToken: timeoutCts.Token);
            await nats.PublishAsync(subject, payload, replyTo: inbox, cancellationToken: timeoutCts.Token);

            await foreach (var msg in replies.Msgs.ReadAllAsync(timeoutCts.Token))
            {
                if (msg.Data is null || msg.Data.Length == 0)
                {
                    throw new InvalidOperationException("Executor sidecar returned an empty reply.");
                }
                if (!LooksLikeCodeNodeReply(msg.Data))
                {
                    log.LogDebug(
                        "Ignoring non-CodeNodeReply message on inbox for node {NodeId} (JetStream ack?): {Payload}",
                        pipelineNode.Id, Encoding.UTF8.GetString(msg.Data));
                    continue;
                }
                var parsed = JsonSerializer.Deserialize<CodeNodeReply>(msg.Data, SerializerOptions)
                    ?? throw new InvalidOperationException("Executor sidecar reply could not be parsed.");
                if (!parsed.Success)
                {
                    throw new InvalidOperationException(
                        parsed.ErrorMessage ?? "Executor sidecar reported an unknown failure.");
                }
                return parsed.Output is null ? DataFrame.Empty : CodeNodeWireFormat.ToDataFrame(parsed.Output);
            }
            // The subscription only ends on cancellation, which the catch
            // blocks below translate; reaching here means the server closed it.
            throw new InvalidOperationException("Executor sidecar reply subscription ended without a reply.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Executor sidecar did not reply within {DefaultTimeout.TotalSeconds:N0}s.");
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    // A CodeNodeReply always carries `success`; a JetStream PubAck carries
    // `stream`/`seq` and nothing else. Anything without `success` is not ours.
    internal static bool LooksLikeCodeNodeReply(ReadOnlySpan<byte> payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload.ToArray());
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("success", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Resolve a code transformer by name. Convenience for the
    // Transformer/Analyzer fallthrough so the runners don't have to take
    // the store directly.
    public Task<CodeTransformer?> TryResolveAsync(string name, CancellationToken cancellationToken)
        => codeStore.GetByNameAsync(name, cancellationToken);
}
