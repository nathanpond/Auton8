using System.Xml.Linq;

namespace AutoNate.Web.Services.Workflow;

// Which identity a script task runs as, and whether that can be determined
// from the diagram (#153).
//
// The model was settled in planning (.n8/decisions.md, Ad-hoc 2026-09-05):
//
//   * default        — the assignee of the last user task on the token's own
//                      path;
//   * system         — bypasses individual permission checks; requires a
//                      permission most authors will not have. It never
//                      bypasses the sandbox;
//   * workflowAuthor — the author of the definition.
//
// Publishing fails when the default cannot resolve: a script task reachable
// with no preceding user task, or one downstream of a parallel join where "the
// last user task" is ambiguous. The author must then say which identity they
// mean.
//
// Nothing here resolves an identity at runtime. At v1.0 the host API exposes
// only process variables, so there is nothing to authorize; this stores and
// validates the author's declaration so that adding enforcement later does not
// mean migrating every diagram that already exists.
public static class ScriptTaskIdentity
{
    public static readonly XNamespace AutoNateNamespace = "http://autonate.dev/workflows";
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    public const string RunAsAttribute = "runAs";
    public const string System = "system";
    public const string WorkflowAuthor = "workflowAuthor";

    /// <summary>Reads a script task's declared identity, or null when unset.</summary>
    public static string? ReadRunAs(XElement scriptTask) =>
        Normalise(scriptTask.Attribute(AutoNateNamespace + RunAsAttribute)?.Value
                  ?? scriptTask.Attribute(RunAsAttribute)?.Value);

    private static string? Normalise(string? value) => value switch
    {
        null or "" => null,
        _ when string.Equals(value, System, StringComparison.OrdinalIgnoreCase) => System,
        _ when string.Equals(value, WorkflowAuthor, StringComparison.OrdinalIgnoreCase) => WorkflowAuthor,
        _ => value,
    };

    /// <summary>Does any script task in this document ask to run as the system?</summary>
    /// <remarks>
    /// Drives the server-side permission check on publish. The studio hides the
    /// option from an author who lacks the permission, but a hidden control is
    /// not a gate — the check has to happen where the XML arrives.
    /// </remarks>
    public static bool DeclaresSystemIdentity(XDocument document) =>
        document.Descendants(Bpmn + "scriptTask").Any(t => ReadRunAs(t) == System);

    /// <summary>Every script task's declared identity, keyed by element id.</summary>
    public static IReadOnlyDictionary<string, string> DeclaredIdentities(XDocument document) =>
        document.Descendants(Bpmn + "scriptTask")
            .Where(t => t.Attribute("id") is not null && ReadRunAs(t) is not null)
            .ToDictionary(t => t.Attribute("id")!.Value, t => ReadRunAs(t)!, StringComparer.Ordinal);

    // --- the analysis -----------------------------------------------------

    private sealed class Node
    {
        public required string Id { get; init; }
        public required string LocalName { get; init; }
        public List<string> Next { get; } = [];
        /// <summary>
        /// Edges that must not count this node as a *completed* user task —
        /// a boundary event fires while the task is still running, so its
        /// assignee has not completed anything.
        /// </summary>
        public List<string> InterruptedNext { get; } = [];
        public int IncomingCount { get; set; }
        public bool IsStart { get; set; }
    }

    private sealed record Facts(bool AllPathsHaveUserTask, bool AnyPathCrossesJoin);

    /// <summary>
    /// Errors for script tasks whose identity cannot be determined and which do
    /// not say what they mean.
    /// </summary>
    public static IReadOnlyList<string> BuildIdentityValidationErrors(XDocument document)
    {
        var errors = new List<string>();

        // Each process and each embedded subprocess is its own flow scope: a
        // sequence flow never crosses the boundary, so analysing them together
        // would connect nodes that cannot reach each other.
        foreach (var scope in Scopes(document))
        {
            var nodes = BuildGraph(scope);
            if (nodes.Count == 0) continue;
            var reachable = Reachable(nodes);
            var facts = Solve(nodes);

            foreach (var scriptTask in scope.Elements(Bpmn + "scriptTask"))
            {
                var id = scriptTask.Attribute("id")?.Value;
                if (id is null || ReadRunAs(scriptTask) is not null) continue;
                // Unreachable from any start event, so it can never run.
                if (!reachable.Contains(id)) continue;
                if (!facts.TryGetValue(id, out var f)) continue;

                var label = scriptTask.Attribute("name")?.Value ?? id;
                if (!f.AllPathsHaveUserTask)
                {
                    errors.Add(
                        $"Script task '{label}' can be reached without a preceding user task, so " +
                        "there is no assignee whose permissions it would run with. Set Run as to " +
                        "'System' (requires permission) or 'Workflow author'.");
                }
                else if (f.AnyPathCrossesJoin)
                {
                    errors.Add(
                        $"Script task '{label}' runs after a parallel join, where 'the last user " +
                        "task' is ambiguous — more than one branch reaches it. Set Run as to " +
                        "'System' (requires permission) or 'Workflow author'.");
                }
            }
        }

        return errors;
    }

    // A scope is a process or an embedded subProcess. Nested subprocesses are
    // returned too, and each is analysed with its own start events.
    private static IEnumerable<XElement> Scopes(XDocument document)
    {
        foreach (var process in document.Descendants(Bpmn + "process")) yield return process;
        foreach (var sub in document.Descendants(Bpmn + "subProcess")) yield return sub;
        foreach (var sub in document.Descendants(Bpmn + "transaction")) yield return sub;
    }

    private static Dictionary<string, Node> BuildGraph(XElement scope)
    {
        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);

        foreach (var element in scope.Elements())
        {
            var id = element.Attribute("id")?.Value;
            if (id is null || element.Name.Namespace != Bpmn) continue;
            if (element.Name.LocalName is "sequenceFlow" or "extensionElements" or "laneSet") continue;
            nodes[id] = new Node { Id = id, LocalName = element.Name.LocalName };
        }

        foreach (var flow in scope.Elements(Bpmn + "sequenceFlow"))
        {
            var from = flow.Attribute("sourceRef")?.Value;
            var to = flow.Attribute("targetRef")?.Value;
            if (from is null || to is null) continue;
            if (!nodes.TryGetValue(from, out var source) || !nodes.ContainsKey(to)) continue;
            source.Next.Add(to);
            nodes[to].IncomingCount++;
        }

        // Boundary events: the token arrives from the task they are attached
        // to, but that task did not complete, so its assignee cannot be "the
        // last user task". Modelled as an edge that carries the state *before*
        // the attached node rather than after it.
        foreach (var boundary in scope.Elements(Bpmn + "boundaryEvent"))
        {
            var id = boundary.Attribute("id")?.Value;
            var attachedTo = boundary.Attribute("attachedToRef")?.Value;
            if (id is null || attachedTo is null) continue;
            if (!nodes.TryGetValue(attachedTo, out var host) || !nodes.ContainsKey(id)) continue;
            host.InterruptedNext.Add(id);
            nodes[id].IncomingCount++;
        }

        // Only a real start event begins a path. A node with no incoming flow
        // is not a start — it is unreachable, and a script task that can never
        // execute needs no identity. Treating those as starts flagged every
        // disconnected fragment, which is a false positive of exactly the kind
        // that leaves an author unable to publish with no way to comply.
        foreach (var node in nodes.Values)
        {
            node.IsStart = node.LocalName is "startEvent";
        }

        return nodes;
    }

    // Everything a token can actually get to from a start event, following
    // sequence flows and boundary-event edges.
    private static HashSet<string> Reachable(Dictionary<string, Node> nodes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(nodes.Values.Where(n => n.IsStart).Select(n => n.Id));
        foreach (var id in queue) seen.Add(id);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!nodes.TryGetValue(current, out var node)) continue;
            foreach (var next in node.Next.Concat(node.InterruptedNext))
            {
                if (seen.Add(next)) queue.Enqueue(next);
            }
        }
        return seen;
    }

    // Two dataflow analyses over the same graph, run to a fixpoint so loops
    // terminate.
    //
    //   AllPathsHaveUserTask — a "must" property, so paths are combined with
    //     AND and unvisited nodes start optimistic (true), converging downward.
    //   AnyPathCrossesJoin   — a "may" property: OR, starting false.
    private static Dictionary<string, Facts> Solve(Dictionary<string, Node> nodes)
    {
        var allUser = nodes.Keys.ToDictionary(k => k, _ => true, StringComparer.Ordinal);
        var anyJoin = nodes.Keys.ToDictionary(k => k, _ => false, StringComparer.Ordinal);

        foreach (var node in nodes.Values.Where(n => n.IsStart))
        {
            allUser[node.Id] = false;
        }

        // Bounded: each iteration can only flip values one way, and the lattice
        // is finite, so this settles. The cap is belt-and-braces against a
        // malformed graph rather than an expected path.
        for (var round = 0; round < nodes.Count + 2; round++)
        {
            var changed = false;

            foreach (var node in nodes.Values)
            {
                foreach (var (targetId, completed) in
                         node.Next.Select(n => (n, true)).Concat(node.InterruptedNext.Select(n => (n, false))))
                {
                    if (!nodes.TryGetValue(targetId, out var target)) continue;

                    // A call activity runs a process this document cannot see,
                    // so whether it contained a user task is unknowable. Not
                    // counting it is the conservative direction: the author is
                    // asked to be explicit rather than being given a
                    // permissive answer that might be wrong.
                    var outAllUser = allUser[node.Id]
                        || (completed && node.LocalName == "userTask");
                    var outAnyJoin = anyJoin[node.Id] || IsJoin(node);

                    if (!target.IsStart && allUser[targetId] && !outAllUser)
                    {
                        allUser[targetId] = false;
                        changed = true;
                    }
                    if (!anyJoin[targetId] && outAnyJoin)
                    {
                        anyJoin[targetId] = true;
                        changed = true;
                    }
                }
            }

            if (!changed) break;
        }

        return nodes.Keys.ToDictionary(
            k => k,
            k => new Facts(allUser[k], anyJoin[k]),
            StringComparer.Ordinal);
    }

    // A join is a gateway merging more than one incoming branch. After one,
    // several branches have run and "the last user task" has no single answer.
    private static bool IsJoin(Node node) =>
        node.IncomingCount > 1
        && node.LocalName is "parallelGateway" or "inclusiveGateway" or "complexGateway";
}
