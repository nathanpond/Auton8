using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Pipelines.Execution;

// Wire format the host publishes to NATS and the `services/executor/`
// sidecar consumes. Versioned so a future Pyodide upgrade or input-shape
// extension can roll over without breaking older sidecar pods.
public sealed record class CodeNodeRequest(
    int Version,
    string NodeId,
    string Language,         // "js" | "python"
    string Kind,             // "transformer" | "analyzer"
    string Code,
    // Received by the sidecar and ignored — the full-CPython runner this was
    // meant to select was never built. Kept on the wire only because the
    // column and the permission still exist; see #190.
    bool IsUnsafe,
    IReadOnlyDictionary<string, string> Config,
    IReadOnlyList<CodeNodeFrame> Inputs,
    int TimeoutMs,
    int MemoryMb,
    // Process variables for a BPMN script task (#147). Null for the pipeline
    // kinds, whose data travels as `Inputs` frames instead.
    IReadOnlyDictionary<string, object?>? Variables = null);

public sealed record class CodeNodeFrame(
    IReadOnlyList<CodeNodeColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

public sealed record class CodeNodeColumn(string Name, int Type)
{
    // Wire-side mirror of DataColumnType so the sidecar doesn't need the
    // abstractions package to interpret the value. Keep the int mapping
    // identical (Text=0, Integer=1, Number=2, Boolean=3, Date=4, Json=5).
    public DataColumnType AsDataColumnType() => (DataColumnType)Type;
}

public sealed record class CodeNodeReply(
    bool Success,
    string? ErrorMessage,
    CodeNodeFrame? Output,
    // Script-task kinds only. Separate from Output because variable mutations
    // are not tabular; a CodeNodeFrame cannot carry a non-scalar variable
    // without misrepresenting its shape.
    ScriptTaskResult? ScriptTask = null);

// What a script task returns: the value backing `resultVariable`, and the
// variables the script wrote, to be applied to the execution by the caller.
public sealed record class ScriptTaskResult(
    object? Result,
    IReadOnlyDictionary<string, object?> Mutations);

internal static class CodeNodeWireFormat
{
    public const int CurrentVersion = 1;

    // BPMN script tasks. `kind` is "scripttask" and the executor replies with
    // mutations rather than a frame; see services/executor/src/scriptTaskRunner.ts.
    //
    // `language` is the executor's name for the runner ("js" | "python"), not
    // the BPMN `scriptFormat` the author wrote. ScriptSurfaceRules owns that
    // translation so the two vocabularies meet in exactly one place.
    public static CodeNodeRequest ForScriptTask(
        string nodeId,
        string code,
        string language,
        IReadOnlyDictionary<string, object?> variables,
        int timeoutMs,
        int memoryMb) =>
        new(CurrentVersion,
            nodeId,
            language,
            Kind: "scripttask",
            code,
            IsUnsafe: false,
            Config: new Dictionary<string, string>(StringComparer.Ordinal),
            Inputs: [],
            timeoutMs,
            memoryMb,
            variables);

    public static CodeNodeRequest From(
        string nodeId,
        string language,
        string kind,
        string code,
        bool isUnsafe,
        IReadOnlyDictionary<string, string> config,
        IReadOnlyList<DataFrame> inputs,
        int timeoutMs,
        int memoryMb)
    {
        var wireInputs = inputs
            .Select(frame => new CodeNodeFrame(
                frame.Columns.Select(c => new CodeNodeColumn(c.Name, (int)c.Type)).ToList(),
                frame.Rows))
            .ToList();
        return new CodeNodeRequest(
            CurrentVersion, nodeId, language, kind, code, isUnsafe,
            config, wireInputs, timeoutMs, memoryMb);
    }

    public static DataFrame ToDataFrame(CodeNodeFrame frame)
    {
        if (frame.Columns.Count == 0 && frame.Rows.Count == 0) return DataFrame.Empty;
        var columns = frame.Columns.Select(c => new DataColumn(c.Name, c.AsDataColumnType())).ToList();
        return new DataFrame(columns, frame.Rows);
    }
}
