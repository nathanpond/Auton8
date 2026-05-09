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

    [Fact]
    public async Task CreateType_with_inline_fields_dry_run_validates_each_field()
    {
        var typeStore = new FakeTypeStore();
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "create_record_type", new
        {
            shortCode = "CAR",
            name = "Car",
            fields = new object[]
            {
                new { fieldKey = "model", displayName = "Model", dataType = "text", isRequired = true, sortOrder = 0 },
                new { fieldKey = "color", displayName = "Color", dataType = "option", config = new { choices = new[] { new { value = "red", label = "Red" } } }, isRequired = false, sortOrder = 10 }
            },
            confirmed = false
        }, typeStore, authorizer);

        Assert.Equal("record_type_change_proposal", result.GetProperty("kind").GetString());
        var fields = result.GetProperty("data").GetProperty("after").GetProperty("fields");
        Assert.Equal(2, fields.GetArrayLength());
        Assert.True(result.GetProperty("data").GetProperty("validation").GetProperty("ok").GetBoolean());
        Assert.Empty(typeStore.CreateCalls);
        Assert.Empty(typeStore.CreateFieldCalls);
    }

    [Fact]
    public async Task CreateType_with_inline_fields_dry_run_flags_invalid_option_config()
    {
        var typeStore = new FakeTypeStore();
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "create_record_type", new
        {
            shortCode = "CAR",
            name = "Car",
            fields = new object[]
            {
                new { fieldKey = "color", displayName = "Color", dataType = "option", config = new { } } // missing choices
            },
            confirmed = false
        }, typeStore, authorizer);

        var validation = result.GetProperty("data").GetProperty("validation");
        Assert.False(validation.GetProperty("ok").GetBoolean());
        Assert.Contains("choices", validation.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task CreateType_with_inline_fields_dry_run_flags_unknown_dataType()
    {
        var typeStore = new FakeTypeStore();
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "create_record_type", new
        {
            shortCode = "CAR",
            name = "Car",
            fields = new object[]
            {
                new { fieldKey = "x", displayName = "X", dataType = "lol", config = new { } }
            },
            confirmed = false
        }, typeStore, authorizer);

        var validation = result.GetProperty("data").GetProperty("validation");
        Assert.False(validation.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task CreateType_with_inline_fields_commit_creates_type_then_each_field()
    {
        var typeStore = new FakeTypeStore();
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "create_record_type", new
        {
            shortCode = "CAR",
            name = "Car",
            fields = new object[]
            {
                new { fieldKey = "model", displayName = "Model", dataType = "text", isRequired = true, sortOrder = 0 },
                new { fieldKey = "year",  displayName = "Year",  dataType = "number", isRequired = false, sortOrder = 10 }
            },
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("record_type_change_committed", result.GetProperty("kind").GetString());
        Assert.Equal(2, result.GetProperty("data").GetProperty("createdFieldCount").GetInt32());

        Assert.Single(typeStore.CreateCalls);
        Assert.Equal(2, typeStore.CreateFieldCalls.Count);
        Assert.Equal("model", typeStore.CreateFieldCalls[0].Input.FieldKey);
        Assert.Equal("year",  typeStore.CreateFieldCalls[1].Input.FieldKey);
    }

    [Fact]
    public async Task CreateType_with_inline_fields_commit_partial_failure_returns_failed_with_partial_state()
    {
        var typeStore = new FakeTypeStore
        {
            CreateFieldThrows = new RecordTypeValidationException("field_key 'year' is already in use for this record type.")
        };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "create_record_type", new
        {
            shortCode = "CAR",
            name = "Car",
            fields = new object[]
            {
                new { fieldKey = "year",  displayName = "Year",  dataType = "number", isRequired = false, sortOrder = 0 }
            },
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("record_type_change_failed", result.GetProperty("kind").GetString());
        Assert.Single(typeStore.CreateCalls); // type WAS created
    }

    [Fact]
    public async Task CreateType_with_inline_fields_requires_DefineFields_authorization()
    {
        var typeStore = new FakeTypeStore();
        var authorizer = new FakeAuthorizer
        {
            Default = AuthEffect.Allow,
            Decisions = { [(Actions.DefineFields, EntityKinds.RecordType, "*")] = AuthEffect.Deny }
        };
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "create_record_type", new
        {
            shortCode = "CAR",
            name = "Car",
            fields = new object[] { new { fieldKey = "x", displayName = "X", dataType = "text" } },
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Empty(typeStore.CreateCalls);
    }

    [Fact]
    public async Task UpdateType_unknown_shortCode_returns_error()
    {
        var typeStore = new FakeTypeStore();
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "update_record_type", new
        {
            typeShortCode = "NOPE",
            name = "X",
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Empty(typeStore.UpdateCalls);
    }

    [Fact]
    public async Task UpdateType_system_type_is_rejected()
    {
        var typeStore = new FakeTypeStore { Types = { SystemType } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "update_record_type", new
        {
            typeShortCode = "SYS",
            name = "Renamed",
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Contains("system type", result.GetProperty("data").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(typeStore.UpdateCalls);
    }

    [Fact]
    public async Task UpdateType_dry_run_returns_before_after_diff()
    {
        var typeStore = new FakeTypeStore { Types = { CarType } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "update_record_type", new
        {
            typeShortCode = "CAR",
            description = "Vehicles in inventory (updated)",
            confirmed = false
        }, typeStore, authorizer);

        Assert.Equal("record_type_change_proposal", result.GetProperty("kind").GetString());
        var data = result.GetProperty("data");
        Assert.Equal("update_type", data.GetProperty("operation").GetString());
        Assert.Equal("Vehicles in inventory", data.GetProperty("before").GetProperty("description").GetString());
        Assert.Equal("Vehicles in inventory (updated)", data.GetProperty("after").GetProperty("description").GetString());
        Assert.Empty(typeStore.UpdateCalls);
    }

    [Fact]
    public async Task UpdateType_commit_layers_patch_onto_current_value()
    {
        var typeStore = new FakeTypeStore { Types = { CarType } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        // Only patching `description` — name should stay "Car".
        await Invoke(skill, "update_record_type", new
        {
            typeShortCode = "CAR",
            description = "Updated",
            confirmed = true
        }, typeStore, authorizer);

        var call = Assert.Single(typeStore.UpdateCalls);
        Assert.Equal(CarTypeId, call.Id);
        Assert.Equal(SessionUserId, call.ActorId);
        Assert.Equal("Car", call.Input.Name); // preserved
        Assert.Equal("Updated", call.Input.Description);
    }

    [Fact]
    public async Task UpdateType_explicit_null_clears_nullable_field()
    {
        var typeStore = new FakeTypeStore { Types = { CarType } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var argsJson = """{"typeShortCode":"CAR","description":null,"confirmed":true}""";
        await InvokeRaw(skill, "update_record_type", argsJson, typeStore, authorizer);

        var call = Assert.Single(typeStore.UpdateCalls);
        Assert.Null(call.Input.Description);
    }

    [Fact]
    public async Task UpdateType_authorizer_denial_returns_error_against_type_instance_id()
    {
        var typeStore = new FakeTypeStore { Types = { CarType } };
        var authorizer = new FakeAuthorizer
        {
            Default = AuthEffect.Allow,
            Decisions = { [(Actions.Edit, EntityKinds.RecordType, CarTypeId.ToString())] = AuthEffect.Deny }
        };
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "update_record_type", new
        {
            typeShortCode = "CAR",
            description = "Updated",
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Empty(typeStore.UpdateCalls);
        Assert.Contains(authorizer.Calls, c => c.Action == Actions.Edit && c.Target.Id == CarTypeId.ToString());
    }

    [Fact]
    public async Task SetTypeArchived_archive_uses_Delete_action()
    {
        var typeStore = new FakeTypeStore { Types = { CarType } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "set_record_type_archived", new
        {
            typeShortCode = "CAR",
            archived = true,
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("record_type_change_committed", result.GetProperty("kind").GetString());
        var call = Assert.Single(typeStore.ArchiveCalls);
        Assert.True(call.Archived);
        Assert.Equal(SessionUserId, call.ActorId);
        Assert.Contains(authorizer.Calls, c => c.Action == Actions.Delete && c.Target.Id == CarTypeId.ToString());
    }

    [Fact]
    public async Task SetTypeArchived_restore_uses_Edit_action()
    {
        var typeStore = new FakeTypeStore { Types = { CarType with { IsArchived = true } } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "set_record_type_archived", new
        {
            typeShortCode = "CAR",
            archived = false,
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("record_type_change_committed", result.GetProperty("kind").GetString());
        var call = Assert.Single(typeStore.ArchiveCalls);
        Assert.False(call.Archived);
        Assert.Contains(authorizer.Calls, c => c.Action == Actions.Edit && c.Target.Id == CarTypeId.ToString());
    }

    [Fact]
    public async Task SetTypeArchived_dry_run_does_not_call_store()
    {
        var typeStore = new FakeTypeStore { Types = { CarType } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "set_record_type_archived", new
        {
            typeShortCode = "CAR",
            archived = true,
            confirmed = false
        }, typeStore, authorizer);

        Assert.Equal("record_type_change_proposal", result.GetProperty("kind").GetString());
        Assert.Empty(typeStore.ArchiveCalls);
    }

    [Fact]
    public async Task SetTypeArchived_system_type_is_rejected()
    {
        var typeStore = new FakeTypeStore { Types = { SystemType } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "set_record_type_archived", new
        {
            typeShortCode = "SYS",
            archived = true,
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Empty(typeStore.ArchiveCalls);
    }

    [Fact]
    public async Task AddField_unknown_type_returns_error()
    {
        var typeStore = new FakeTypeStore();
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "add_record_type_field", new
        {
            typeShortCode = "NOPE",
            fieldKey = "x",
            displayName = "X",
            dataType = "text",
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("error", result.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task AddField_dry_run_returns_proposal_without_calling_store()
    {
        var typeStore = new FakeTypeStore { Types = { CarType }, FieldsByType = { [CarTypeId] = new() { ModelField, YearField } } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "add_record_type_field", new
        {
            typeShortCode = "CAR",
            fieldKey = "vin",
            displayName = "VIN",
            dataType = "text",
            isRequired = true,
            confirmed = false
        }, typeStore, authorizer);

        Assert.Equal("record_type_change_proposal", result.GetProperty("kind").GetString());
        Assert.Equal("add_field", result.GetProperty("data").GetProperty("operation").GetString());
        Assert.Empty(typeStore.CreateFieldCalls);
    }

    [Fact]
    public async Task AddField_dry_run_defaults_sortOrder_to_max_plus_ten()
    {
        var typeStore = new FakeTypeStore { Types = { CarType }, FieldsByType = { [CarTypeId] = new() { ModelField, YearField } } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "add_record_type_field", new
        {
            typeShortCode = "CAR",
            fieldKey = "vin",
            displayName = "VIN",
            dataType = "text",
            // no sortOrder
            confirmed = false
        }, typeStore, authorizer);

        Assert.Equal(20, result.GetProperty("data").GetProperty("after").GetProperty("sortOrder").GetInt32());
    }

    [Fact]
    public async Task AddField_commit_calls_CreateFieldAsync_and_uses_DefineFields_auth()
    {
        var typeStore = new FakeTypeStore { Types = { CarType }, FieldsByType = { [CarTypeId] = new() { ModelField } } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "add_record_type_field", new
        {
            typeShortCode = "CAR",
            fieldKey = "vin",
            displayName = "VIN",
            dataType = "text",
            isRequired = true,
            sortOrder = 30,
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("record_type_change_committed", result.GetProperty("kind").GetString());
        var call = Assert.Single(typeStore.CreateFieldCalls);
        Assert.Equal(CarTypeId, call.TypeId);
        Assert.Equal("vin", call.Input.FieldKey);
        Assert.Equal(30, call.Input.SortOrder);
        Assert.True(call.Input.IsRequired);
        Assert.Contains(authorizer.Calls, c => c.Action == Actions.DefineFields && c.Target.Id == CarTypeId.ToString());
    }

    [Fact]
    public async Task AddField_invalid_option_config_returns_failed_envelope_in_dry_run()
    {
        var typeStore = new FakeTypeStore { Types = { CarType }, FieldsByType = { [CarTypeId] = new() } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "add_record_type_field", new
        {
            typeShortCode = "CAR",
            fieldKey = "color",
            displayName = "Color",
            dataType = "option",
            config = new { },
            confirmed = false
        }, typeStore, authorizer);

        var validation = result.GetProperty("data").GetProperty("validation");
        Assert.False(validation.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task AddField_system_type_is_rejected()
    {
        var typeStore = new FakeTypeStore { Types = { SystemType } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "add_record_type_field", new
        {
            typeShortCode = "SYS",
            fieldKey = "x",
            displayName = "X",
            dataType = "text",
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Empty(typeStore.CreateFieldCalls);
    }

    [Fact]
    public async Task UpdateField_unknown_field_returns_error()
    {
        var typeStore = new FakeTypeStore { Types = { CarType }, FieldsByType = { [CarTypeId] = new() { ModelField } } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "update_record_type_field", new
        {
            typeShortCode = "CAR",
            fieldKey = "nope",
            displayName = "New",
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Empty(typeStore.UpdateFieldCalls);
    }

    [Fact]
    public async Task UpdateField_dry_run_returns_per_attribute_diff()
    {
        var typeStore = new FakeTypeStore { Types = { CarType }, FieldsByType = { [CarTypeId] = new() { ModelField } } };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "update_record_type_field", new
        {
            typeShortCode = "CAR",
            fieldKey = "model",
            displayName = "Make/Model",
            isRequired = false,
            confirmed = false
        }, typeStore, authorizer);

        Assert.Equal("record_type_change_proposal", result.GetProperty("kind").GetString());
        var changes = result.GetProperty("data").GetProperty("fieldChanges");
        var changeKeys = changes.EnumerateArray().Select(e => e.GetProperty("attribute").GetString()).ToArray();
        Assert.Contains("displayName", changeKeys);
        Assert.Contains("isRequired", changeKeys);
        Assert.Empty(typeStore.UpdateFieldCalls);
    }

    [Fact]
    public async Task UpdateField_commit_layers_patch_onto_existing_values()
    {
        var typeStore = new FakeTypeStore
        {
            Types = { CarType },
            FieldsByType = { [CarTypeId] = new() { ModelField } }
        };
        var authorizer = new FakeAuthorizer();
        var skill = new ManageRecordTypesSkill();

        // Only patching displayName — isRequired/sortOrder/config should keep current value.
        await Invoke(skill, "update_record_type_field", new
        {
            typeShortCode = "CAR",
            fieldKey = "model",
            displayName = "Make/Model",
            confirmed = true
        }, typeStore, authorizer);

        var call = Assert.Single(typeStore.UpdateFieldCalls);
        Assert.Equal(ModelField.Id, call.FieldId);
        Assert.Equal(SessionUserId, call.ActorId);
        Assert.Equal("Make/Model", call.Input.DisplayName);
        Assert.True(call.Input.IsRequired);     // preserved
        Assert.Equal(0, call.Input.SortOrder);  // preserved
    }

    [Fact]
    public async Task UpdateField_authorizer_denial_returns_error()
    {
        var typeStore = new FakeTypeStore { Types = { CarType }, FieldsByType = { [CarTypeId] = new() { ModelField } } };
        var authorizer = new FakeAuthorizer
        {
            Default = AuthEffect.Allow,
            Decisions = { [(Actions.DefineFields, EntityKinds.RecordType, CarTypeId.ToString())] = AuthEffect.Deny }
        };
        var skill = new ManageRecordTypesSkill();

        var result = await Invoke(skill, "update_record_type_field", new
        {
            typeShortCode = "CAR",
            fieldKey = "model",
            displayName = "X",
            confirmed = true
        }, typeStore, authorizer);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Empty(typeStore.UpdateFieldCalls);
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
