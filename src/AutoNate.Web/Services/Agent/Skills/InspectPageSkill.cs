using System.Text.Json;
using AutoNate.Web.Services.Agent.PageQuery;

namespace AutoNate.Web.Services.Agent.Skills;

// Generic page-awareness skill. Both tools are page-agnostic:
//   - inspect_page reads slices of the per-message snapshot the SPA bundled
//     with the user's message. Cheap; no network round-trip.
//   - query_page asks the live SPA for fresh or larger data the snapshot
//     deliberately omits or has truncated. Round-trips through the
//     IPageQueryChannel and the SSE stream.
//
// The skill itself never knows about workflows, records, etc. The contract
// the model sees is: "if a page snapshot is present, you can inspect it or
// ask the page for more." Page-specific shapes (topics, args) come from
// each page's registered context provider on the SPA side.
public sealed class InspectPageSkill : IAgentSkill
{
    public const string InspectToolName = "inspect_page";
    public const string QueryToolName = "query_page";
    public const string ApplyActionToolName = "apply_page_action";

    // Cap on the JSON we hand back to the model. The snapshot itself is
    // capped at 64KB on the way in, but per-topic slices are typically much
    // smaller; this is a defensive ceiling for unusually large slices and
    // for query_page replies. When exceeded, the result includes a
    // _truncated marker the model can read.
    private const int MaxResultBytes = 32 * 1024;

    private readonly IPageQueryChannel _pageQueryChannel;
    private readonly IPageActionChannel _pageActionChannel;

    public InspectPageSkill(IPageQueryChannel pageQueryChannel, IPageActionChannel pageActionChannel)
    {
        _pageQueryChannel = pageQueryChannel;
        _pageActionChannel = pageActionChannel;
        Tools = new[]
        {
            new AgentTool(
                Name: InspectToolName,
                Description: "Read a slice of the user's current page snapshot. The snapshot reflects the live state of whatever page the user has open (including unsaved edits). Pass an optional 'topic' to drill in by dotted path (e.g. 'selection', 'selection.elements', 'workflow'). Omit topic to discover top-level keys. Returns 'no_snapshot' when no page snapshot was bundled with this message.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "topic": {
                          "type": "string",
                          "description": "Optional dotted-path slice of the snapshot data. Omit for an overview of top-level keys."
                        }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeInspectAsync),

            new AgentTool(
                Name: QueryToolName,
                Description: "Ask the user's page for fresh or extra data the snapshot doesn't carry. Use when you need data that's likely to have changed since the user sent their message, or data the snapshot deliberately omits (e.g. canonical XML, full property bodies). Topic and args are page-specific — the page snapshot's structure hints at what topics are available. Returns 'page_unreachable' if the user navigated away or the page can't answer.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "topic": {
                          "type": "string",
                          "description": "Page-specific topic to query (e.g. 'bpmn.xml', 'node.byId')."
                        },
                        "args": {
                          "type": "object",
                          "description": "Optional topic-specific arguments (e.g. { id: 'UserTask_3' }).",
                          "additionalProperties": true
                        }
                      },
                      "required": ["topic"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeQueryAsync),

            new AgentTool(
                Name: ApplyActionToolName,
                Description: "Mutate the user's current page (in-memory only — the user must still save afterward). Available actions are listed in the page snapshot under data.actions; built-in 'set_form_field' and 'submit_form' work on any page with forms. ALWAYS call first with confirmed=false to acknowledge the action and outline the change in your reply, then ask the user to confirm. Only call again with confirmed=true after the user agrees in the chat. The first call performs no mutation; the second one performs the change. Returns 'page_unreachable' if the user navigated away.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "action": {
                          "type": "string",
                          "description": "Action name from the page's actions list (e.g. 'update_node', 'replace_diagram_xml', 'set_form_field')."
                        },
                        "args": {
                          "type": "object",
                          "description": "Action-specific arguments. Schema is described in the action's listing.",
                          "additionalProperties": true
                        },
                        "confirmed": {
                          "type": "boolean",
                          "description": "Default false. Set to true only after the user has explicitly agreed in chat to the change you described."
                        }
                      },
                      "required": ["action"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeApplyActionAsync)
        };
    }

    public string Name => "page-awareness";

    public string Description => "Read structured live state from the user's current page (snapshot + on-demand queries).";

    public IReadOnlyList<AgentTool> Tools { get; }

    public string? SystemPromptFragment(AgentSessionContext context)
    {
        // Only advertise the affordance when a snapshot is actually present.
        // Without one, the tools self-degrade but there's no point biasing
        // the model toward calling them.
        if (context.PageContext is null) return null;
        return "When the user asks about something visible on their current page (a selected node, the record being viewed, a list they can see), prefer inspect_page first. Fall through to query_page when you need fresh data or fields the snapshot doesn't carry. To mutate the page, use apply_page_action — but ALWAYS describe what you'll change and wait for the user to agree (call once with confirmed=false to acknowledge, then again with confirmed=true after they agree). Mutations only change the page's in-memory state; the user must save manually.";
    }

    private Task<JsonElement> InvokeInspectAsync(JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var snapshot = context.Session.PageContext;
        if (snapshot is null)
        {
            return Task.FromResult(Error("no_snapshot", "No page snapshot was bundled with this message."));
        }

        string? topic = null;
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty("topic", out var topicProp) &&
            topicProp.ValueKind == JsonValueKind.String)
        {
            topic = topicProp.GetString();
        }

        if (string.IsNullOrWhiteSpace(topic))
        {
            // Overview: top-level keys + summary metadata, no data body.
            var keys = snapshot.Data.ValueKind == JsonValueKind.Object
                ? snapshot.Data.EnumerateObject().Select(p => p.Name).ToArray()
                : Array.Empty<string>();
            return Task.FromResult(Ok(new
            {
                pageKey = snapshot.PageKey,
                schemaVersion = snapshot.SchemaVersion,
                version = snapshot.Version,
                summary = snapshot.Summary,
                dataKeys = keys
            }));
        }

        if (!TryWalkDottedPath(snapshot.Data, topic!, out var node))
        {
            return Task.FromResult(Error("topic_not_found", $"Topic '{topic}' was not present in the snapshot. Call inspect_page with no topic to see available top-level keys."));
        }

        return Task.FromResult(EnvelopeWithCap(node, kind: "inspect_page_result"));
    }

    private async Task<JsonElement> InvokeApplyActionAsync(JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("action", out var actionProp) ||
            actionProp.ValueKind != JsonValueKind.String)
        {
            return Error("bad_request", "action is required.");
        }
        var actionName = actionProp.GetString() ?? string.Empty;

        JsonElement? actionArgs = null;
        if (args.TryGetProperty("args", out var aProp) && aProp.ValueKind != JsonValueKind.Null)
        {
            actionArgs = aProp;
        }

        bool confirmed = false;
        if (args.TryGetProperty("confirmed", out var cProp) && cProp.ValueKind == JsonValueKind.True)
        {
            confirmed = true;
        }

        if (!confirmed)
        {
            // First call from the model: do nothing, just hand back a structured
            // "needs confirmation" envelope. The model is expected to summarise
            // the change in chat and ask the user to confirm. After the user
            // agrees, the model calls again with confirmed=true.
            return JsonSerializer.SerializeToElement(new
            {
                kind = "page_action_proposal",
                source = nameof(InspectPageSkill),
                data = new
                {
                    action = actionName,
                    args = actionArgs,
                    confirmed = false,
                    nextStep = "Describe the change to the user in chat (what will change, scope, any preconditions). Wait for the user to agree, then call apply_page_action again with the same action and args plus confirmed=true."
                }
            });
        }

        var result = await _pageActionChannel.ApplyAsync(actionName, actionArgs, ct).ConfigureAwait(false);
        return result switch
        {
            PageActionResult.Success ok => JsonSerializer.SerializeToElement(new
            {
                kind = "page_action_applied",
                source = nameof(InspectPageSkill),
                data = new { action = actionName, summary = ok.Summary, changes = ok.Changes }
            }),
            PageActionResult.Failure fail => Error(fail.ErrorCode, fail.Message ?? fail.ErrorCode),
            _ => Error("unknown", "Unexpected page-action result.")
        };
    }

    private async Task<JsonElement> InvokeQueryAsync(JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("topic", out var topicProp) ||
            topicProp.ValueKind != JsonValueKind.String)
        {
            return Error("bad_request", "topic is required.");
        }
        var topic = topicProp.GetString() ?? string.Empty;

        JsonElement? queryArgs = null;
        if (args.TryGetProperty("args", out var aProp) && aProp.ValueKind != JsonValueKind.Null)
        {
            queryArgs = aProp;
        }

        var result = await _pageQueryChannel.AskAsync(topic, queryArgs, ct).ConfigureAwait(false);
        return result switch
        {
            PageQueryResult.Success ok => EnvelopeWithCap(ok.Data, kind: "query_page_result"),
            PageQueryResult.Failure fail => Error(fail.ErrorCode, fail.Message ?? fail.ErrorCode),
            _ => Error("unknown", "Unexpected page-query result.")
        };
    }

    // Walk a dotted path through a JsonElement tree. "selection.elements.0"
    // means: snapshot.data.selection.elements[0]. Numeric segments index
    // arrays. Returns false if any segment is missing.
    private static bool TryWalkDottedPath(JsonElement root, string path, out JsonElement node)
    {
        node = root;
        if (string.IsNullOrEmpty(path)) return true;
        var segments = path.Split('.');
        foreach (var seg in segments)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (!node.TryGetProperty(seg, out var next))
                {
                    node = default;
                    return false;
                }
                node = next;
            }
            else if (node.ValueKind == JsonValueKind.Array && int.TryParse(seg, out var idx))
            {
                if (idx < 0 || idx >= node.GetArrayLength())
                {
                    node = default;
                    return false;
                }
                node = node[idx];
            }
            else
            {
                node = default;
                return false;
            }
        }
        return true;
    }

    // Wraps a payload in the standard tool-result envelope and applies the
    // size cap. If the rendered JSON exceeds the cap, returns a small marker
    // object instead of the raw value so the model can choose to call back
    // with a narrower topic.
    private static JsonElement EnvelopeWithCap(JsonElement data, string kind)
    {
        try
        {
            var raw = data.GetRawText();
            if (raw.Length > MaxResultBytes)
            {
                return JsonSerializer.SerializeToElement(new
                {
                    kind,
                    source = nameof(InspectPageSkill),
                    data = new { _truncated = true, _sizeBytes = raw.Length },
                    message = $"Result exceeds {MaxResultBytes} bytes; call back with a narrower topic."
                });
            }
        }
        catch (InvalidOperationException) { /* ignore — emit verbatim */ }

        return JsonSerializer.SerializeToElement(new
        {
            kind,
            source = nameof(InspectPageSkill),
            data
        });
    }

    private static JsonElement Ok(object payload) =>
        JsonSerializer.SerializeToElement(new
        {
            kind = "inspect_page_result",
            source = nameof(InspectPageSkill),
            data = payload
        });

    private static JsonElement Error(string code, string message) =>
        JsonSerializer.SerializeToElement(new
        {
            kind = "error",
            source = nameof(InspectPageSkill),
            data = new { code, message }
        });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
