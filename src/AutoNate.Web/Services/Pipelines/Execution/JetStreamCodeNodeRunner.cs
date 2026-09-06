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
    ILogger<JetStreamCodeNodeRunner> log) : IScriptTaskRunner
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
        log.LogDebug("Dispatching code-node {NodeId} for run {RunId} to executor sidecar.",
            pipelineNode.Id, pipelineRunId);
        var parsed = await RequestAsync(subject, request, pipelineNode.Id, cancellationToken);
        return parsed.Output is null ? DataFrame.Empty : CodeNodeWireFormat.ToDataFrame(parsed.Output);
    }

    // Runs a BPMN script task in the executor sandbox (#147).
    //
    // Fail-closed by construction: every failure path here throws, and the only
    // caller turns that into a retryable Flowable job. There is deliberately no
    // fallback — running the script anywhere else on a transport failure would
    // reintroduce GHSA-82rh-gjhw-rg9r exactly when the system is degraded.
    public async Task<ScriptTaskResult> RunScriptTaskAsync(
        string processInstanceId,
        string nodeId,
        string code,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken cancellationToken)
    {
        var request = CodeNodeWireFormat.ForScriptTask(
            nodeId: nodeId,
            code: code,
            variables: variables,
            timeoutMs: (int)DefaultTimeout.TotalMilliseconds,
            memoryMb: DefaultMemoryMb);

        // Same `pipeline-code-run.>` subject space the sidecar already
        // subscribes to, so script tasks need no new stream or queue group.
        var subject = $"pipeline-code-run.scripttask.{Sanitize(processInstanceId)}.{Sanitize(nodeId)}";
        log.LogDebug("Dispatching script task {NodeId} for process {ProcessInstanceId} to executor sidecar.",
            nodeId, processInstanceId);
        var parsed = await RequestAsync(subject, request, nodeId, cancellationToken);
        return parsed.ScriptTask
            ?? throw new InvalidOperationException(
                "Executor sidecar replied to a script task without a script-task result.");
    }

    // NATS subject tokens cannot contain '.', ' ', '*' or '>'. Flowable ids are
    // normally safe, but a node id comes from author-controlled BPMN, so it is
    // not trusted to be: an id containing '.' would silently widen the subject.
    private static string Sanitize(string token)
    {
        Span<char> buffer = stackalloc char[token.Length];
        for (var i = 0; i < token.Length; i++)
        {
            var c = token[i];
            buffer[i] = c is '.' or ' ' or '*' or '>' ? '_' : c;
        }
        return buffer.Length == 0 ? "_" : new string(buffer);
    }

    private async Task<CodeNodeReply> RequestAsync(
        string subject,
        CodeNodeRequest request,
        string nodeId,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(request, SerializerOptions);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultTimeout);

        try
        {
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
                        nodeId, Encoding.UTF8.GetString(msg.Data));
                    continue;
                }
                var parsed = JsonSerializer.Deserialize<CodeNodeReply>(msg.Data, SerializerOptions)
                    ?? throw new InvalidOperationException("Executor sidecar reply could not be parsed.");
                if (!parsed.Success)
                {
                    // The sidecar answered — the author's code is what failed.
                    // Distinct from the transport failures the catch blocks
                    // below produce, so a caller can tell a script error from
                    // an executor that was not there.
                    throw new ScriptExecutionException(
                        parsed.ErrorMessage ?? "Executor sidecar reported an unknown failure.");
                }
                return parsed;
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
