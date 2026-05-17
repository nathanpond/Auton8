using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Records;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions.Resolvers;

// Maps a record.events message to:
//   record:{recordId}              — instance channel; gated by per-recipient
//                                    IAuthorizer view check.
//   records:visible                — list channel; same per-recipient gate.
//   records:assigned-to:{userId}   — one delivery per current assignee; gate
//                                    via IAuthorizer (an actor must still
//                                    have view to receive even self-assigned
//                                    events).
public sealed class RecordChannelResolver : IChannelResolver
{
    public string Topic => DaprRecordEventPublisher.TopicName;

    public IReadOnlyList<ResolvedDelivery> Resolve(BusWatcherStreamService.BusWatcherMessage message)
    {
        if (!TryExtract(message.Payload, out var recordId, out var assigneeIds))
        {
            return Array.Empty<ResolvedDelivery>();
        }

        var target = new EntityRef(EntityKinds.Record, recordId.ToString());
        var deliveries = new List<ResolvedDelivery>(2 + assigneeIds.Count)
        {
            new($"{RecordChannelNames.InstanceKind}:{recordId}", target, FastGate: null),
            new(RecordChannelNames.VisibleList, target, FastGate: null),
        };
        foreach (var assignee in assigneeIds)
        {
            deliveries.Add(new ResolvedDelivery(
                $"{RecordChannelNames.ListKind}:assigned-to:{assignee}",
                target,
                FastGate: null));
        }
        return deliveries;
    }

    private static bool TryExtract(string payload, out Guid recordId, out IReadOnlyList<Guid> assigneeIds)
    {
        recordId = Guid.Empty;
        assigneeIds = Array.Empty<Guid>();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty("recordId", out var idElement)) return false;
            if (!Guid.TryParse(idElement.GetString(), out recordId)) return false;

            if (document.RootElement.TryGetProperty("assigneeIds", out var assigneesElement)
                && assigneesElement.ValueKind == JsonValueKind.Array)
            {
                var ids = new List<Guid>(assigneesElement.GetArrayLength());
                foreach (var element in assigneesElement.EnumerateArray())
                {
                    if (Guid.TryParse(element.GetString(), out var assigneeId))
                    {
                        ids.Add(assigneeId);
                    }
                }
                assigneeIds = ids;
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public static class RecordChannelNames
{
    public const string InstanceKind = "record";
    public const string ListKind = "records";
    public const string VisibleList = "records:visible";
}
