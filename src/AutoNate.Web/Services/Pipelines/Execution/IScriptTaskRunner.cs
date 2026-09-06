namespace AutoNate.Web.Services.Pipelines.Execution;

// The script-task half of JetStreamCodeNodeRunner, behind an interface (#147).
//
// Not indirection for its own sake: the endpoint's contract is mostly about
// which failure becomes which status code — a script error is 422 and an
// unreachable sandbox is 503 — and that distinction is exactly what a test
// needs to pin. Without a seam, asserting it would mean standing up NATS and
// the sidecar and then contriving each failure, which tests the infrastructure
// rather than the mapping.
public interface IScriptTaskRunner
{
    Task<ScriptTaskResult> RunScriptTaskAsync(
        string processInstanceId,
        string nodeId,
        string code,
        string language,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken cancellationToken);
}
