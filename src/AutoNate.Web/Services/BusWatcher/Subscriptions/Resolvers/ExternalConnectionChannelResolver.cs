using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.ExternalConnections;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions.Resolvers;

// Maps an external-connections.events message to:
//   external-connection:{id}   — per-recipient IAuthorizer view check on
//                                EntityKinds.ExternalConnection.
public sealed class ExternalConnectionChannelResolver : IChannelResolver
{
    public string Topic => ExternalConnectionEventTopic.TopicName;

    public IReadOnlyList<ResolvedDelivery> Resolve(BusWatcherStreamService.BusWatcherMessage message)
    {
        if (!TryExtractId(message.Payload, out var id))
        {
            return Array.Empty<ResolvedDelivery>();
        }
        var target = new EntityRef(EntityKinds.ExternalConnection, id.ToString());
        return new[]
        {
            new ResolvedDelivery($"{ExternalConnectionChannelNames.InstanceKind}:{id}", target, FastGate: null),
        };
    }

    private static bool TryExtractId(string payload, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty("resource", out var resource)
                || resource.ValueKind != JsonValueKind.Object) return false;
            if (!resource.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.String) return false;
            return Guid.TryParse(idElement.GetString(), out id);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public static class ExternalConnectionChannelNames
{
    public const string InstanceKind = "external-connection";
}
