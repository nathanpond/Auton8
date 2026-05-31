using System.Text.Json;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Nats;
using AutoNate.Web.Services.Transformers.Code;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Services.Pipelines.Execution;

// Publishes a CodeNodeRequest to NATS subject `pipeline-code-run.<runId>.<nodeId>`
// and awaits the sidecar's reply on a generated reply subject. The
// `services/executor/` Node.js sidecar is the canonical subscriber under a
// durable consumer named `executor`. The runner doesn't itself know the
// underlying request/reply implementation — NATS handles correlation via
// the per-call reply subject.
//
// Phase 6 v1 timeouts are conservative: 30s wall-clock for the whole
// invocation, 128MB sidecar memory cap. Both override-able via
// CodeNode:TimeoutMs / CodeNode:MemoryMb options.
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
            var reply = await nats.RequestAsync<byte[], byte[]>(
                subject,
                payload,
                cancellationToken: timeoutCts.Token);

            if (reply.Data is null)
            {
                throw new InvalidOperationException("Executor sidecar returned an empty reply.");
            }
            var parsed = JsonSerializer.Deserialize<CodeNodeReply>(reply.Data, SerializerOptions)
                ?? throw new InvalidOperationException("Executor sidecar reply could not be parsed.");
            if (!parsed.Success)
            {
                throw new InvalidOperationException(
                    parsed.ErrorMessage ?? "Executor sidecar reported an unknown failure.");
            }
            return parsed.Output is null ? DataFrame.Empty : CodeNodeWireFormat.ToDataFrame(parsed.Output);
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

    // Resolve a code transformer by name. Convenience for the
    // Transformer/Analyzer fallthrough so the runners don't have to take
    // the store directly.
    public Task<CodeTransformer?> TryResolveAsync(string name, CancellationToken cancellationToken)
        => codeStore.GetByNameAsync(name, cancellationToken);
}
