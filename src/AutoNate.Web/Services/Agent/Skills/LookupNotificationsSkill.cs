using System.Text.Json;
using AutoNate.Web.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Read-only inbox tools for the current user. Notification records belong to
// the user they were addressed to — there is no per-row authorization check
// because the store scopes every list/get by the session's UserId. The Phase 3
// SendNotificationsSkill will add write capabilities (send_notification,
// mark_notification_read) with the same scoping.
public sealed class LookupNotificationsSkill : IAgentSkill
{
    public string Name => "lookup-notifications";

    public string Description =>
        "Read the current user's notification inbox.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public LookupNotificationsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "list_notifications",
                Description: "List the current user's notifications, newest first. Use unreadOnly to filter to unread.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "unreadOnly": { "type": "boolean", "description": "Defaults to false." },
                        "search": { "type": "string", "description": "Optional free text matched against title/body." },
                        "take": { "type": "integer", "minimum": 1, "maximum": 100 }
                      },
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeListAsync),

            new AgentTool(
                Name: "get_unread_count",
                Description: "Return the current user's unread notification count.",
                JsonSchema: ParseSchema("""
                    { "type": "object", "properties": {}, "additionalProperties": false }
                    """),
                Invoke: InvokeUnreadCountAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "Notifications are scoped to the current user; the chatbot cannot see another user's inbox. Each notification has a kind (workflow_task, mention, etc.), a related entity, and an optional linkPath that deep-links into the SPA.";

    private static async Task<JsonElement> InvokeListAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var unreadOnly = args.TryGetProperty("unreadOnly", out var u) && u.ValueKind == JsonValueKind.True;
        var search = args.TryGetProperty("search", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString()
            : null;
        var take = args.TryGetProperty("take", out var t) && t.ValueKind == JsonValueKind.Number
            ? Math.Clamp(t.GetInt32(), 1, 100)
            : 25;

        var store = context.Services.GetRequiredService<INotificationStore>();
        var page = await store.ListPagedForUserAsync(
            context.Session.UserId,
            new ListNotificationsRequest(
                Page: 0,
                PageSize: take,
                Search: search,
                SortBy: null,
                SortDir: null,
                UnreadOnly: unreadOnly),
            ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "notifications",
            source = "INotificationStore",
            data = new
            {
                totalCount = page.TotalCount,
                unreadCount = page.UnreadCount,
                items = page.Items.Select(n => new
                {
                    id = n.Id,
                    kind = n.Kind,
                    title = n.Title,
                    body = n.Body,
                    relatedEntityKind = n.RelatedEntityKind,
                    relatedEntityId = n.RelatedEntityId,
                    parentEntityKind = n.ParentEntityKind,
                    parentEntityId = n.ParentEntityId,
                    linkPath = n.LinkPath,
                    isRead = n.IsRead,
                    createdAtUtc = n.CreatedAtUtc,
                    readAtUtc = n.ReadAtUtc
                }).ToArray()
            }
        });
    }

    private static async Task<JsonElement> InvokeUnreadCountAsync(
        JsonElement args, AgentToolContext context, CancellationToken ct)
    {
        var store = context.Services.GetRequiredService<INotificationStore>();
        var count = await store.GetUnreadCountAsync(context.Session.UserId, ct);
        return JsonSerializer.SerializeToElement(new
        {
            kind = "notification_unread_count",
            source = "INotificationStore",
            data = new { unreadCount = count }
        });
    }

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
