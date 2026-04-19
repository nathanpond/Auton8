using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Workflow;

public sealed class EfCoreWorkflowModelStore(IDbContextFactory<AutoNateDbContext> dbContextFactory) : IWorkflowModelStore
{
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

    public async Task<WorkflowModel> SaveAsync(WorkflowModel model, CancellationToken cancellationToken = default)
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
}
