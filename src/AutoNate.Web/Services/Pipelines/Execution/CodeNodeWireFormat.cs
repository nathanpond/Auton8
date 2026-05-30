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
    bool IsUnsafe,
    IReadOnlyDictionary<string, string> Config,
    IReadOnlyList<CodeNodeFrame> Inputs,
    int TimeoutMs,
    int MemoryMb);

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
    CodeNodeFrame? Output);

internal static class CodeNodeWireFormat
{
    public const int CurrentVersion = 1;

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
