using System.Reflection;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// Every workflow admin event is in the EventCatalog (#254).
/// </summary>
/// <remarks>
/// <para>
/// An uncatalogued event does not appear on the Events admin page and cannot be
/// discovered by a subscriber, so publishing one and cataloguing it are not the
/// same thing — and the claims that rest on the trail ("who advanced which
/// instance is on the record", "starting is recorded — who ran what, and when")
/// are weaker than they read when the entry is missing.
/// </para>
/// <para>
/// Six had drifted out by the time this was written: both message events, all
/// three ad-hoc events, and the call-activity children view. Their siblings were
/// catalogued, which is what made the gap invisible — the family looked covered.
/// Dashboards have had a parity test since they hit the same problem
/// (<c>DashboardEndpointsTests</c>); workflow events had none, so they drifted
/// the same way for the same reason.
/// </para>
/// </remarks>
public sealed class WorkflowEventCatalogParityTests
{
    /// <summary>The event-type constants, as opposed to the topic and kind names.</summary>
    private static IReadOnlyList<(string Name, string Value)> DeclaredEventTypes() =>
        typeof(WorkflowAdminEventTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false })
            .Where(field => field.FieldType == typeof(string))
            .Select(field => (field.Name, (string)field.GetRawConstantValue()!))
            .ToList();

    [Fact]
    public void Every_declared_workflow_event_type_has_a_catalog_entry()
    {
        var declared = DeclaredEventTypes();

        // Reflection over a class that had been renamed or emptied would make
        // every assertion below vacuous.
        Assert.True(declared.Count >= 30,
            $"Only {declared.Count} event types were found on WorkflowAdminEventTypes.");

        var catalogued = EventCatalog.AllEntries
            .Where(entry => entry.Topic == WorkflowAdminEventTopic.TopicName)
            .Select(entry => entry.EventType)
            .ToHashSet(StringComparer.Ordinal);

        var missing = declared
            .Where(type => !catalogued.Contains(type.Value))
            .Select(type => $"{type.Name} ({type.Value})")
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Workflow admin events that are published but not in the EventCatalog, " +
            "so they are invisible on the Events admin page and undiscoverable by " +
            $"subscribers:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", missing));
    }

    [Fact]
    public void Every_catalogued_workflow_event_is_one_the_code_declares()
    {
        // The other direction: an entry for an event nothing publishes any more
        // advertises a subscription that will never fire.
        var declared = DeclaredEventTypes().Select(type => type.Value)
            .ToHashSet(StringComparer.Ordinal);

        var orphans = EventCatalog.AllEntries
            .Where(entry => entry.Topic == WorkflowAdminEventTopic.TopicName)
            .Select(entry => entry.EventType)
            .Where(eventType => !declared.Contains(eventType))
            .ToList();

        Assert.True(
            orphans.Count == 0,
            "EventCatalog entries on the workflow-admin topic that no constant " +
            $"declares: {string.Join(", ", orphans)}");
    }

    [Fact]
    public void Each_workflow_catalog_entry_says_what_it_carries_and_when_it_fires()
    {
        // An entry with an empty description is present without being useful,
        // which passes the parity check above while leaving a subscriber no
        // better off than an uncatalogued event.
        foreach (var entry in EventCatalog.AllEntries
            .Where(entry => entry.Topic == WorkflowAdminEventTopic.TopicName))
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Summary),
                $"{entry.EventType} has no description.");
            Assert.False(string.IsNullOrWhiteSpace(entry.FiresWhen),
                $"{entry.EventType} does not say when it fires.");
            Assert.NotEmpty(entry.PayloadHighlights);
        }
    }

    [Fact]
    public void No_workflow_event_type_is_catalogued_twice()
    {
        // A duplicate renders twice on the admin page and means one of the two
        // descriptions is being maintained and the other is not.
        var duplicates = EventCatalog.AllEntries
            .Where(entry => entry.Topic == WorkflowAdminEventTopic.TopicName)
            .GroupBy(entry => entry.EventType, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            $"Duplicated workflow catalog entries: {string.Join(", ", duplicates)}");
    }
}
