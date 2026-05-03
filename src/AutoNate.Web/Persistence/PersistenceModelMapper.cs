using System.Text.Json;
using AutoNate.Web.Models;
using LocalUserEntity = AutoNate.Web.Persistence.Scaffolded.LocalUser;
using WorkflowModelEntity = AutoNate.Web.Persistence.Scaffolded.WorkflowModel;
using WorkflowModelVersionEntity = AutoNate.Web.Persistence.Scaffolded.WorkflowModelVersion;

namespace AutoNate.Web.Persistence;

internal static class PersistenceModelMapper
{
    private static readonly JsonSerializerOptions DefaultVariablesJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };


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
            IdpKey = entity.IdpKey,
            FailedLoginAttempts = entity.FailedLoginAttempts,
            IsLocked = entity.IsLocked,
            LockedAtUtc = entity.LockedAtUtc is null ? null : ToDateTimeOffset(entity.LockedAtUtc.Value)
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
            IsDraft = entity.IsDraft,
            DraftVersionNumber = entity.DraftVersionNumber,
            PublishedVersionNumber = entity.PublishedVersionNumber,
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
                },
            DefaultVariables = DeserializeDefaultVariables(entity.DefaultVariables)
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
        entity.IsDraft = model.IsDraft;
        entity.DraftVersionNumber = model.DraftVersionNumber;
        entity.PublishedVersionNumber = model.PublishedVersionNumber;
        entity.ActiveProcessInstanceId = model.ActiveProcessInstanceId;
        entity.CreatedAtUtc = model.CreatedAtUtc.UtcDateTime;
        entity.UpdatedAtUtc = model.UpdatedAtUtc.UtcDateTime;
        entity.LastDeploymentId = model.LastDeployment?.DeploymentId;
        entity.LastProcessDefinitionId = model.LastDeployment?.ProcessDefinitionId;
        entity.LastProcessDefinitionKey = model.LastDeployment?.ProcessDefinitionKey;
        entity.LastProcessDefinitionVersion = model.LastDeployment?.ProcessDefinitionVersion;
        entity.LastDeployedAtUtc = model.LastDeployment?.DeployedAtUtc.UtcDateTime;
        entity.DefaultVariables = SerializeDefaultVariables(model.DefaultVariables);
    }

    private static IReadOnlyList<WorkflowDefaultVariable>? DeserializeDefaultVariables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<WorkflowDefaultVariable>>(json, DefaultVariablesJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? SerializeDefaultVariables(IReadOnlyList<WorkflowDefaultVariable>? variables)
    {
        if (variables is null || variables.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(variables, DefaultVariablesJsonOptions);
    }

    public static WorkflowModelVersion ToModel(this WorkflowModelVersionEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new WorkflowModelVersion
        {
            Id = entity.Id,
            WorkflowModelId = entity.WorkflowModelId,
            VersionNumber = entity.VersionNumber,
            Name = entity.Name,
            ProcessKey = entity.ProcessKey,
            BpmnXml = entity.BpmnXml,
            PublishedAtUtc = ToDateTimeOffset(entity.PublishedAtUtc),
            Deployment = new WorkflowDeploymentInfo
            {
                DeploymentId = entity.DeploymentId,
                ProcessDefinitionId = entity.ProcessDefinitionId,
                ProcessDefinitionKey = entity.ProcessDefinitionKey,
                ProcessDefinitionVersion = entity.ProcessDefinitionVersion,
                DeployedAtUtc = ToDateTimeOffset(entity.PublishedAtUtc)
            }
        };
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
