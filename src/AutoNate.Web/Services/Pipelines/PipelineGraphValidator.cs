namespace AutoNate.Web.Services.Pipelines;

// Pre-execution graph checks (Phase 5 of the Data Stores plan). Topological
// sort + cycle detection live here so the orchestrator can rely on the
// returned ordering; schema-flow type checks across nodes are a follow-up
// (every transformer today accepts any DataFrame regardless of upstream
// column shape).
public sealed class PipelineGraphValidationException(string message) : Exception(message);

public static class PipelineGraphValidator
{
    // Returns a deterministic topological order with the caller's source
    // nodes first. Throws PipelineGraphValidationException for cycles,
    // dangling edges, duplicate node ids, or unknown node kinds.
    public static IReadOnlyList<PipelineNode> TopologicalSort(PipelineGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (graph.Nodes.Count == 0) return Array.Empty<PipelineNode>();

        var byId = new Dictionary<string, PipelineNode>(StringComparer.Ordinal);
        foreach (var n in graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(n.Id))
                throw new PipelineGraphValidationException("Pipeline node is missing an id.");
            if (!byId.TryAdd(n.Id, n))
                throw new PipelineGraphValidationException($"Duplicate node id '{n.Id}'.");
            ValidateKind(n);
        }
        var inDegree = byId.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var outgoing = byId.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var e in graph.Edges)
        {
            if (!byId.ContainsKey(e.Source))
                throw new PipelineGraphValidationException($"Edge source '{e.Source}' not found in nodes.");
            if (!byId.ContainsKey(e.Target))
                throw new PipelineGraphValidationException($"Edge target '{e.Target}' not found in nodes.");
            outgoing[e.Source].Add(e.Target);
            inDegree[e.Target]++;
        }

        // Kahn's algorithm with stable ordering: each step picks the node id
        // with the lexicographically smallest id among the in-degree-zero
        // frontier so the orchestrator's iteration is reproducible across
        // runs of the same graph.
        var ordered = new List<PipelineNode>(byId.Count);
        var frontier = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (id, deg) in inDegree)
        {
            if (deg == 0) frontier.Add(id);
        }
        while (frontier.Count > 0)
        {
            var nextId = frontier.Min!;
            frontier.Remove(nextId);
            ordered.Add(byId[nextId]);
            foreach (var child in outgoing[nextId])
            {
                if (--inDegree[child] == 0) frontier.Add(child);
            }
        }
        if (ordered.Count != byId.Count)
        {
            throw new PipelineGraphValidationException(
                "Pipeline graph contains a cycle; every DAG must be acyclic.");
        }
        return ordered;
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ResolveUpstreamMap(PipelineGraph graph)
    {
        var dict = graph.Nodes.ToDictionary(
            n => n.Id,
            _ => (IReadOnlyList<string>)new List<string>(),
            StringComparer.Ordinal);
        foreach (var e in graph.Edges)
        {
            ((List<string>)dict[e.Target]).Add(e.Source);
        }
        return dict;
    }

    private static void ValidateKind(PipelineNode node)
    {
        switch (node.Kind)
        {
            case PipelineNodeKinds.DatasetSource:
            case PipelineNodeKinds.Transformer:
            case PipelineNodeKinds.Analyzer:
            case PipelineNodeKinds.DatasetSink:
                return;
            default:
                throw new PipelineGraphValidationException(
                    $"Unknown node kind '{node.Kind}' on node '{node.Id}'. " +
                    $"Allowed: {PipelineNodeKinds.DatasetSource}, {PipelineNodeKinds.Transformer}, " +
                    $"{PipelineNodeKinds.Analyzer}, {PipelineNodeKinds.DatasetSink}.");
        }
    }
}
