using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Agent.Skills;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Records.Fields;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class ManageRecordTypesSkillTests
{
    private static readonly Guid CarTypeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SystemTypeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SessionUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly RecordType CarType = new()
    {
        Id = CarTypeId,
        ShortCode = "CAR",
        Name = "Car",
        Description = "Vehicles in inventory",
        IsSystem = false
    };

    private static readonly RecordType SystemType = new()
    {
        Id = SystemTypeId,
        ShortCode = "SYS",
        Name = "System",
        IsSystem = true
    };

    private static readonly RecordTypeField ModelField = new()
    {
        Id = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
        RecordTypeId = CarTypeId,
        FieldKey = "model",
        DisplayName = "Model",
        DataType = "text",
        Config = ParseElement("{}"),
        IsRequired = true,
        SortOrder = 0
    };

    private static readonly RecordTypeField YearField = new()
    {
        Id = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111"),
        RecordTypeId = CarTypeId,
        FieldKey = "year",
        DisplayName = "Year",
        DataType = "number",
        Config = ParseElement("{}"),
        IsRequired = false,
        SortOrder = 10
    };

    [Fact]
    public void Skill_exposes_six_tools_with_expected_names()
    {
        var skill = new ManageRecordTypesSkill();

        var toolNames = skill.Tools.Select(t => t.Name).OrderBy(n => n).ToArray();

        Assert.Equal(
            new[]
            {
                "add_record_type_field",
                "create_record_type",
                "set_record_type_archived",
                "set_record_type_field_archived",
                "update_record_type",
                "update_record_type_field"
            },
            toolNames);
    }

    [Fact]
    public async Task CreateType_dry_run_returns_proposal_and_does_not_call_CreateAsync()
    {
        var typeStore = new FakeTypeStore();
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "create_record_type", new
        {
            shortCode = "CAR",
            name = "Car",
            description = "Vehicles",
            confirmed = false
        }, typeStore, authorizer);

        Assert.Equal("record_type_change_proposal", result.GetProperty("kind").GetString());
        var data = result.GetProperty("data");
        Assert.Equal("create_type", data.GetProperty("operation").GetString());
        Assert.Equal("CAR", data.GetProperty("after").GetProperty("shortCode").GetString());
        Assert.True(data.GetProperty("validation").GetProperty("ok").GetBoolean());
        Assert.Empty(typeStore.CreateCalls);
    }

    [Fact]
    public async Task CreateType_commit_calls_CreateAsync_with_session_userId()
    {
        var typeStore = new FakeTypeStore();
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "create_record_type", new
        {
            shortCode = "CAR",
            name = "Car",
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("record_type_change_committed", result.GetProperty("kind").GetString());
        Assert.Equal("CAR", result.GetProperty("data").GetProperty("shortCode").GetString());
        Assert.Equal(0, result.GetProperty("data").GetProperty("createdFieldCount").GetInt32());

        var call = Assert.Single(typeStore.CreateCalls);
        Assert.Equal(SessionUserId, call.ActorId);
        Assert.Equal("CAR", call.Input.ShortCode);
        Assert.Equal("Car", call.Input.Name);
    }

    [Fact]
    public async Task CreateType_authorizer_denial_returns_error_and_short_circuits()
    {
        var typeStore = new FakeTypeStore();
        var authorizer = new FakeAuthorizer
        {
            Default = AuthEffect.Allow,
            Decisions = { [(Actions.Create, EntityKinds.RecordType, "*")] = AuthEffect.Deny }
        };
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "create_record_type", new
        {
            shortCode = "CAR",
            name = "Car",
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Empty(typeStore.CreateCalls);
    }

    [Fact]
    public async Task CreateType_commit_surfaces_validation_exception_as_failed_envelope()
    {
        var typeStore = new FakeTypeStore
        {
            CreateThrows = new RecordTypeValidationException("short_code 'CAR' is already in use.")
        };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "create_record_type", new
        {
            shortCode = "CAR",
            name = "Car",
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("record_type_change_failed", result.GetProperty("kind").GetString());
        Assert.False(result.GetProperty("data").GetProperty("validation").GetProperty("ok").GetBoolean());
    }

    // --- helpers / fakes ---

    private static async Task<JsonElement> Invoke(
        ManageRecordTypesSkill skill,
        string toolName,
        object args,
        FakeTypeStore typeStore,
        FakeAuthorizer authorizer,
        IFieldTypeRegistry? registry = null)
    {
        var argsJson = JsonSerializer.Serialize(args);
        return await InvokeRaw(skill, toolName, argsJson, typeStore, authorizer, registry);
    }

    private static async Task<JsonElement> InvokeRaw(
        ManageRecordTypesSkill skill,
        string toolName,
        string argsJson,
        FakeTypeStore typeStore,
        FakeAuthorizer authorizer,
        IFieldTypeRegistry? registry = null)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var tool = skill.Tools.Single(t => t.Name == toolName);

        var services = new ServiceCollection();
        services.AddSingleton<IRecordTypeStore>(typeStore);
        services.AddSingleton<IAuthorizer>(authorizer);
        services.AddSingleton<IFieldTypeRegistry>(registry ?? new FakeFieldTypeRegistry());
        var sp = services.BuildServiceProvider();

        var ctx = new AgentToolContext(
            new AgentSessionContext(new ClaimsPrincipal(), SessionUserId, "test"),
            sp);

        return await tool.Invoke(doc.RootElement, ctx, CancellationToken.None);
    }

    private static JsonElement ParseElement(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private sealed class FakeTypeStore : IRecordTypeStore
    {
        public List<RecordType> Types { get; } = new();
        public Dictionary<Guid, List<RecordTypeField>> FieldsByType { get; } = new();

        public List<(CreateRecordTypeInput Input, Guid ActorId)> CreateCalls { get; } = new();
        public List<(Guid Id, UpdateRecordTypeInput Input, Guid ActorId)> UpdateCalls { get; } = new();
        public List<(Guid Id, bool Archived, Guid ActorId)> ArchiveCalls { get; } = new();
        public List<(Guid TypeId, CreateRecordTypeFieldInput Input, Guid ActorId)> CreateFieldCalls { get; } = new();
        public List<(Guid TypeId, Guid FieldId, UpdateRecordTypeFieldInput Input, Guid ActorId)> UpdateFieldCalls { get; } = new();
        public List<(Guid TypeId, Guid FieldId, bool Archived, Guid ActorId)> ArchiveFieldCalls { get; } = new();

        public RecordTypeValidationException? CreateThrows { get; set; }
        public RecordTypeValidationException? UpdateThrows { get; set; }
        public RecordTypeValidationException? CreateFieldThrows { get; set; }
        public RecordTypeValidationException? UpdateFieldThrows { get; set; }

        public Func<CreateRecordTypeInput, RecordType>? CreateResponseFactory { get; set; }
        public Func<UpdateRecordTypeInput, RecordType>? UpdateResponseFactory { get; set; }
        public Func<CreateRecordTypeFieldInput, RecordTypeField>? CreateFieldResponseFactory { get; set; }
        public Func<UpdateRecordTypeFieldInput, RecordTypeField>? UpdateFieldResponseFactory { get; set; }

        public Task<IReadOnlyList<RecordType>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RecordType>>(Types);

        public Task<RecordType?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Types.FirstOrDefault(t => t.Id == id));

        public Task<RecordType?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Types.FirstOrDefault(t => t.ShortCode == shortCode));

        public Task<RecordType> CreateAsync(CreateRecordTypeInput input, Guid actorId, CancellationToken cancellationToken = default)
        {
            CreateCalls.Add((input, actorId));
            if (CreateThrows is not null) throw CreateThrows;
            var created = CreateResponseFactory?.Invoke(input) ?? new RecordType
            {
                Id = Guid.NewGuid(),
                ShortCode = input.ShortCode,
                Name = input.Name
            };
            Types.Add(created);
            return Task.FromResult(created);
        }

        public Task<RecordType> UpdateAsync(Guid id, UpdateRecordTypeInput input, Guid actorId, CancellationToken cancellationToken = default)
        {
            UpdateCalls.Add((id, input, actorId));
            if (UpdateThrows is not null) throw UpdateThrows;
            return Task.FromResult(UpdateResponseFactory?.Invoke(input) ?? Types.First(t => t.Id == id));
        }

        public Task<RecordType> SetArchivedAsync(Guid id, bool archived, Guid actorId, CancellationToken cancellationToken = default)
        {
            ArchiveCalls.Add((id, archived, actorId));
            return Task.FromResult(Types.First(t => t.Id == id) with { IsArchived = archived });
        }

        public Task<IReadOnlyList<RecordTypeField>> ListFieldsAsync(Guid recordTypeId, bool includeArchived, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RecordTypeField>>(FieldsByType.TryGetValue(recordTypeId, out var f) ? f : new List<RecordTypeField>());

        public Task<RecordTypeField?> GetFieldAsync(Guid recordTypeId, Guid fieldId, CancellationToken cancellationToken = default) =>
            Task.FromResult(FieldsByType.TryGetValue(recordTypeId, out var list) ? list.FirstOrDefault(f => f.Id == fieldId) : null);

        public Task<RecordTypeField> CreateFieldAsync(Guid recordTypeId, CreateRecordTypeFieldInput input, Guid actorId, CancellationToken cancellationToken = default)
        {
            CreateFieldCalls.Add((recordTypeId, input, actorId));
            if (CreateFieldThrows is not null) throw CreateFieldThrows;
            var created = CreateFieldResponseFactory?.Invoke(input) ?? new RecordTypeField
            {
                Id = Guid.NewGuid(),
                RecordTypeId = recordTypeId,
                FieldKey = input.FieldKey,
                DisplayName = input.DisplayName,
                DataType = input.DataType,
                Config = input.Config,
                IsRequired = input.IsRequired,
                SortOrder = input.SortOrder
            };
            if (!FieldsByType.TryGetValue(recordTypeId, out var list))
            {
                list = new List<RecordTypeField>();
                FieldsByType[recordTypeId] = list;
            }
            list.Add(created);
            return Task.FromResult(created);
        }

        public Task<RecordTypeField> UpdateFieldAsync(Guid recordTypeId, Guid fieldId, UpdateRecordTypeFieldInput input, Guid actorId, CancellationToken cancellationToken = default)
        {
            UpdateFieldCalls.Add((recordTypeId, fieldId, input, actorId));
            if (UpdateFieldThrows is not null) throw UpdateFieldThrows;
            var existing = FieldsByType[recordTypeId].First(f => f.Id == fieldId);
            return Task.FromResult(UpdateFieldResponseFactory?.Invoke(input) ?? existing);
        }

        public Task<RecordTypeField> SetFieldArchivedAsync(Guid recordTypeId, Guid fieldId, bool archived, Guid actorId, CancellationToken cancellationToken = default)
        {
            ArchiveFieldCalls.Add((recordTypeId, fieldId, archived, actorId));
            var existing = FieldsByType[recordTypeId].First(f => f.Id == fieldId);
            return Task.FromResult(existing with { IsArchived = archived });
        }

        public Task<IReadOnlyList<RecordTypeAuditEntry>> ListAuditAsync(Guid recordTypeId, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RecordTypeAuditEntry>>(Array.Empty<RecordTypeAuditEntry>());
    }

    private sealed class FakeAuthorizer : IAuthorizer
    {
        public List<(string Action, EntityRef Target)> Calls { get; } = new();

        // Default: allow everything. Tests override per-(action, kind, id) by setting
        // a more specific entry in Decisions.
        public AuthEffect Default { get; set; } = AuthEffect.Allow;
        public Dictionary<(string Action, string Kind, string Id), AuthEffect> Decisions { get; } = new();

        public Task<AuthDecision> AuthorizeAsync(ClaimsPrincipal actor, string action, EntityRef target, CancellationToken cancellationToken = default)
        {
            Calls.Add((action, target));
            var key = (action, target.Kind, target.Id);
            var effect = Decisions.TryGetValue(key, out var v) ? v : Default;
            return Task.FromResult(effect == AuthEffect.Allow
                ? AuthDecision.Allow("test")
                : AuthDecision.Deny("test"));
        }

        public Task<IQueryable<T>> FilterQueryAsync<T>(AutoNateDbContext db, ClaimsPrincipal actor, string kind, string action, IQueryable<T> source, CancellationToken cancellationToken = default) where T : class =>
            Task.FromResult(source);
        public Task<CapabilitySummary> GetCapabilitiesAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<bool> IsAuthorizedAsync(ClaimsPrincipal actor, string kind, string action, Func<SelectorAst, bool> selectorMatcher, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<RecordSqlFilter> BuildRecordSqlFilterAsync(ClaimsPrincipal actor, string action, int parameterOffset, CancellationToken cancellationToken = default) =>
            Task.FromResult(RecordSqlFilter.Open);
        public Task<AuthExplanation> ExplainAsync(Guid asUserId, string action, EntityRef target, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class FakeFieldTypeRegistry : IFieldTypeRegistry
    {
        public IReadOnlyCollection<IFieldType> All => Array.Empty<IFieldType>();

        public IFieldType Get(string dataType) =>
            TryGet(dataType, out var ft) ? ft : throw new UnknownFieldTypeException(dataType);

        public bool TryGet(string dataType, out IFieldType fieldType)
        {
            fieldType = new PassThroughFieldType(dataType);
            return dataType is "text" or "number" or "date" or "phone" or "email" or "option" or "boolean";
        }

        private sealed class PassThroughFieldType : IFieldType
        {
            public PassThroughFieldType(string dataType) { DataType = dataType; }
            public string DataType { get; }
            public JsonElement NormalizeConfig(JsonElement config)
            {
                if (DataType == "option")
                {
                    if (config.ValueKind != JsonValueKind.Object || !config.TryGetProperty("choices", out var c) || c.ValueKind != JsonValueKind.Array || c.GetArrayLength() == 0)
                    {
                        throw new FieldConfigException("option.choices must have at least one entry.");
                    }
                }
                return config.ValueKind == JsonValueKind.Undefined ? ParseElement("{}") : config.Clone();
            }
            public FieldValidationResult ValidateValue(JsonElement value, JsonElement config, bool isRequired, out JsonElement normalized)
            {
                normalized = value;
                return FieldValidationResult.Success;
            }
            public FilterSqlFragment BuildFilter(string fieldKey, FilterOperator op, JsonElement operand, JsonElement config) =>
                throw new NotSupportedException();
        }
    }
}
