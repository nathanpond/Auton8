using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Signals;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Workflow;

public sealed class EfCoreWorkflowModelStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    IWorkflowSignalRegistry signalRegistry,
    IDaprStreamingSubscriber streamingSubscriber) : IWorkflowModelStore
{
    private readonly IWorkflowSignalRegistry _signalRegistry = signalRegistry;
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
        existingVersion.PublishedAtUtc = deployment.DeployedAtUtc.UtcDateTime;

        await dbContext.SaveChangesAsync(cancellationToken);

        // Re-derive the topic→signal map from all currently-published workflows
        // and ask the streaming subscriber to pick up any new topics or release
        // ones that no longer back a published workflow. Streaming subscriptions
        // mean these changes take effect without a sidecar restart.
        await _signalRegistry.RefreshAsync(cancellationToken);
        await _streamingSubscriber.SyncAsync(cancellationToken);

        return entity.ToModel();
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
        var hasDefinitionChanges =
            !string.Equals(existingEntity.BpmnXml, incomingModel.BpmnXml, StringComparison.Ordinal) ||
            !string.Equals(existingEntity.Name, incomingModel.Name, StringComparison.Ordinal);

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
