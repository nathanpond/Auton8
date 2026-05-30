using System.Text.Json;

namespace AutoNate.Web.Services.Pipelines;

// In-memory DAG model for Phase 5 of the Data Stores plan. Persisted as
// `pipelines.graph` JSON; mirrored as `pipeline_runs.graph_snapshot` so a
// concurrent edit can't mutate an in-flight run. Node kinds are intentionally
// closed in v1 — adding a new kind is a registry entry on the host plus a
// node-runner implementation, not a graph-shape concern.
public sealed record class PipelineGraph(
    IReadOnlyList<PipelineNode> Nodes,
    IReadOnlyList<PipelineEdge> Edges)
{
    public static PipelineGraph Empty { get; } = new(Array.Empty<PipelineNode>(), Array.Empty<PipelineEdge>());

    public static PipelineGraph FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return Empty;
        var parsed = JsonSerializer.Deserialize<PipelineGraph>(json, Options);
        return parsed ?? Empty;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}

// "dataset-source" | "transformer" | "analyzer" | "dataset-sink".
public static class PipelineNodeKinds
{
    public const string DatasetSource = "dataset-source";
    public const string Transformer = "transformer";
    public const string Analyzer = "analyzer";
    public const string DatasetSink = "dataset-sink";
}

public sealed record class PipelineNode(
    string Id,
    string Kind,
    // For "dataset-source" / "dataset-sink": the dataset name to read/write.
    // For "transformer" / "analyzer": the registry key (e.g. "filter-rows").
    string Key,
    IReadOnlyDictionary<string, string>? Config,
    // Editor-only metadata; ignored by the orchestrator. Persisted so the
    // React Flow canvas can restore positions on reload.
    PipelineNodePosition? Position);

public sealed record class PipelineNodePosition(double X, double Y);

public sealed record class PipelineEdge(
    string Id,
    string Source,
    string Target);
