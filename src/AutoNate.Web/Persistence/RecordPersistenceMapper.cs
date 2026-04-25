using System.Text.Json;
using AutoNate.Web.Models.Records;
using RecordTypeEntity = AutoNate.Web.Persistence.Scaffolded.RecordType;
using RecordTypeFieldEntity = AutoNate.Web.Persistence.Scaffolded.RecordTypeField;
using RecordTypeAuditEntity = AutoNate.Web.Persistence.Scaffolded.RecordTypeAuditEntry;
using RecordEntity = AutoNate.Web.Persistence.Scaffolded.Record;
using RecordFieldChangeEntity = AutoNate.Web.Persistence.Scaffolded.RecordFieldChange;
using RecordModel = AutoNate.Web.Models.Records.Record;
using RecordFieldChangeModel = AutoNate.Web.Models.Records.RecordFieldChange;
using RecordEdgeTypeEntity = AutoNate.Web.Persistence.Scaffolded.RecordEdgeType;
using RecordEdgeTypeFieldEntity = AutoNate.Web.Persistence.Scaffolded.RecordEdgeTypeField;
using RecordEdgeEntity = AutoNate.Web.Persistence.Scaffolded.RecordEdge;
using RecordEdgeTypeModel = AutoNate.Web.Models.Records.RecordEdgeType;
using RecordEdgeTypeFieldModel = AutoNate.Web.Models.Records.RecordEdgeTypeField;
using RecordEdgeModel = AutoNate.Web.Models.Records.RecordEdge;
using RecordCommentEntity = AutoNate.Web.Persistence.Scaffolded.RecordComment;
using RecordCommentRevisionEntity = AutoNate.Web.Persistence.Scaffolded.RecordCommentRevision;
using RecordCommentModel = AutoNate.Web.Models.Records.RecordComment;
using RecordCommentRevisionModel = AutoNate.Web.Models.Records.RecordCommentRevision;

namespace AutoNate.Web.Persistence;

internal static class RecordPersistenceMapper
{
    public static RecordType ToModel(this RecordTypeEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new RecordType
        {
            Id = entity.Id,
            ShortCode = entity.ShortCode,
            Name = entity.Name,
            Description = entity.Description,
            Icon = entity.Icon,
            Color = entity.Color,
            IsSystem = entity.IsSystem,
            IsArchived = entity.IsArchived,
            NextKeyNumber = entity.NextKeyNumber,
            CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(entity.CreatedAtUtc),
            CreatedBy = entity.CreatedBy,
            UpdatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(entity.UpdatedAtUtc),
            UpdatedBy = entity.UpdatedBy
        };
    }

    public static void Apply(this RecordTypeEntity entity, RecordType model)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(model);

        entity.Id = model.Id;
        entity.ShortCode = model.ShortCode;
        entity.Name = model.Name;
        entity.Description = model.Description;
        entity.Icon = model.Icon;
        entity.Color = model.Color;
        entity.IsSystem = model.IsSystem;
        entity.IsArchived = model.IsArchived;
        entity.NextKeyNumber = model.NextKeyNumber;
        entity.CreatedAtUtc = model.CreatedAtUtc.UtcDateTime;
        entity.CreatedBy = model.CreatedBy;
        entity.UpdatedAtUtc = model.UpdatedAtUtc.UtcDateTime;
        entity.UpdatedBy = model.UpdatedBy;
    }

    public static RecordTypeField ToModel(this RecordTypeFieldEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new RecordTypeField
        {
            Id = entity.Id,
            RecordTypeId = entity.RecordTypeId,
            FieldKey = entity.FieldKey,
            DisplayName = entity.DisplayName,
            DataType = entity.DataType,
            Config = ParseJson(entity.Config),
            IsRequired = entity.IsRequired,
            IsArchived = entity.IsArchived,
            SortOrder = entity.SortOrder,
            CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(entity.CreatedAtUtc),
            UpdatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(entity.UpdatedAtUtc)
        };
    }

    public static void Apply(this RecordTypeFieldEntity entity, RecordTypeField model)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(model);

        entity.Id = model.Id;
        entity.RecordTypeId = model.RecordTypeId;
        entity.FieldKey = model.FieldKey;
        entity.DisplayName = model.DisplayName;
        entity.DataType = model.DataType;
        entity.Config = model.Config.ValueKind == JsonValueKind.Undefined
            ? "{}"
            : model.Config.GetRawText();
        entity.IsRequired = model.IsRequired;
        entity.IsArchived = model.IsArchived;
        entity.SortOrder = model.SortOrder;
        entity.CreatedAtUtc = model.CreatedAtUtc.UtcDateTime;
        entity.UpdatedAtUtc = model.UpdatedAtUtc.UtcDateTime;
    }

    public static RecordTypeAuditEntry ToModel(this RecordTypeAuditEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new RecordTypeAuditEntry
        {
            Id = entity.Id,
            RecordTypeId = entity.RecordTypeId,
            ChangeKind = entity.ChangeKind,
            Before = entity.Before is null ? null : ParseJson(entity.Before),
            After = entity.After is null ? null : ParseJson(entity.After),
            ChangedBy = entity.ChangedBy,
            ChangedAtUtc = PersistenceModelMapper.ToDateTimeOffset(entity.ChangedAtUtc)
        };
    }

    public static JsonElement ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static RecordModel ToModel(this RecordEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new RecordModel
        {
            Id = entity.Id,
            RecordTypeId = entity.RecordTypeId,
            Key = entity.Key,
            KeyNumber = entity.KeyNumber,
            Name = entity.Name,
            AssigneeIds = entity.AssigneeIds.ToList(),
            Status = entity.Status,
            DueDate = entity.DueDate,
            Values = ParseJson(entity.Values),
            IsArchived = entity.IsArchived,
            CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(entity.CreatedAtUtc),
            CreatedBy = entity.CreatedBy,
            UpdatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(entity.UpdatedAtUtc),
            UpdatedBy = entity.UpdatedBy
        };
    }

    public static RecordFieldChangeModel ToModel(this RecordFieldChangeEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new RecordFieldChangeModel
        {
            Id = entity.Id,
            RecordId = entity.RecordId,
            ChangeSetId = entity.ChangeSetId,
            ChangeKind = entity.ChangeKind,
            FieldKey = entity.FieldKey,
            OldValue = entity.OldValue is null ? null : ParseJson(entity.OldValue),
            NewValue = entity.NewValue is null ? null : ParseJson(entity.NewValue),
            ChangedBy = entity.ChangedBy,
            ChangedAtUtc = PersistenceModelMapper.ToDateTimeOffset(entity.ChangedAtUtc)
        };
    }

    public static RecordEdgeTypeModel ToModel(this RecordEdgeTypeEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new RecordEdgeTypeModel
        {
            Id = entity.Id,
            ShortCode = entity.ShortCode,
            Name = entity.Name,
            InverseName = entity.InverseName,
            IsDirected = entity.IsDirected,
            AllowSelfReference = entity.AllowSelfReference,
            Cardinality = entity.Cardinality,
            FromRecordTypeIds = entity.FromRecordTypeIds?.ToList(),
            ToRecordTypeIds = entity.ToRecordTypeIds?.ToList(),
            IsArchived = entity.IsArchived,
            CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(entity.CreatedAtUtc),
            UpdatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(entity.UpdatedAtUtc)
        };
    }

    public static void Apply(this RecordEdgeTypeEntity entity, RecordEdgeTypeModel model)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(model);

        entity.Id = model.Id;
        entity.ShortCode = model.ShortCode;
        entity.Name = model.Name;
        entity.InverseName = model.InverseName;
        entity.IsDirected = model.IsDirected;
        entity.AllowSelfReference = model.AllowSelfReference;
        entity.Cardinality = model.Cardinality;
        entity.FromRecordTypeIds = model.FromRecordTypeIds?.ToArray();
        entity.ToRecordTypeIds = model.ToRecordTypeIds?.ToArray();
        entity.IsArchived = model.IsArchived;
        entity.CreatedAtUtc = model.CreatedAtUtc.UtcDateTime;
        entity.UpdatedAtUtc = model.UpdatedAtUtc.UtcDateTime;
    }

    public static RecordEdgeTypeFieldModel ToModel(this RecordEdgeTypeFieldEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new RecordEdgeTypeFieldModel
        {
            Id = entity.Id,
            EdgeTypeId = entity.EdgeTypeId,
            FieldKey = entity.FieldKey,
            DisplayName = entity.DisplayName,
            DataType = entity.DataType,
            Config = ParseJson(entity.Config),
            IsRequired = entity.IsRequired,
            SortOrder = entity.SortOrder
        };
    }

    public static void Apply(this RecordEdgeTypeFieldEntity entity, RecordEdgeTypeFieldModel model)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(model);

        entity.Id = model.Id;
        entity.EdgeTypeId = model.EdgeTypeId;
        entity.FieldKey = model.FieldKey;
        entity.DisplayName = model.DisplayName;
        entity.DataType = model.DataType;
        entity.Config = model.Config.ValueKind == System.Text.Json.JsonValueKind.Undefined
            ? "{}"
            : model.Config.GetRawText();
        entity.IsRequired = model.IsRequired;
        entity.SortOrder = model.SortOrder;
    }

    public static RecordEdgeModel ToModel(this RecordEdgeEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new RecordEdgeModel
        {
            Id = entity.Id,
            EdgeTypeId = entity.EdgeTypeId,
            FromRecordId = entity.FromRecordId,
            ToRecordId = entity.ToRecordId,
            Data = ParseJson(entity.Data),
            CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(entity.CreatedAtUtc),
            CreatedBy = entity.CreatedBy
        };
    }

    public static RecordCommentModel ToModel(this RecordCommentEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new RecordCommentModel
        {
            Id = entity.Id,
            RecordId = entity.RecordId,
            AuthorId = entity.AuthorId,
            Body = entity.Body,
            CreatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(entity.CreatedAtUtc),
            BodyUpdatedAtUtc = PersistenceModelMapper.ToDateTimeOffset(entity.BodyUpdatedAtUtc),
            IsDeleted = entity.IsDeleted,
            DeletedAtUtc = entity.DeletedAtUtc is null ? null : PersistenceModelMapper.ToDateTimeOffset(entity.DeletedAtUtc.Value),
            DeletedBy = entity.DeletedBy
        };
    }

    public static RecordCommentRevisionModel ToModel(this RecordCommentRevisionEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new RecordCommentRevisionModel
        {
            Id = entity.Id,
            CommentId = entity.CommentId,
            Body = entity.Body,
            ReplacedAtUtc = PersistenceModelMapper.ToDateTimeOffset(entity.ReplacedAtUtc),
            ReplacedBy = entity.ReplacedBy
        };
    }
}
