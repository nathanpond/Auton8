using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Agent.Skills;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Records.Fields;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
// Disambiguate against Xunit.Record.
using Record = AutoNate.Web.Models.Records.Record;

namespace AutoNate.Web.Tests;

public sealed class ManageRecordsSkillTests
{
    private static readonly Guid AccTypeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid IncRecordId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SessionUserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static readonly RecordType AccType = new()
    {
        Id = AccTypeId,
        ShortCode = "ACC",
        Name = "Account",
        Description = "Customer accounts"
    };

    private static readonly RecordTypeField NameField = new()
    {
        Id = Guid.NewGuid(),
        RecordTypeId = AccTypeId,
        FieldKey = "email",
        DisplayName = "Email",
        DataType = "text",
        IsRequired = false,
        Config = ParseElement("{}"),
        SortOrder = 0
    };

    private static readonly RecordTypeField CompanyField = new()
    {
        Id = Guid.NewGuid(),
        RecordTypeId = AccTypeId,
        FieldKey = "company",
        DisplayName = "Company",
        DataType = "text",
        IsRequired = true,
        Config = ParseElement("{}"),
        SortOrder = 1
    };

    [Fact]
    public async Task Create_with_unknown_typeShortCode_returns_error_envelope()
    {
        var typeStore = new FakeTypeStore();
        var recordStore = new FakeRecordStore();
        var skill = new ManageRecordsSkill();

        var result = await Invoke(skill, "create_record", new
        {
            typeShortCode = "XYZ",
            name = "Acme",
            confirmed = false
        }, typeStore, recordStore);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Empty(recordStore.CreateCalls);
    }

    [Fact]
    public async Task Create_with_confirmed_false_returns_proposal_and_does_NOT_call_CreateAsync()
    {
        var typeStore = new FakeTypeStore { Types = { AccType }, FieldsByType = { [AccTypeId] = new[] { NameField, CompanyField } } };
        var recordStore = new FakeRecordStore();
        var skill = new ManageRecordsSkill();

        var result = await Invoke(skill, "create_record", new
        {
            typeShortCode = "ACC",
            name = "Acme Corp",
            values = new { email = "contact@acme.com", company = "Acme Co" },
            status = "active",
            confirmed = false
        }, typeStore, recordStore);

        Assert.Equal("record_change_proposal", result.GetProperty("kind").GetString());
        var data = result.GetProperty("data");
        Assert.Equal("create", data.GetProperty("operation").GetString());
        Assert.Equal("ACC", data.GetProperty("typeShortCode").GetString());
        Assert.Equal("Acme Corp", data.GetProperty("name").GetString());
        Assert.True(data.GetProperty("validation").GetProperty("ok").GetBoolean());

        Assert.Empty(recordStore.CreateCalls); // SERVER-ENFORCED: dry-run never mutates.
    }

    [Fact]
    public async Task Create_dry_run_flags_missing_required_fields_in_validation_errors()
    {
        var typeStore = new FakeTypeStore { Types = { AccType }, FieldsByType = { [AccTypeId] = new[] { NameField, CompanyField } } };
        var recordStore = new FakeRecordStore();
        var skill = new ManageRecordsSkill();

        // Missing the required "company" field.
        var result = await Invoke(skill, "create_record", new
        {
            typeShortCode = "ACC",
            name = "Acme Corp",
            values = new { email = "contact@acme.com" },
            confirmed = false
        }, typeStore, recordStore);

        var validation = result.GetProperty("data").GetProperty("validation");
        Assert.False(validation.GetProperty("ok").GetBoolean());
        var errors = validation.GetProperty("errors");
        Assert.True(errors.GetArrayLength() >= 1);
        Assert.Contains("Company", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Create_with_confirmed_true_calls_CreateAsync_once_with_session_userId()
    {
        var typeStore = new FakeTypeStore { Types = { AccType }, FieldsByType = { [AccTypeId] = new[] { NameField, CompanyField } } };
        var recordStore = new FakeRecordStore
        {
            CreateResponse = new Record
            {
                Id = Guid.NewGuid(),
                RecordTypeId = AccTypeId,
                Key = "ACC-42",
                Name = "Acme Corp",
                Status = "active",
                Values = ParseElement("{}"),
                CreatedAtUtc = DateTimeOffset.UtcNow
            }
        };
        var skill = new ManageRecordsSkill();

        var result = await Invoke(skill, "create_record", new
        {
            typeShortCode = "ACC",
            name = "Acme Corp",
            values = new { company = "Acme Co" },
            status = "active",
            confirmed = true
        }, typeStore, recordStore);

        Assert.Equal("record_change_committed", result.GetProperty("kind").GetString());
        Assert.Equal("ACC-42", result.GetProperty("data").GetProperty("key").GetString());

        var call = Assert.Single(recordStore.CreateCalls);
        Assert.Equal(SessionUserId, call.ActorId);
        Assert.Equal(AccTypeId, call.Input.RecordTypeId);
        Assert.Equal("Acme Corp", call.Input.Name);
        Assert.Equal("active", call.Input.Status);
    }

    [Fact]
    public async Task Create_commit_surfaces_RecordValidationException_as_failed_envelope()
    {
        var typeStore = new FakeTypeStore { Types = { AccType }, FieldsByType = { [AccTypeId] = new[] { NameField, CompanyField } } };
        var recordStore = new FakeRecordStore
        {
            CreateThrows = new RecordValidationException("invalid", new[]
            {
                new FieldValidationError("required", "Field 'company' is required.")
            })
        };
        var skill = new ManageRecordsSkill();

        var result = await Invoke(skill, "create_record", new
        {
            typeShortCode = "ACC",
            name = "Acme Corp",
            confirmed = true
        }, typeStore, recordStore);

        Assert.Equal("record_change_failed", result.GetProperty("kind").GetString());
        var validation = result.GetProperty("data").GetProperty("validation");
        Assert.False(validation.GetProperty("ok").GetBoolean());
        Assert.Equal("required", validation.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Update_with_confirmed_false_returns_diff_envelope_and_does_NOT_call_UpdateAsync()
    {
        var existing = new Record
        {
            Id = IncRecordId,
            RecordTypeId = AccTypeId,
            Key = "ACC-42",
            Name = "Acme Corp",
            Status = "active",
            Values = ParseElement("""{"email":"old@acme.com","company":"Acme Co"}"""),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var typeStore = new FakeTypeStore { Types = { AccType }, FieldsByType = { [AccTypeId] = new[] { NameField, CompanyField } } };
        var recordStore = new FakeRecordStore { GetByKeyResponse = existing };
        var skill = new ManageRecordsSkill();

        var result = await Invoke(skill, "update_record", new
        {
            key = "ACC-42",
            values = new { email = "new@acme.com" },
            confirmed = false
        }, typeStore, recordStore);

        Assert.Equal("record_change_proposal", result.GetProperty("kind").GetString());
        var changes = result.GetProperty("data").GetProperty("fieldChanges");
        Assert.Equal(1, changes.GetArrayLength());
        Assert.Equal("email", changes[0].GetProperty("key").GetString());
        Assert.Equal("old@acme.com", changes[0].GetProperty("before").GetString());
        Assert.Equal("new@acme.com", changes[0].GetProperty("after").GetString());

        Assert.Empty(recordStore.UpdateCalls);
    }

    [Fact]
    public async Task Update_uses_Optional_NotProvided_when_status_arg_omitted()
    {
        var existing = new Record
        {
            Id = IncRecordId,
            RecordTypeId = AccTypeId,
            Key = "ACC-42",
            Name = "Acme",
            Status = "active",
            Values = ParseElement("{}"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var typeStore = new FakeTypeStore { Types = { AccType }, FieldsByType = { [AccTypeId] = new[] { NameField } } };
        var recordStore = new FakeRecordStore { GetByKeyResponse = existing, UpdateResponse = existing };
        var skill = new ManageRecordsSkill();

        await Invoke(skill, "update_record", new
        {
            key = "ACC-42",
            // no status property
            values = new { email = "x@y.com" },
            confirmed = true
        }, typeStore, recordStore);

        var call = Assert.Single(recordStore.UpdateCalls);
        Assert.False(call.Input.Status.HasValue);   // Optional<string?>.None — store keeps existing status
        Assert.False(call.Input.DueDate.HasValue);  // Optional<DateOnly?>.None
    }

    [Fact]
    public async Task Update_uses_Optional_Some_null_when_status_arg_is_explicit_null()
    {
        var existing = new Record
        {
            Id = IncRecordId,
            RecordTypeId = AccTypeId,
            Key = "ACC-42",
            Name = "Acme",
            Status = "active",
            Values = ParseElement("{}"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var typeStore = new FakeTypeStore { Types = { AccType }, FieldsByType = { [AccTypeId] = new[] { NameField } } };
        var recordStore = new FakeRecordStore { GetByKeyResponse = existing, UpdateResponse = existing };
        var skill = new ManageRecordsSkill();

        // Use a JSON literal so we can express explicit null.
        var argsJson = """{"key":"ACC-42","status":null,"confirmed":true}""";
        await InvokeRaw(skill, "update_record", argsJson, typeStore, recordStore);

        var call = Assert.Single(recordStore.UpdateCalls);
        Assert.True(call.Input.Status.HasValue);     // explicitly provided
        Assert.Null(call.Input.Status.Value);        // ...as null = clear
    }

    [Fact]
    public async Task Update_with_confirmed_true_calls_UpdateAsync_with_session_userId()
    {
        var existing = new Record
        {
            Id = IncRecordId,
            RecordTypeId = AccTypeId,
            Key = "ACC-42",
            Name = "Acme",
            Status = "active",
            Values = ParseElement("{}"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var typeStore = new FakeTypeStore { Types = { AccType }, FieldsByType = { [AccTypeId] = new[] { NameField } } };
        var recordStore = new FakeRecordStore { GetByKeyResponse = existing, UpdateResponse = existing };
        var skill = new ManageRecordsSkill();

        await Invoke(skill, "update_record", new
        {
            key = "ACC-42",
            name = "Acme Corp",
            confirmed = true
        }, typeStore, recordStore);

        var call = Assert.Single(recordStore.UpdateCalls);
        Assert.Equal(SessionUserId, call.ActorId);
        Assert.Equal(IncRecordId, call.RecordId);
        Assert.Equal("Acme Corp", call.Input.Name);
    }

    // --- helpers / fakes ---

    private static async Task<JsonElement> Invoke(
        ManageRecordsSkill skill,
        string toolName,
        object args,
        FakeTypeStore typeStore,
        FakeRecordStore recordStore)
    {
        var argsJson = JsonSerializer.Serialize(args);
        return await InvokeRaw(skill, toolName, argsJson, typeStore, recordStore);
    }

    private static async Task<JsonElement> InvokeRaw(
        ManageRecordsSkill skill,
        string toolName,
        string argsJson,
        FakeTypeStore typeStore,
        FakeRecordStore recordStore)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var tool = skill.Tools.Single(t => t.Name == toolName);

        var services = new ServiceCollection();
        services.AddSingleton<IRecordTypeStore>(typeStore);
        services.AddSingleton<IRecordStore>(recordStore);
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
        public Dictionary<Guid, IReadOnlyList<RecordTypeField>> FieldsByType { get; } = new();

        public Task<IReadOnlyList<RecordType>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RecordType>>(Types);
        public Task<RecordType?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Types.FirstOrDefault(t => t.Id == id));
        public Task<RecordType?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Types.FirstOrDefault(t => t.ShortCode == shortCode));
        public Task<IReadOnlyList<RecordTypeField>> ListFieldsAsync(Guid recordTypeId, bool includeArchived, CancellationToken cancellationToken = default) =>
            Task.FromResult(FieldsByType.TryGetValue(recordTypeId, out var f) ? f : Array.Empty<RecordTypeField>());

        // Methods this test class doesn't exercise — return defaults so the
        // fake is robust if the surface under test grows new call sites.
        public Task<RecordType> CreateAsync(CreateRecordTypeInput input, Guid actorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecordType());
        public Task<RecordType> UpdateAsync(Guid id, UpdateRecordTypeInput input, Guid actorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecordType());
        public Task<RecordType> SetArchivedAsync(Guid id, bool archived, Guid actorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecordType());
        public Task<RecordTypeField?> GetFieldAsync(Guid recordTypeId, Guid fieldId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RecordTypeField?>(null);
        public Task<RecordTypeField> CreateFieldAsync(Guid recordTypeId, CreateRecordTypeFieldInput input, Guid actorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecordTypeField());
        public Task<RecordTypeField> UpdateFieldAsync(Guid recordTypeId, Guid fieldId, UpdateRecordTypeFieldInput input, Guid actorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecordTypeField());
        public Task<RecordTypeField> SetFieldArchivedAsync(Guid recordTypeId, Guid fieldId, bool archived, Guid actorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecordTypeField());
        public Task<IReadOnlyList<RecordTypeAuditEntry>> ListAuditAsync(Guid recordTypeId, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RecordTypeAuditEntry>>(Array.Empty<RecordTypeAuditEntry>());
    }

    private sealed class FakeRecordStore : IRecordStore
    {
        public List<(CreateRecordInput Input, Guid ActorId)> CreateCalls { get; } = new();
        public List<(Guid RecordId, UpdateRecordInput Input, Guid ActorId)> UpdateCalls { get; } = new();

        public Record? CreateResponse { get; set; }
        public Record? UpdateResponse { get; set; }
        public Record? GetByKeyResponse { get; set; }

        public RecordValidationException? CreateThrows { get; set; }
        public RecordValidationException? UpdateThrows { get; set; }

        public Task<Record?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(GetByKeyResponse);

        public Task<Record> CreateAsync(CreateRecordInput input, Guid actorId, CancellationToken cancellationToken = default)
        {
            CreateCalls.Add((input, actorId));
            if (CreateThrows is not null) throw CreateThrows;
            return Task.FromResult(CreateResponse ?? throw new InvalidOperationException("CreateResponse not set"));
        }

        public Task<Record> UpdateAsync(Guid id, UpdateRecordInput input, Guid actorId, CancellationToken cancellationToken = default)
        {
            UpdateCalls.Add((id, input, actorId));
            if (UpdateThrows is not null) throw UpdateThrows;
            return Task.FromResult(UpdateResponse ?? throw new InvalidOperationException("UpdateResponse not set"));
        }

        public Task<Record?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Record?>(null);
        public Task<Record> SetArchivedAsync(Guid id, bool archived, Guid actorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Record());
        public Task<RecordListPage> SearchAsync(RecordSearchInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(EmptyPage(input.Page, input.PageSize));
        public Task<RecordListPage> SearchAsync(RecordSearchInput input, ClaimsPrincipal actor, CancellationToken cancellationToken = default) =>
            Task.FromResult(EmptyPage(input.Page, input.PageSize));
        public Task<RecordListPage> SearchAssignedAsync(Guid assigneeId, int page, int pageSize, bool includeArchived, string? sort, CancellationToken cancellationToken = default) =>
            Task.FromResult(EmptyPage(page, pageSize));
        public Task<RecordListPage> SearchAssignedAsync(Guid assigneeId, int page, int pageSize, bool includeArchived, string? sort, ClaimsPrincipal actor, CancellationToken cancellationToken = default) =>
            Task.FromResult(EmptyPage(page, pageSize));
        public Task<RecordListPage> ListAuthorizedAsync(ClaimsPrincipal actor, Guid? recordTypeId, int page, int pageSize, bool includeArchived, CancellationToken cancellationToken = default) =>
            Task.FromResult(EmptyPage(page, pageSize));

        private static RecordListPage EmptyPage(int page, int pageSize) =>
            new(Array.Empty<Record>(), 0, page, pageSize);
    }
}
