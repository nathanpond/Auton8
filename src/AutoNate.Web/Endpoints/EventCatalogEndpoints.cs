using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.SiteSettings;
using AutoNate.Web.Services.Workflow;

namespace AutoNate.Web.Endpoints;

public sealed record EventCatalogResponse(
    IReadOnlyList<EventCatalogTransport> Transports,
    IReadOnlyList<EventCatalogPayloadField> PayloadFields,
    IReadOnlyList<EventCatalogCategory> Categories,
    IReadOnlyList<EventCatalogWorkflowRegistration> WorkflowRegistrations);

// (topic, eventType) tuples already configured on signal start events in
// currently-published workflows. Surfaced alongside the static catalog so
// the modal can suggest event types other workflows are already listening for.
public sealed record EventCatalogWorkflowRegistration(string Topic, string EventType);

public static class EventCatalogEndpoints
{
    public static IEndpointRouteBuilder MapEventCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/event-catalog")
            .RequireAuthorization();

        group.MapGet("/", async (
            IWorkflowSignalRegistry signalRegistry,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var registrations = signalRegistry
                .GetSubscribedTopics()
                .SelectMany(topic => signalRegistry
                    .GetSignalNamesForTopic(topic)
                    .Select(name => new EventCatalogWorkflowRegistration(topic, name)))
                .OrderBy(entry => entry.Topic, StringComparer.Ordinal)
                .ThenBy(entry => entry.EventType, StringComparer.Ordinal)
                .ToArray();

            await auditPublisher.PublishAsync(
                SiteEventTopic.TopicName,
                SiteEventTypes.EventCatalogViewed,
                SiteResourceKinds.EventCatalog,
                resource: null,
                details: new
                {
                    transportCount = EventCatalog.Transports.Length,
                    categoryCount = EventCatalog.Categories.Length,
                    workflowRegistrationCount = registrations.Length
                },
                ct);

            return Results.Ok(new EventCatalogResponse(
                EventCatalog.Transports,
                EventCatalog.PayloadFields,
                EventCatalog.Categories,
                registrations));
        });

        return app;
    }
}
