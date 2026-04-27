using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Workflow;

public sealed class EfCoreWorkflowSignalRegistry(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    ILogger<EfCoreWorkflowSignalRegistry> logger)
    : IWorkflowSignalRegistry
{
    private static readonly IReadOnlySet<string> EmptySet =
        new HashSet<string>(StringComparer.Ordinal);

    private readonly IDbContextFactory<AutoNateDbContext> _dbContextFactory = dbContextFactory;
    private readonly ILogger<EfCoreWorkflowSignalRegistry> _logger = logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private IReadOnlyDictionary<string, IReadOnlySet<string>> _byTopic =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

    public IReadOnlyCollection<string> GetSubscribedTopics()
    {
        return _byTopic.Keys.ToArray();
    }

    public IReadOnlySet<string> GetSignalNamesForTopic(string topic)
    {
        return _byTopic.TryGetValue(topic, out var names) ? names : EmptySet;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            // Only published versions matter at runtime: drafts aren't deployed
            // to Flowable and can't trigger signal start events.
            var publishedXmls = await dbContext.WorkflowModels
                .AsNoTracking()
                .Where(model => model.PublishedVersionNumber != null)
                .Select(model => model.BpmnXml)
                .ToListAsync(cancellationToken);

            var byTopic = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var xml in publishedXmls)
            {
                foreach (var registration in WorkflowBpmnXml.ExtractSignalRegistrations(xml))
                {
                    if (!byTopic.TryGetValue(registration.Topic, out var names))
                    {
                        names = new HashSet<string>(StringComparer.Ordinal);
                        byTopic[registration.Topic] = names;
                    }

                    names.Add(registration.SignalName);
                }
            }

            _byTopic = byTopic.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlySet<string>)pair.Value,
                StringComparer.Ordinal);

            _logger.LogInformation(
                "Workflow signal registry refreshed: {TopicCount} topics, {SignalCount} signals total.",
                _byTopic.Count,
                _byTopic.Values.Sum(set => set.Count));
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
