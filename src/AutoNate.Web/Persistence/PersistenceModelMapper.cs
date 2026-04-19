using AutoNate.Web.Models;
using LocalUserEntity = AutoNate.Web.Persistence.Scaffolded.LocalUser;
using WorkflowModelEntity = AutoNate.Web.Persistence.Scaffolded.WorkflowModel;

namespace AutoNate.Web.Persistence;

internal static class PersistenceModelMapper
{
    public static LocalUser ToModel(this LocalUserEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new LocalUser
        {
            Id = entity.Id,
            Username = entity.Username,
            Email = entity.Email,
            FirstName = entity.FirstName,
            LastName = entity.LastName,
            UserId = entity.UserId,
            CreatedDate = ToDateTimeOffset(entity.CreatedDate),
            LastLoginDate = entity.LastLoginDate is null ? null : ToDateTimeOffset(entity.LastLoginDate.Value),
            IdpKey = entity.IdpKey
        };
    }

    public static WorkflowModel ToModel(this WorkflowModelEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new WorkflowModel
        {
            Id = entity.Id,
            Name = entity.Name,
            ProcessKey = entity.ProcessKey,
            BpmnXml = entity.BpmnXml,
            ActiveProcessInstanceId = entity.ActiveProcessInstanceId,
            CreatedAtUtc = ToDateTimeOffset(entity.CreatedAtUtc),
            UpdatedAtUtc = ToDateTimeOffset(entity.UpdatedAtUtc),
            LastDeployment = entity.LastDeploymentId is null
                ? null
                : new WorkflowDeploymentInfo
                {
                    DeploymentId = entity.LastDeploymentId,
                    ProcessDefinitionId = entity.LastProcessDefinitionId ?? string.Empty,
                    ProcessDefinitionKey = entity.LastProcessDefinitionKey ?? string.Empty,
                    ProcessDefinitionVersion = entity.LastProcessDefinitionVersion ?? 0,
                    DeployedAtUtc = entity.LastDeployedAtUtc is null
                        ? DateTimeOffset.MinValue
                        : ToDateTimeOffset(entity.LastDeployedAtUtc.Value)
                }
        };
    }

    public static void Apply(this WorkflowModelEntity entity, WorkflowModel model)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(model);

        entity.Id = model.Id;
        entity.Name = model.Name;
        entity.ProcessKey = model.ProcessKey;
        entity.BpmnXml = model.BpmnXml;
        entity.ActiveProcessInstanceId = model.ActiveProcessInstanceId;
        entity.CreatedAtUtc = model.CreatedAtUtc.UtcDateTime;
        entity.UpdatedAtUtc = model.UpdatedAtUtc.UtcDateTime;
        entity.LastDeploymentId = model.LastDeployment?.DeploymentId;
        entity.LastProcessDefinitionId = model.LastDeployment?.ProcessDefinitionId;
        entity.LastProcessDefinitionKey = model.LastDeployment?.ProcessDefinitionKey;
        entity.LastProcessDefinitionVersion = model.LastDeployment?.ProcessDefinitionVersion;
        entity.LastDeployedAtUtc = model.LastDeployment?.DeployedAtUtc.UtcDateTime;
    }

    public static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        var utcValue = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value.ToUniversalTime()
        };

        return new DateTimeOffset(utcValue);
    }
}
