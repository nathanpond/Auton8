using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Phase 3 notification-write skill. Sending notifications to other users is
// admin-gated (SiteConfig:edit) because there's no per-row authorization on
// notification creation and we don't want the chatbot to become a spam
// vector. Read-side mark-as-read / mark-all-read are user-scoped (the store
// gates by userId in its own query) and don't need an extra check.
public sealed class SendNotificationsSkill : IAgentSkill
{
    public string Name => "send-notifications";

    public string Description =>
        "Send a notification to a user (admin-only) and mark notifications as read.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public SendNotificationsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: "send_notification",
                Description: "Send a notification to a user. Admin-only (SiteConfig:edit). The user gets the notification in their inbox and optionally a deep-link.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "userId": { "type": "string", "description": "Recipient user GUID." },
                        "kind": { "type": "string", "description": "Notification kind (free-form; common: 'system', 'mention'). Defaults to 'system'." },
                        "title": { "type": "string" },
                        "body": { "type": "string" },
                        "linkPath": { "type": ["string", "null"], "description": "Optional SPA path to deep-link from the notification." },
                        "relatedEntityKind": { "type": ["string", "null"] },
                        "relatedEntityId": { "type": ["string", "null"] },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["userId", "title", "body"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeSendAsync),

            new AgentTool(
                Name: "mark_notification_read",
                Description: "Mark one of the current user's notifications as read.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "id": { "type": "string" }
                      },
                      "required": ["id"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeMarkReadAsync),

            new AgentTool(
                Name: "mark_all_notifications_read",
                Description: "Mark every unread notification for the current user as read.",
                JsonSchema: ParseSchema("""
                    { "type": "object", "properties": {}, "additionalProperties": false }
                    """),
                Invoke: InvokeMarkAllAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "send_notification is admin-only — the model should not propose it for general users. Mark-read tools are user-scoped (the store filters by the current user's id; the chatbot cannot mark another user's notifications).";

    private static async Task<JsonElement> InvokeSendAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "send_notification";
        if (!TryReadGuid(args, "userId", out var userId))
            return ConfirmGate.Rejected(action, "userId is required and must be a GUID.");
        var title = ReadString(args, "title");
        var body = ReadString(args, "body");
        if (string.IsNullOrWhiteSpace(title)) return ConfirmGate.Rejected(action, "title is required.");
        if (string.IsNullOrWhiteSpace(body)) return ConfirmGate.Rejected(action, "body is required.");
        var kind = ReadString(args, "kind");
        if (string.IsNullOrWhiteSpace(kind)) kind = "system";
        var linkPath = ReadString(args, "linkPath");
        var relatedKind = ReadString(args, "relatedEntityKind");
        var relatedId = ReadString(args, "relatedEntityId");

        var authorizer = ctx.Services.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(
            ctx.Session.User, Actions.Edit, new EntityRef(EntityKinds.SiteConfig, string.Empty), ct);
        if (!decision.IsAllowed)
        {
            return ConfirmGate.Rejected(action, "SiteConfig:edit permission required to send notifications.");
        }

        var preview = new { userId, kind, title, body, linkPath };
        if (!ConfirmGate.IsConfirmed(args))
        {
            return ConfirmGate.Proposal("notification_send_proposal", action, preview);
        }

        var store = ctx.Services.GetRequiredService<INotificationStore>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        var notification = await store.CreateAsync(new CreateNotificationInput(
            UserId: userId,
            Kind: kind,
            Title: title,
            Body: body,
            RelatedEntityKind: relatedKind,
            RelatedEntityId: relatedId,
            LinkPath: linkPath), ct);
        await audit.PublishAsync(
            DaprNotificationEventPublisher.TopicName,
            NotificationEventTypes.Created,
            NotificationResourceKinds.Notification,
            resource: new { id = notification.Id, userId, kind, title },
            details: new { source = "chatbot", sentBy = ctx.Session.UserId },
            ct);
        return ConfirmGate.Committed("notification_send_committed", action, new
        {
            id = notification.Id,
            userId,
            kind,
            title,
            createdAtUtc = notification.CreatedAtUtc
        });
    }

    private static async Task<JsonElement> InvokeMarkReadAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "mark_notification_read";
        if (!TryReadGuid(args, "id", out var id))
            return ConfirmGate.Rejected(action, "id is required and must be a GUID.");

        var store = ctx.Services.GetRequiredService<INotificationStore>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        var notification = await store.MarkReadAsync(id, ctx.Session.UserId, ct);
        if (notification is null)
        {
            return ConfirmGate.Failed("notification_mark_read_failed", action, $"Notification {id} not found or not addressed to current user.");
        }
        await audit.PublishAsync(
            DaprNotificationEventPublisher.TopicName,
            NotificationEventTypes.Read,
            NotificationResourceKinds.Notification,
            resource: new { id, userId = ctx.Session.UserId },
            details: new { source = "chatbot" }, ct);
        return ConfirmGate.Committed("notification_mark_read_committed", action, new { id });
    }

    private static async Task<JsonElement> InvokeMarkAllAsync(JsonElement args, AgentToolContext ctx, CancellationToken ct)
    {
        const string action = "mark_all_notifications_read";
        var store = ctx.Services.GetRequiredService<INotificationStore>();
        var audit = ctx.Services.GetRequiredService<IAuditEventPublisher>();
        var count = await store.MarkAllReadAsync(ctx.Session.UserId, ct);
        await audit.PublishAsync(
            DaprNotificationEventPublisher.TopicName,
            NotificationEventTypes.AllRead,
            NotificationResourceKinds.NotificationCollection,
            resource: new { userId = ctx.Session.UserId },
            details: new { source = "chatbot", updatedCount = count }, ct);
        return ConfirmGate.Committed("notifications_mark_all_read_committed", action, new { updated = count });
    }

    private static bool TryReadGuid(JsonElement args, string name, out Guid id)
    {
        id = Guid.Empty;
        if (!args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        return Guid.TryParse(v.GetString(), out id);
    }

    private static string? ReadString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
