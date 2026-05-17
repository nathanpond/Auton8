using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Content;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions.Resolvers;

// Maps a content.events message to:
//   page:{pageId}   — only when resourceKind == "page" AND resource.id parses
//                     as a Guid. Per-recipient gate is the IContentAuthorizer
//                     view check (closest-ancestor share model).
//
// Other content kinds (project / cabinet / notebook / note / comment) aren't
// surfaced as channels yet — no SPA consumer needs them today. Add similar
// resolvers when one does.
public sealed class ContentChannelResolver : IChannelResolver
{
    public string Topic => ContentEventTopic.TopicName;

    public IReadOnlyList<ResolvedDelivery> Resolve(BusWatcherStreamService.BusWatcherMessage message)
    {
        if (!TryExtractPageId(message.Payload, out var pageId))
        {
            return Array.Empty<ResolvedDelivery>();
        }
        var target = new EntityRef(EntityKinds.Page, pageId.ToString());
        return new[]
        {
            new ResolvedDelivery($"{ContentChannelNames.PageInstanceKind}:{pageId}", target, FastGate: null),
        };
    }

    private static bool TryExtractPageId(string payload, out Guid pageId)
    {
        pageId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;

            if (!document.RootElement.TryGetProperty("resourceKind", out var kindElement)
                || kindElement.ValueKind != JsonValueKind.String
                || !string.Equals(kindElement.GetString(), ContentResourceKinds.Page, StringComparison.Ordinal))
            {
                return false;
            }
            if (!document.RootElement.TryGetProperty("resource", out var resourceElement)
                || resourceElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            if (!resourceElement.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            return Guid.TryParse(idElement.GetString(), out pageId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public static class ContentChannelNames
{
    public const string PageInstanceKind = "page";
}
