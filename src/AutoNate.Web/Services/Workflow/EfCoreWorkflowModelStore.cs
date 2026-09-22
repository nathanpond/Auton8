using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Signals;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Workflow;

public sealed class EfCoreWorkflowModelStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IWorkflowSignalRegistry signalRegistry,
    IWorkflowMessageRegistry messageRegistry,
    IDaprStreamingSubscriber streamingSubscriber) : IWorkflowModelStore
{
    private readonly IWorkflowSignalRegistry _signalRegistry = signalRegistry;
    private readonly IWorkflowMessageRegistry _messageRegistry = messageRegistry;
    private readonly IDaprStreamingSubscriber _streamingSubscriber = streamingSubscriber;

    public async Task<IReadOnlyList<WorkflowModel>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var models = await dbContext.WorkflowModels
            .AsNoTracking()
            .OrderByDescending(model => model.UpdatedAtUtc)
            .ThenBy(model => model.Name)
            .ToListAsync(cancellationToken);

        return models.Select(model => model.ToModel()).ToList();
    }

    public async Task<WorkflowModel?> GetAsync(Guid workflowModelId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.WorkflowModels
            .AsNoTracking()
            .SingleOrDefaultAsync(model => model.Id == workflowModelId, cancellationToken);

        return entity?.ToModel();
    }

    public async Task<WorkflowModel?> GetMostRecentAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.WorkflowModels
            .AsNoTracking()
            .OrderByDescending(model => model.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return entity?.ToModel();
    }

    public async Task<WorkflowModel?> GetByProcessKeyAsync(string processKey, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.WorkflowModels
            .AsNoTracking()
            .FirstOrDefaultAsync(model => model.ProcessKey == processKey, cancellationToken);

        return entity?.ToModel();
    }

    public async Task<WorkflowModel?> GetPublishedByProcessKeyAsync(
        string processKey, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // The join again (#553), for one key rather than all of them. See
        // ListPublishedAsync above: filtering `GetByProcessKeyAsync` cannot
        // recover the published xml, because the row it returns is already
        // carrying the draft's.
        // MATCHED ON THE VERSION'S KEY, NOT THE MODEL'S (#558).
        //
        // `workflow_models.process_key` is the working copy's, and `SaveAsync`
        // re-applies it from the request on every save -- so a draft save can
        // rename a published workflow's key while it is running. Matching on the
        // model row then failed to find the definition Flowable is executing,
        // for a caller reasoning about an instance of exactly that definition:
        // the same "a draft edit changes what a running instance does" shape
        // this method was written to end, surviving inside it.
        var row = await dbContext.WorkflowModels
            .AsNoTracking()
            .Where(model => model.PublishedVersionNumber != null)
            .Join(
                dbContext.WorkflowModelVersions.AsNoTracking(),
                model => new { model.Id, Version = model.PublishedVersionNumber!.Value },
                version => new { Id = version.WorkflowModelId, Version = version.VersionNumber },
                (model, version) => new
                {
                    model,
                    version.BpmnXml,
                    version.ProcessKey,
                    version.Name,
                    version.PublishedAtUtc,
                    version.VersionNumber
                })
            // NEWEST PUBLICATION FIRST, and the ordering is load-bearing (#561).
            //
            // `workflow_models.process_key` is UNIQUE; `workflow_model_versions`
            // .process_key is NOT, so moving the match here made the answer
            // ambiguous where the old one was merely wrong: publish A as `x`,
            // rename A's draft (freeing the unique constraint), then publish B
            // as `x`, and two published version rows carry `x`. An unordered
            // FirstOrDefault then hands a running instance whichever row the
            // planner felt like. Newest publication is the one the engine most
            // recently deployed under that key.
            .OrderByDescending(row => row.PublishedAtUtc)
            .ThenByDescending(row => row.VersionNumber)
            .FirstOrDefaultAsync(row => row.ProcessKey == processKey, cancellationToken);

        return row is null
            ? null
            : row.model.ToModel() with
            {
                BpmnXml = row.BpmnXml,
                ProcessKey = row.ProcessKey,
                Name = row.Name
            };
    }

    public async Task<WorkflowModel> SaveAsync(WorkflowModel model, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var normalizedInput = model with
        {
            Id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id,
            Name = WorkflowBpmnXml.NormalizeWorkflowName(model.Name),
            ProcessKey = WorkflowBpmnXml.NormalizeProcessKey(model.ProcessKey),
            CreatedAtUtc = model.CreatedAtUtc == default ? now : model.CreatedAtUtc,
            UpdatedAtUtc = now
        };

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.WorkflowModels
            .SingleOrDefaultAsync(existingModel => existingModel.Id == normalizedInput.Id, cancellationToken);

        var normalizedModel = entity is null
            ? normalizedInput with
            {
                IsDraft = true,
                DraftVersionNumber = Math.Max(1, normalizedInput.DraftVersionNumber),
                PublishedVersionNumber = normalizedInput.PublishedVersionNumber
            }
            : NormalizeDraftState(entity, normalizedInput);

        if (entity is null)
        {
            entity = new Persistence.Scaffolded.WorkflowModel();
            entity.Apply(normalizedModel);
            dbContext.WorkflowModels.Add(entity);
        }
        else
        {
            entity.Apply(normalizedModel);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task<WorkflowModel> PublishAsync(
        WorkflowModel model,
        WorkflowDeploymentInfo deployment,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var normalizedModel = model with
        {
            Id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id,
            Name = WorkflowBpmnXml.NormalizeWorkflowName(model.Name),
            ProcessKey = WorkflowBpmnXml.NormalizeProcessKey(model.ProcessKey),
            CreatedAtUtc = model.CreatedAtUtc == default ? now : model.CreatedAtUtc,
            UpdatedAtUtc = now
        };

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.WorkflowModels
            .SingleOrDefaultAsync(existingModel => existingModel.Id == normalizedModel.Id, cancellationToken);

        if (entity is null)
        {
            entity = new Persistence.Scaffolded.WorkflowModel();
            dbContext.WorkflowModels.Add(entity);
        }

        var draftModel = NormalizeDraftState(entity, normalizedModel);
        var publishedVersionNumber = draftModel.DraftVersionNumber;
        var publishedModel = draftModel with
        {
            IsDraft = false,
            PublishedVersionNumber = publishedVersionNumber,
            LastDeployment = deployment
        };

        entity.Apply(publishedModel);

        var existingVersion = await dbContext.WorkflowModelVersions
            .SingleOrDefaultAsync(
                version => version.WorkflowModelId == publishedModel.Id &&
                           version.VersionNumber == publishedVersionNumber,
                cancellationToken);

        if (existingVersion is null)
        {
            existingVersion = new Persistence.Scaffolded.WorkflowModelVersion
            {
                Id = Guid.NewGuid(),
                WorkflowModelId = publishedModel.Id
            };
            dbContext.WorkflowModelVersions.Add(existingVersion);
        }

        existingVersion.VersionNumber = publishedVersionNumber;
        existingVersion.Name = publishedModel.Name;
        existingVersion.ProcessKey = publishedModel.ProcessKey;
        existingVersion.BpmnXml = publishedModel.BpmnXml;
        existingVersion.DeploymentId = deployment.DeploymentId;
        existingVersion.ProcessDefinitionId = deployment.ProcessDefinitionId;
        existingVersion.ProcessDefinitionKey = deployment.ProcessDefinitionKey;
        existingVersion.ProcessDefinitionVersion = deployment.ProcessDefinitionVersion;
        // #169. The whole set, beside the primary's columns.
        existingVersion.DeployedDefinitions = PersistenceModelMapper.SerializeDeployedDefinitions(deployment.Definitions);
        existingVersion.PublishedAtUtc = deployment.DeployedAtUtc.UtcDateTime;

        await dbContext.SaveChangesAsync(cancellationToken);

        // Re-derive the topic→signal map from all currently-published workflows
        // and ask the streaming subscriber to pick up any new topics or release
        // ones that no longer back a published workflow. Streaming subscriptions
        // mean these changes take effect without a sidecar restart.
        await _signalRegistry.RefreshAsync(cancellationToken);
        // #524. Both, or a newly published message start never gets a
        // subscription and a deleted one keeps receiving.
        await _messageRegistry.RefreshAsync(cancellationToken);
        await _streamingSubscriber.SyncAsync(cancellationToken);

        return entity.ToModel();
    }

    public async Task<IReadOnlyList<WorkflowModel>> ListPublishedAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // The join is the point (#544). Filtering `ListAsync` would still hand
        // back each model's DRAFT xml, so a published workflow whose draft has
        // since dropped a declaration would read as no longer declaring it --
        // while instances sit parked on exactly that element in the engine.
        var published = await dbContext.WorkflowModels
            .AsNoTracking()
            .Where(model => model.PublishedVersionNumber != null)
            .Join(
                dbContext.WorkflowModelVersions.AsNoTracking(),
                model => new { Id = model.Id, Version = model.PublishedVersionNumber!.Value },
                version => new { Id = version.WorkflowModelId, Version = version.VersionNumber },
                (model, version) => new { model, version.BpmnXml })
            .ToListAsync(cancellationToken);

        return published
            .Select(row => row.model.ToModel() with { BpmnXml = row.BpmnXml })
            .ToList();
    }

    public async Task<IReadOnlyList<WorkflowModelVersion>> ListVersionsAsync(
        Guid workflowModelId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var versions = await dbContext.WorkflowModelVersions
            .AsNoTracking()
            .Where(version => version.WorkflowModelId == workflowModelId)
            .OrderByDescending(version => version.VersionNumber)
            .ToListAsync(cancellationToken);

        return versions.Select(version => version.ToModel()).ToList();
    }

    public async Task<WorkflowModel?> DeleteAsync(Guid workflowModelId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.WorkflowModels
            .SingleOrDefaultAsync(model => model.Id == workflowModelId, cancellationToken);
        if (entity is null) return null;

        var snapshot = entity.ToModel();

        // workflow_model_versions has ON DELETE CASCADE so EF doesn't have to
        // remove versions explicitly; the Postgres engine handles it.
        dbContext.WorkflowModels.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Re-derive the topic→signal map. If the deleted workflow was published
        // it may have been backing one or more signal subscriptions; the
        // streaming subscriber needs to release those topics now that no
        // published version owns them.
        await _signalRegistry.RefreshAsync(cancellationToken);
        // #524. Both, or a newly published message start never gets a
        // subscription and a deleted one keeps receiving.
        await _messageRegistry.RefreshAsync(cancellationToken);
        await _streamingSubscriber.SyncAsync(cancellationToken);

        return snapshot;
    }

    private static WorkflowModel NormalizeDraftState(
        Persistence.Scaffolded.WorkflowModel? existingEntity,
        WorkflowModel incomingModel)
    {
        if (existingEntity is null)
        {
            return incomingModel with
            {
                DraftVersionNumber = Math.Max(1, incomingModel.DraftVersionNumber),
                IsDraft = true,
                PublishedVersionNumber = incomingModel.PublishedVersionNumber
            };
        }

        var existingPublishedVersionNumber = existingEntity.PublishedVersionNumber;
        var existingDraftVersionNumber = Math.Max(existingEntity.DraftVersionNumber, 1);
        // PROCESS KEY COUNTS (#561).
        //
        // It was xml-or-name, and a key-only save therefore left `IsDraft`
        // false and `DraftVersionNumber` unbumped on a PUBLISHED model. Two
        // consequences, both real: the studio reported "not a draft" while the
        // row had diverged from what the engine is running, and the next
        // publish UPSERT-ed the existing version row rather than cutting a new
        // one -- silently rewriting the recorded `process_key` of a version
        // that is already deployed.
        //
        // The key is the identity the engine deploys under, so changing it is
        // as much a definition change as changing the name.
        var hasDefinitionChanges =
            !string.Equals(existingEntity.BpmnXml, incomingModel.BpmnXml, StringComparison.Ordinal) ||
            !string.Equals(existingEntity.Name, incomingModel.Name, StringComparison.Ordinal) ||
            !string.Equals(existingEntity.ProcessKey, incomingModel.ProcessKey, StringComparison.Ordinal);

        var draftVersionNumber = existingDraftVersionNumber;
        if (hasDefinitionChanges &&
            existingPublishedVersionNumber is not null &&
            existingDraftVersionNumber == existingPublishedVersionNumber.Value)
        {
            draftVersionNumber = existingPublishedVersionNumber.Value + 1;
        }

        return incomingModel with
        {
            IsDraft = existingEntity.IsDraft || hasDefinitionChanges,
            DraftVersionNumber = draftVersionNumber,
            PublishedVersionNumber = existingPublishedVersionNumber
        };
    }
}
