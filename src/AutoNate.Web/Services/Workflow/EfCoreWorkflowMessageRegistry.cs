using System.Xml;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Workflow;

// #524. The message half of what EfCoreWorkflowSignalRegistry does for signals.
// Deliberately a sibling rather than a widening of it — see IWorkflowMessageRegistry
// for why the separation is the feature.
public sealed class EfCoreWorkflowMessageRegistry(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<EfCoreWorkflowMessageRegistry> logger)
    : IWorkflowMessageRegistry
{
    private static readonly IReadOnlySet<string> EmptySet =
        new HashSet<string>(StringComparer.Ordinal);

    private static readonly IReadOnlyList<WorkflowMessageRegistration> EmptyRegistrations =
        Array.Empty<WorkflowMessageRegistration>();

    private readonly IDbContextFactory<AutoNateDbContext> _dbContextFactory = dbContextFactory;
    private readonly ILogger<EfCoreWorkflowMessageRegistry> _logger = logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private IReadOnlyDictionary<string, IReadOnlySet<string>> _byTopic =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

    private IReadOnlyDictionary<string, IReadOnlyList<WorkflowMessageRegistration>> _registrationsByTopic =
        new Dictionary<string, IReadOnlyList<WorkflowMessageRegistration>>(StringComparer.Ordinal);

    public IReadOnlyCollection<string> GetSubscribedTopics() => _byTopic.Keys.ToArray();

    public IReadOnlySet<string> GetMessageNamesForTopic(string topic) =>
        _byTopic.TryGetValue(topic, out var names) ? names : EmptySet;

    public IReadOnlyList<WorkflowMessageRegistration> GetRegistrationsForTopic(string topic) =>
        _registrationsByTopic.TryGetValue(topic, out var registrations)
            ? registrations
            : EmptyRegistrations;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            // THE PUBLISHED VERSION'S XML, NOT THE MODEL'S (#553).
            //
            // Filtering on `PublishedVersionNumber` and then selecting
            // `model.BpmnXml` reads the WORKING copy -- so for a published
            // workflow, whatever the author last typed into the draft decided
            // which names this registry subscribes to, while Flowable went on
            // running what was published. A draft edit could therefore drop a
            // subscription out from under parked instances, or add one nothing
            // deployed catches.
            //
            // #544 fixed exactly this in the signal broadcast endpoint and
            // named these two registries as the precedent that got it right.
            // They got the FILTER right and the xml wrong, which is the half
            // that bit.
            var publishedModels = await dbContext.WorkflowModels
                .AsNoTracking()
                .Where(model => model.PublishedVersionNumber != null)
                .Join(
                    dbContext.WorkflowModelVersions.AsNoTracking(),
                    model => new { model.Id, Version = model.PublishedVersionNumber!.Value },
                    version => new { Id = version.WorkflowModelId, Version = version.VersionNumber },
                    (model, version) => new { model.Id, version.BpmnXml })
                .ToListAsync(cancellationToken);

            var byTopicNames = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var byTopicRegs = new Dictionary<string, List<WorkflowMessageRegistration>>(StringComparer.Ordinal);

            foreach (var model in publishedModels)
            {
                IReadOnlyList<WorkflowMessageRegistration> registrationsForModel;
                try
                {
                    registrationsForModel = WorkflowBpmnXml.ExtractMessageRegistrations(model.BpmnXml);
                }
                catch (XmlException ex)
                {
                    // One unparseable stored diagram must not cost every other
                    // workflow its subscriptions — but it is said out loud,
                    // because messages this model declares will not arrive until
                    // it is fixed and re-published.
                    _logger.LogWarning(ex,
                        "Skipping message registrations for workflow {WorkflowId}: BPMN XML failed to parse.",
                        model.Id);
                    continue;
                }

                foreach (var registration in registrationsForModel)
                {
                    if (!byTopicNames.TryGetValue(registration.Topic, out var names))
                    {
                        names = new HashSet<string>(StringComparer.Ordinal);
                        byTopicNames[registration.Topic] = names;
                    }

                    names.Add(registration.MessageName);

                    if (!byTopicRegs.TryGetValue(registration.Topic, out var registrations))
                    {
                        registrations = new List<WorkflowMessageRegistration>();
                        byTopicRegs[registration.Topic] = registrations;
                    }

                    registrations.Add(registration);
                }
            }

            _byTopic = byTopicNames.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlySet<string>)pair.Value,
                StringComparer.Ordinal);

            _registrationsByTopic = byTopicRegs.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<WorkflowMessageRegistration>)pair.Value.AsReadOnly(),
                StringComparer.Ordinal);
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
