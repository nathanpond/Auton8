# Agent Record-Type Mutation Skill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `ManageRecordTypesSkill` to the chatbot agent so it can author and edit record types — with a confirm-before-commit gate, skill-level authorization, and a guard against modifying system types.

**Architecture:** New `IAgentSkill` implementation that exposes six tools (`create_record_type`, `update_record_type`, `set_record_type_archived`, `add_record_type_field`, `update_record_type_field`, `set_record_type_field_archived`). Each tool accepts a `confirmed: bool` arg; `confirmed=false` returns a structured proposal envelope, `confirmed=true` calls the matching `IRecordTypeStore` method. Authorization checks (`IAuthorizer`) and `IsSystem` guards run before either path so a denied dry-run returns `error` and never narrates a hypothetical change. Mirrors `ManageRecordsSkill` shape exactly.

**Tech Stack:** C# / .NET, xUnit, `IRecordTypeStore`, `IAuthorizer`, `IFieldTypeRegistry`. No SPA changes, no DB schema changes, no new endpoints.

**Spec:** `docs/superpowers/specs/2026-05-08-agent-record-type-mutation-skill-design.md`

---

## File Map

| Action | Path | Responsibility |
|---|---|---|
| Create | `src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs` | The skill — six tools, dry-run/commit logic, auth + system-type guards, envelope construction. |
| Create | `tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs` | xUnit tests with fakes for `IRecordTypeStore`, `IAuthorizer`, and `IFieldTypeRegistry`. |
| Modify | `src/AutoNate.Web/Program.cs` | One DI line: `AddScoped<IAgentSkill, ManageRecordTypesSkill>()` next to the existing `ManageRecordsSkill` registration. |

---

## Task 1: Test scaffolding (fakes for store, authorizer, field-type registry)

**Files:**
- Create: `tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs`

This task lays down the test class skeleton plus reusable fakes. We do not write the skill yet — instead we write a single deliberately-failing test that proves the harness wires up correctly, then implement the skill skeleton in Task 2 to pass it.

- [ ] **Step 1: Create the test file with shared constants, fakes, and one failing skeleton test**

```csharp
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
            Task.FromResult(new CapabilitySummary());
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
```

Look at `tests/AutoNate.Web.Tests/ManageRecordsSkillTests.cs` for the fake-store pattern this is modeled on.

- [ ] **Step 2: Run test to verify it fails (skill class doesn't exist yet)**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ManageRecordTypesSkill"`

Expected: Build error — `ManageRecordTypesSkill` does not exist. **Don't proceed past the build error to a runtime failure** — the build error is the failing red.

- [ ] **Step 3: No commit yet** — we'll commit at the end of Task 2 once the skill skeleton makes this test pass.

---

## Task 2: Skill skeleton + DI registration

**Files:**
- Create: `src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs`
- Modify: `src/AutoNate.Web/Program.cs`

Goal: empty-but-typed skill class that compiles, exposes the six tool names, and is registered in DI. Tools all return a placeholder error so subsequent tasks can replace them one at a time.

- [ ] **Step 1: Create the skill class with six placeholder tools**

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Records.Fields;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// Second mutating skill (after ManageRecordsSkill). Authors and edits record
// types and their fields, with a server-enforced confirmed:bool gate. Layout
// mirrors ManageRecordsSkill exactly: dry-run returns a structured proposal
// envelope; commit goes through IRecordTypeStore. Two extra guards live in
// this skill that the records skill doesn't need: explicit IAuthorizer checks
// (because IRecordTypeStore does NOT gate on authorizer — only HTTP endpoints
// do), and an IsSystem refusal (because nothing else stops a system-type
// mutation today).
public sealed class ManageRecordTypesSkill : IAgentSkill
{
    public const string CreateTypeToolName = "create_record_type";
    public const string UpdateTypeToolName = "update_record_type";
    public const string SetTypeArchivedToolName = "set_record_type_archived";
    public const string AddFieldToolName = "add_record_type_field";
    public const string UpdateFieldToolName = "update_record_type_field";
    public const string SetFieldArchivedToolName = "set_record_type_field_archived";

    public string Name => "manage-record-types";

    public string Description =>
        "Create new record types and edit existing ones (metadata, fields, archive state), with mandatory user confirmation before each commit.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public ManageRecordTypesSkill()
    {
        Tools = new[]
        {
            new AgentTool(CreateTypeToolName,        "placeholder", ParseSchema("""{"type":"object"}"""), NotImplementedAsync),
            new AgentTool(UpdateTypeToolName,        "placeholder", ParseSchema("""{"type":"object"}"""), NotImplementedAsync),
            new AgentTool(SetTypeArchivedToolName,   "placeholder", ParseSchema("""{"type":"object"}"""), NotImplementedAsync),
            new AgentTool(AddFieldToolName,          "placeholder", ParseSchema("""{"type":"object"}"""), NotImplementedAsync),
            new AgentTool(UpdateFieldToolName,       "placeholder", ParseSchema("""{"type":"object"}"""), NotImplementedAsync),
            new AgentTool(SetFieldArchivedToolName,  "placeholder", ParseSchema("""{"type":"object"}"""), NotImplementedAsync),
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) => null;

    private static Task<JsonElement> NotImplementedAsync(JsonElement args, AgentToolContext context, CancellationToken ct) =>
        Task.FromResult(JsonSerializer.SerializeToElement(new { kind = "error", source = "ManageRecordTypesSkill", data = new { message = "not implemented" } }));

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
```

- [ ] **Step 2: Register the skill in DI**

Open `src/AutoNate.Web/Program.cs`. Find the line `builder.Services.AddScoped<IAgentSkill, ManageRecordsSkill>();` (around line 257). Add directly after it:

```csharp
// Mutating record-type schema skill. Same confirmed-gate contract as
// ManageRecordsSkill, plus skill-level IAuthorizer checks (the type store
// is unauthorized — endpoints enforce permissions today) and an IsSystem
// refusal that no other layer guards.
builder.Services.AddScoped<IAgentSkill, ManageRecordTypesSkill>();
```

- [ ] **Step 3: Run the Task 1 test to verify it passes**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ManageRecordTypesSkill"`

Expected: 1 passed, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs \
        src/AutoNate.Web/Program.cs \
        tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs
git commit -m "Scaffold ManageRecordTypesSkill with six placeholder tools"
```

---

## Task 3: `create_record_type` — the bare type (no inline fields)

**Files:**
- Modify: `src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs`
- Modify: `tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs`

This task implements `create_record_type` for the case where `fields[]` is omitted. Inline-fields support is Task 4. Auth and IsSystem guards are full-strength here — they get reused by every later tool.

- [ ] **Step 1: Write the failing tests (proposal, commit, kind-level auth denial, validation passthrough)**

Add to `ManageRecordTypesSkillTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ManageRecordTypesSkill"`

Expected: 4 new tests fail with `kind=error / data.message=not implemented`.

- [ ] **Step 3: Replace the placeholder `create_record_type` tool**

In `ManageRecordTypesSkill.cs`, replace the `CreateTypeToolName` entry in `Tools` with the real schema and invoker, and add the implementation methods at the bottom. The full code block:

```csharp
// Replace the AgentTool(CreateTypeToolName, ...) entry in the Tools array:
new AgentTool(
    Name: CreateTypeToolName,
    Description: "Create a new record type (the schema for a category of records). ALWAYS call with confirmed=false first to preview the change. Only call with confirmed=true after the user has explicitly approved the proposal. Optionally include fields[] to create the type with an initial schema in one shot.",
    JsonSchema: ParseSchema("""
        {
          "type": "object",
          "properties": {
            "shortCode":   { "type": "string", "description": "2-8 chars, starts with a letter, then letters or digits." },
            "name":        { "type": "string" },
            "description": { "type": ["string", "null"] },
            "icon":        { "type": ["string", "null"] },
            "color":       { "type": ["string", "null"] },
            "fields": {
              "type": "array",
              "description": "Optional initial fields. Each is created in sequence after the type itself. If a later field fails, the type and any earlier fields stay.",
              "items": {
                "type": "object",
                "properties": {
                  "fieldKey":    { "type": "string" },
                  "displayName": { "type": "string" },
                  "dataType":    { "type": "string", "enum": ["text","number","date","phone","email","option","boolean"] },
                  "config":      { "type": "object" },
                  "isRequired":  { "type": "boolean" },
                  "sortOrder":   { "type": "integer" }
                },
                "required": ["fieldKey","displayName","dataType"]
              }
            },
            "confirmed":   { "type": "boolean" }
          },
          "required": ["shortCode","name"],
          "additionalProperties": false
        }
        """),
    Invoke: InvokeCreateTypeAsync),
```

Add these methods to the class (Task 4 will extend `InvokeCreateTypeAsync` for inline-fields support; for now keep it bare):

```csharp
private static async Task<JsonElement> InvokeCreateTypeAsync(
    JsonElement args,
    AgentToolContext context,
    CancellationToken ct)
{
    var shortCode = ReadRequiredString(args, "shortCode");
    if (shortCode is null) return Error(CreateTypeToolName, "shortCode is required.");

    var name = ReadRequiredString(args, "name");
    if (name is null) return Error(CreateTypeToolName, "name is required.");

    var description = ReadOptionalString(args, "description");
    var icon = ReadOptionalString(args, "icon");
    var color = ReadOptionalString(args, "color");

    var authorizer = context.Services.GetRequiredService<IAuthorizer>();
    var allowed = await authorizer.AuthorizeAsync(
        context.Session.User,
        Actions.Create,
        new EntityRef(EntityKinds.RecordType, "*"),
        ct);
    if (!allowed.IsAllowed)
    {
        return Error(CreateTypeToolName, $"Not authorized to create record types ({allowed.Reason}).");
    }

    var confirmed = args.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True;

    if (!confirmed)
    {
        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_type_change_proposal",
            source = "ManageRecordTypesSkill",
            data = new
            {
                operation = "create_type",
                summary = $"Create record type {shortCode}: '{name}'.",
                after = new { shortCode, name, description, icon, color },
                validation = new { ok = true, errors = Array.Empty<object>() }
            }
        });
    }

    var typeStore = context.Services.GetRequiredService<IRecordTypeStore>();
    try
    {
        var created = await typeStore.CreateAsync(
            new CreateRecordTypeInput(shortCode, name, description, icon, color),
            context.Session.UserId,
            ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_type_change_committed",
            source = "ManageRecordTypesSkill",
            data = new
            {
                operation = "create_type",
                id = created.Id,
                shortCode = created.ShortCode,
                createdFieldCount = 0
            }
        });
    }
    catch (RecordTypeValidationException ex)
    {
        return Failed("create_type", ex);
    }
}

// --- helpers ---

private static string? ReadRequiredString(JsonElement args, string property)
{
    if (!args.TryGetProperty(property, out var prop)) return null;
    if (prop.ValueKind != JsonValueKind.String) return null;
    var s = prop.GetString();
    return string.IsNullOrWhiteSpace(s) ? null : s;
}

private static string? ReadOptionalString(JsonElement args, string property)
{
    if (!args.TryGetProperty(property, out var prop)) return null;
    if (prop.ValueKind == JsonValueKind.Null) return null;
    if (prop.ValueKind != JsonValueKind.String) return null;
    var s = prop.GetString();
    return string.IsNullOrWhiteSpace(s) ? null : s;
}

private static JsonElement Error(string source, string message) =>
    JsonSerializer.SerializeToElement(new
    {
        kind = "error",
        source,
        data = new { message }
    });

private static JsonElement Failed(string operation, RecordTypeValidationException ex) =>
    JsonSerializer.SerializeToElement(new
    {
        kind = "record_type_change_failed",
        source = "ManageRecordTypesSkill",
        data = new
        {
            operation,
            message = ex.Message,
            validation = new
            {
                ok = false,
                errors = new[] { new { code = "validation", message = ex.Message } }
            }
        }
    });
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ManageRecordTypesSkill"`

Expected: 5 passed (1 skeleton + 4 new), 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs \
        tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs
git commit -m "Implement create_record_type for the bare-type case"
```

---

## Task 4: `create_record_type` — inline fields

**Files:**
- Modify: `src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs`
- Modify: `tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs`

Extend `create_record_type` to accept and process `fields[]`. Validate every field's `dataType` and `config` via `IFieldTypeRegistry` in dry-run; on commit, create the type then loop `CreateFieldAsync`. Authorize `Actions.DefineFields` only when `fields[]` is non-empty. Surface partial-success cases (some fields land, a later one fails) in `record_type_change_failed`.

- [ ] **Step 1: Write the failing tests**

Add to `ManageRecordTypesSkillTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ManageRecordTypesSkill"`

Expected: 6 new tests fail; the bare-type tests still pass.

- [ ] **Step 3: Extend `InvokeCreateTypeAsync` to handle inline fields**

Replace the entire `InvokeCreateTypeAsync` method with this version, and add the helper `ReadFieldArray` below it:

```csharp
private static async Task<JsonElement> InvokeCreateTypeAsync(
    JsonElement args,
    AgentToolContext context,
    CancellationToken ct)
{
    var shortCode = ReadRequiredString(args, "shortCode");
    if (shortCode is null) return Error(CreateTypeToolName, "shortCode is required.");

    var name = ReadRequiredString(args, "name");
    if (name is null) return Error(CreateTypeToolName, "name is required.");

    var description = ReadOptionalString(args, "description");
    var icon = ReadOptionalString(args, "icon");
    var color = ReadOptionalString(args, "color");

    var fieldInputs = ReadFieldArray(args, "fields", out var fieldParseError);
    if (fieldParseError is not null) return Error(CreateTypeToolName, fieldParseError);

    var authorizer = context.Services.GetRequiredService<IAuthorizer>();
    var createDecision = await authorizer.AuthorizeAsync(
        context.Session.User, Actions.Create, new EntityRef(EntityKinds.RecordType, "*"), ct);
    if (!createDecision.IsAllowed)
        return Error(CreateTypeToolName, $"Not authorized to create record types ({createDecision.Reason}).");

    if (fieldInputs.Count > 0)
    {
        var defineDecision = await authorizer.AuthorizeAsync(
            context.Session.User, Actions.DefineFields, new EntityRef(EntityKinds.RecordType, "*"), ct);
        if (!defineDecision.IsAllowed)
            return Error(CreateTypeToolName, $"Not authorized to define fields on record types ({defineDecision.Reason}).");
    }

    // Dry-run validation: normalize each field's config via the registry.
    var registry = context.Services.GetRequiredService<IFieldTypeRegistry>();
    var validationErrors = new List<object>();
    var normalizedFields = new List<(FieldInput Raw, JsonElement NormalizedConfig)>();
    foreach (var field in fieldInputs)
    {
        if (!registry.TryGet(field.DataType, out var fieldType))
        {
            validationErrors.Add(new { code = "unknown_data_type", fieldKey = field.FieldKey, message = $"Unknown data_type '{field.DataType}'." });
            continue;
        }
        try
        {
            var normalized = fieldType.NormalizeConfig(field.Config);
            normalizedFields.Add((field, normalized));
        }
        catch (FieldConfigException ex)
        {
            validationErrors.Add(new { code = "field_config", fieldKey = field.FieldKey, message = ex.Message });
        }
    }

    var confirmed = args.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True;

    if (!confirmed)
    {
        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_type_change_proposal",
            source = "ManageRecordTypesSkill",
            data = new
            {
                operation = "create_type",
                summary = BuildCreateTypeSummary(shortCode, name, fieldInputs),
                after = new
                {
                    shortCode, name, description, icon, color,
                    fields = fieldInputs.Select((f, i) => new
                    {
                        fieldKey = f.FieldKey,
                        displayName = f.DisplayName,
                        dataType = f.DataType,
                        isRequired = f.IsRequired,
                        sortOrder = f.SortOrder ?? (i * 10)
                    }).ToArray()
                },
                validation = new
                {
                    ok = validationErrors.Count == 0,
                    errors = validationErrors.ToArray()
                }
            }
        });
    }

    if (validationErrors.Count > 0)
    {
        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_type_change_failed",
            source = "ManageRecordTypesSkill",
            data = new
            {
                operation = "create_type",
                message = "One or more fields failed validation.",
                validation = new { ok = false, errors = validationErrors.ToArray() }
            }
        });
    }

    var typeStore = context.Services.GetRequiredService<IRecordTypeStore>();
    RecordType created;
    try
    {
        created = await typeStore.CreateAsync(
            new CreateRecordTypeInput(shortCode, name, description, icon, color),
            context.Session.UserId,
            ct);
    }
    catch (RecordTypeValidationException ex)
    {
        return Failed("create_type", ex);
    }

    var createdFieldCount = 0;
    foreach (var (raw, normalizedConfig) in normalizedFields)
    {
        try
        {
            await typeStore.CreateFieldAsync(
                created.Id,
                new CreateRecordTypeFieldInput(
                    raw.FieldKey,
                    raw.DisplayName,
                    raw.DataType,
                    normalizedConfig,
                    raw.IsRequired,
                    raw.SortOrder ?? (createdFieldCount * 10)),
                context.Session.UserId,
                ct);
            createdFieldCount++;
        }
        catch (RecordTypeValidationException ex)
        {
            return JsonSerializer.SerializeToElement(new
            {
                kind = "record_type_change_failed",
                source = "ManageRecordTypesSkill",
                data = new
                {
                    operation = "create_type",
                    message = $"Type '{created.ShortCode}' was created but field '{raw.FieldKey}' failed: {ex.Message}",
                    typeId = created.Id,
                    shortCode = created.ShortCode,
                    createdFieldCount,
                    validation = new
                    {
                        ok = false,
                        errors = new[] { new { code = "field_create", fieldKey = raw.FieldKey, message = ex.Message } }
                    }
                }
            });
        }
    }

    return JsonSerializer.SerializeToElement(new
    {
        kind = "record_type_change_committed",
        source = "ManageRecordTypesSkill",
        data = new
        {
            operation = "create_type",
            id = created.Id,
            shortCode = created.ShortCode,
            createdFieldCount
        }
    });
}

private sealed record class FieldInput(
    string FieldKey,
    string DisplayName,
    string DataType,
    JsonElement Config,
    bool IsRequired,
    int? SortOrder);

private static IReadOnlyList<FieldInput> ReadFieldArray(JsonElement args, string property, out string? error)
{
    error = null;
    if (!args.TryGetProperty(property, out var prop)) return Array.Empty<FieldInput>();
    if (prop.ValueKind == JsonValueKind.Null) return Array.Empty<FieldInput>();
    if (prop.ValueKind != JsonValueKind.Array)
    {
        error = $"{property} must be an array.";
        return Array.Empty<FieldInput>();
    }

    var list = new List<FieldInput>();
    foreach (var item in prop.EnumerateArray())
    {
        if (item.ValueKind != JsonValueKind.Object) { error = "fields[] entries must be objects."; return Array.Empty<FieldInput>(); }
        var fieldKey = ReadRequiredString(item, "fieldKey");
        if (fieldKey is null) { error = "fields[].fieldKey is required."; return Array.Empty<FieldInput>(); }
        var displayName = ReadRequiredString(item, "displayName");
        if (displayName is null) { error = "fields[].displayName is required."; return Array.Empty<FieldInput>(); }
        var dataType = ReadRequiredString(item, "dataType");
        if (dataType is null) { error = "fields[].dataType is required."; return Array.Empty<FieldInput>(); }

        var config = item.TryGetProperty("config", out var cfg) && cfg.ValueKind == JsonValueKind.Object
            ? cfg.Clone()
            : ParseSchema("{}");
        var isRequired = item.TryGetProperty("isRequired", out var req) && req.ValueKind == JsonValueKind.True;
        int? sortOrder = item.TryGetProperty("sortOrder", out var so) && so.ValueKind == JsonValueKind.Number
            ? so.GetInt32()
            : null;

        list.Add(new FieldInput(fieldKey, displayName, dataType, config, isRequired, sortOrder));
    }
    return list;
}

private static string BuildCreateTypeSummary(string shortCode, string name, IReadOnlyList<FieldInput> fields)
{
    var sb = new StringBuilder();
    sb.Append("Create record type ").Append(shortCode).Append(": '").Append(name).Append("'");
    if (fields.Count > 0)
    {
        sb.Append(" with ").Append(fields.Count).Append(" field").Append(fields.Count == 1 ? "" : "s");
        sb.Append(" (").Append(string.Join(", ", fields.Select(f => $"{f.FieldKey}[{f.DataType}{(f.IsRequired ? "*" : "")}]"))).Append(')');
    }
    return sb.ToString();
}
```

- [ ] **Step 4: Run all tests to verify they pass**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ManageRecordTypesSkill"`

Expected: 11 passed (5 prior + 6 new), 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs \
        tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs
git commit -m "Support inline fields[] on create_record_type"
```

---

## Task 5: `update_record_type` (metadata only)

**Files:**
- Modify: `src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs`
- Modify: `tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs`

Edits `name`, `description`, `icon`, `color`. Lookup by `typeShortCode`. Refuse mutations on `IsSystem` types. Authorize `Actions.Edit` against the resolved type instance.

The store's `UpdateRecordTypeInput` requires a non-empty `name`. To preserve "missing key keeps current value" semantics, the skill loads the current type, layers the patch, and passes the merged result.

- [ ] **Step 1: Write the failing tests**

Add:

```csharp
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
```

- [ ] **Step 2: Run new tests to verify they fail**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ManageRecordTypesSkill"`

Expected: 6 new tests fail with `data.message=not implemented`.

- [ ] **Step 3: Replace the placeholder `update_record_type` tool**

Replace the `UpdateTypeToolName` AgentTool entry:

```csharp
new AgentTool(
    Name: UpdateTypeToolName,
    Description: "Update a record type's metadata (name, description, icon, color). Identified by typeShortCode. ALWAYS call with confirmed=false first. Use null on a nullable property to clear it; omit a property to keep its current value.",
    JsonSchema: ParseSchema("""
        {
          "type": "object",
          "properties": {
            "typeShortCode": { "type": "string" },
            "name":          { "type": "string" },
            "description":   { "type": ["string", "null"] },
            "icon":          { "type": ["string", "null"] },
            "color":         { "type": ["string", "null"] },
            "confirmed":     { "type": "boolean" }
          },
          "required": ["typeShortCode"],
          "additionalProperties": false
        }
        """),
    Invoke: InvokeUpdateTypeAsync),
```

Add the implementation method and a snapshot helper:

```csharp
private static async Task<JsonElement> InvokeUpdateTypeAsync(
    JsonElement args,
    AgentToolContext context,
    CancellationToken ct)
{
    var shortCode = ReadRequiredString(args, "typeShortCode");
    if (shortCode is null) return Error(UpdateTypeToolName, "typeShortCode is required.");

    var typeStore = context.Services.GetRequiredService<IRecordTypeStore>();
    var existing = await typeStore.GetByShortCodeAsync(shortCode, ct);
    if (existing is null) return Error(UpdateTypeToolName, $"No record type with short code '{shortCode}'.");
    if (existing.IsSystem) return Error(UpdateTypeToolName, $"Record type '{shortCode}' is a system type and cannot be modified by the agent.");

    var authorizer = context.Services.GetRequiredService<IAuthorizer>();
    var decision = await authorizer.AuthorizeAsync(
        context.Session.User, Actions.Edit, new EntityRef(EntityKinds.RecordType, existing.Id.ToString()), ct);
    if (!decision.IsAllowed)
        return Error(UpdateTypeToolName, $"Not authorized to edit record type '{shortCode}' ({decision.Reason}).");

    // Layer the patch on top of the current type.
    var newName = args.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString())
        ? n.GetString()!
        : existing.Name;

    string? newDescription = existing.Description;
    if (args.TryGetProperty("description", out var d))
        newDescription = d.ValueKind == JsonValueKind.Null ? null : d.ValueKind == JsonValueKind.String ? d.GetString() : existing.Description;

    string? newIcon = existing.Icon;
    if (args.TryGetProperty("icon", out var iconProp))
        newIcon = iconProp.ValueKind == JsonValueKind.Null ? null : iconProp.ValueKind == JsonValueKind.String ? iconProp.GetString() : existing.Icon;

    string? newColor = existing.Color;
    if (args.TryGetProperty("color", out var colorProp))
        newColor = colorProp.ValueKind == JsonValueKind.Null ? null : colorProp.ValueKind == JsonValueKind.String ? colorProp.GetString() : existing.Color;

    var confirmed = args.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True;

    if (!confirmed)
    {
        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_type_change_proposal",
            source = "ManageRecordTypesSkill",
            data = new
            {
                operation = "update_type",
                summary = $"Update record type {shortCode}.",
                before = SnapshotType(existing),
                after = new { existing.ShortCode, name = newName, description = newDescription, icon = newIcon, color = newColor, isArchived = existing.IsArchived },
                validation = new { ok = true, errors = Array.Empty<object>() }
            }
        });
    }

    try
    {
        var updated = await typeStore.UpdateAsync(
            existing.Id,
            new UpdateRecordTypeInput(newName, newDescription, newIcon, newColor),
            context.Session.UserId,
            ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_type_change_committed",
            source = "ManageRecordTypesSkill",
            data = new
            {
                operation = "update_type",
                id = updated.Id,
                shortCode = updated.ShortCode
            }
        });
    }
    catch (RecordTypeValidationException ex)
    {
        return Failed("update_type", ex);
    }
}

private static object SnapshotType(RecordType type) => new
{
    type.ShortCode,
    type.Name,
    type.Description,
    type.Icon,
    type.Color,
    type.IsArchived
};
```

- [ ] **Step 4: Run all tests to verify they pass**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ManageRecordTypesSkill"`

Expected: 17 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs \
        tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs
git commit -m "Implement update_record_type metadata patching"
```

---

## Task 6: `set_record_type_archived`

**Files:**
- Modify: `src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs`
- Modify: `tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs`

Archive: `Actions.Delete`. Restore: `Actions.Edit`. Both at instance scope. System types refused.

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run new tests to verify they fail**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ManageRecordTypesSkill"`

Expected: 4 new tests fail.

- [ ] **Step 3: Replace the placeholder `set_record_type_archived` tool**

```csharp
new AgentTool(
    Name: SetTypeArchivedToolName,
    Description: "Archive or restore a record type. Archived types stay in the database but disappear from forms. Set archived=true to archive, archived=false to restore. ALWAYS call with confirmed=false first.",
    JsonSchema: ParseSchema("""
        {
          "type": "object",
          "properties": {
            "typeShortCode": { "type": "string" },
            "archived":      { "type": "boolean" },
            "confirmed":     { "type": "boolean" }
          },
          "required": ["typeShortCode","archived"],
          "additionalProperties": false
        }
        """),
    Invoke: InvokeSetTypeArchivedAsync),
```

```csharp
private static async Task<JsonElement> InvokeSetTypeArchivedAsync(
    JsonElement args,
    AgentToolContext context,
    CancellationToken ct)
{
    var shortCode = ReadRequiredString(args, "typeShortCode");
    if (shortCode is null) return Error(SetTypeArchivedToolName, "typeShortCode is required.");

    if (!args.TryGetProperty("archived", out var arch) || (arch.ValueKind != JsonValueKind.True && arch.ValueKind != JsonValueKind.False))
        return Error(SetTypeArchivedToolName, "archived must be a boolean.");
    var archived = arch.ValueKind == JsonValueKind.True;

    var typeStore = context.Services.GetRequiredService<IRecordTypeStore>();
    var existing = await typeStore.GetByShortCodeAsync(shortCode, ct);
    if (existing is null) return Error(SetTypeArchivedToolName, $"No record type with short code '{shortCode}'.");
    if (existing.IsSystem) return Error(SetTypeArchivedToolName, $"Record type '{shortCode}' is a system type and cannot be modified by the agent.");

    var authorizer = context.Services.GetRequiredService<IAuthorizer>();
    var action = archived ? Actions.Delete : Actions.Edit;
    var decision = await authorizer.AuthorizeAsync(
        context.Session.User, action, new EntityRef(EntityKinds.RecordType, existing.Id.ToString()), ct);
    if (!decision.IsAllowed)
        return Error(SetTypeArchivedToolName, $"Not authorized to {(archived ? "archive" : "restore")} record type '{shortCode}' ({decision.Reason}).");

    var op = archived ? "archive_type" : "restore_type";
    var confirmed = args.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True;

    if (!confirmed)
    {
        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_type_change_proposal",
            source = "ManageRecordTypesSkill",
            data = new
            {
                operation = op,
                summary = $"{(archived ? "Archive" : "Restore")} record type {shortCode}.",
                before = SnapshotType(existing),
                after = SnapshotType(existing with { IsArchived = archived }),
                validation = new { ok = true, errors = Array.Empty<object>() }
            }
        });
    }

    var updated = await typeStore.SetArchivedAsync(existing.Id, archived, context.Session.UserId, ct);
    return JsonSerializer.SerializeToElement(new
    {
        kind = "record_type_change_committed",
        source = "ManageRecordTypesSkill",
        data = new
        {
            operation = op,
            id = updated.Id,
            shortCode = updated.ShortCode,
            isArchived = updated.IsArchived
        }
    });
}
```

- [ ] **Step 4: Run all tests to verify they pass**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ManageRecordTypesSkill"`

Expected: 21 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs \
        tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs
git commit -m "Implement set_record_type_archived with archive/restore action split"
```

---

## Task 7: `add_record_type_field`

**Files:**
- Modify: `src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs`
- Modify: `tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs`

Adds one field to an existing type. `Actions.DefineFields`. System types refused. If `sortOrder` is omitted, default to `max(existingActiveField.sortOrder) + 10` (or `0` if none exist).

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run new tests to verify they fail**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ManageRecordTypesSkill"`

Expected: 6 new tests fail.

- [ ] **Step 3: Replace the placeholder `add_record_type_field` tool**

```csharp
new AgentTool(
    Name: AddFieldToolName,
    Description: "Add a new field to an existing record type. ALWAYS call with confirmed=false first to preview. If sortOrder is omitted it defaults to max(existing.sortOrder)+10.",
    JsonSchema: ParseSchema("""
        {
          "type": "object",
          "properties": {
            "typeShortCode": { "type": "string" },
            "fieldKey":      { "type": "string", "description": "snake_case, 1-64 chars, starts with a letter." },
            "displayName":   { "type": "string" },
            "dataType":      { "type": "string", "enum": ["text","number","date","phone","email","option","boolean"] },
            "config":        { "type": "object" },
            "isRequired":    { "type": "boolean" },
            "sortOrder":     { "type": "integer" },
            "confirmed":     { "type": "boolean" }
          },
          "required": ["typeShortCode","fieldKey","displayName","dataType"],
          "additionalProperties": false
        }
        """),
    Invoke: InvokeAddFieldAsync),
```

```csharp
private static async Task<JsonElement> InvokeAddFieldAsync(
    JsonElement args,
    AgentToolContext context,
    CancellationToken ct)
{
    var shortCode = ReadRequiredString(args, "typeShortCode");
    if (shortCode is null) return Error(AddFieldToolName, "typeShortCode is required.");
    var fieldKey = ReadRequiredString(args, "fieldKey");
    if (fieldKey is null) return Error(AddFieldToolName, "fieldKey is required.");
    var displayName = ReadRequiredString(args, "displayName");
    if (displayName is null) return Error(AddFieldToolName, "displayName is required.");
    var dataType = ReadRequiredString(args, "dataType");
    if (dataType is null) return Error(AddFieldToolName, "dataType is required.");

    var typeStore = context.Services.GetRequiredService<IRecordTypeStore>();
    var existing = await typeStore.GetByShortCodeAsync(shortCode, ct);
    if (existing is null) return Error(AddFieldToolName, $"No record type with short code '{shortCode}'.");
    if (existing.IsSystem) return Error(AddFieldToolName, $"Record type '{shortCode}' is a system type and cannot be modified by the agent.");

    var authorizer = context.Services.GetRequiredService<IAuthorizer>();
    var decision = await authorizer.AuthorizeAsync(
        context.Session.User, Actions.DefineFields, new EntityRef(EntityKinds.RecordType, existing.Id.ToString()), ct);
    if (!decision.IsAllowed)
        return Error(AddFieldToolName, $"Not authorized to define fields on '{shortCode}' ({decision.Reason}).");

    var registry = context.Services.GetRequiredService<IFieldTypeRegistry>();
    if (!registry.TryGet(dataType, out var fieldType))
        return Error(AddFieldToolName, $"Unknown data_type '{dataType}'.");

    var rawConfig = args.TryGetProperty("config", out var cfg) && cfg.ValueKind == JsonValueKind.Object
        ? cfg.Clone()
        : ParseSchema("{}");
    var isRequired = args.TryGetProperty("isRequired", out var req) && req.ValueKind == JsonValueKind.True;

    int sortOrder;
    if (args.TryGetProperty("sortOrder", out var so) && so.ValueKind == JsonValueKind.Number)
    {
        sortOrder = so.GetInt32();
    }
    else
    {
        var existingFields = await typeStore.ListFieldsAsync(existing.Id, includeArchived: false, ct);
        sortOrder = existingFields.Count == 0 ? 0 : existingFields.Max(f => f.SortOrder) + 10;
    }

    JsonElement normalizedConfig;
    var validationErrors = new List<object>();
    try
    {
        normalizedConfig = fieldType.NormalizeConfig(rawConfig);
    }
    catch (FieldConfigException ex)
    {
        normalizedConfig = rawConfig;
        validationErrors.Add(new { code = "field_config", fieldKey, message = ex.Message });
    }

    var confirmed = args.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True;

    if (!confirmed)
    {
        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_type_change_proposal",
            source = "ManageRecordTypesSkill",
            data = new
            {
                operation = "add_field",
                summary = $"Add field {fieldKey}[{dataType}{(isRequired ? "*" : "")}] to {shortCode}.",
                after = new { fieldKey, displayName, dataType, isRequired, sortOrder },
                validation = new { ok = validationErrors.Count == 0, errors = validationErrors.ToArray() }
            }
        });
    }

    if (validationErrors.Count > 0)
    {
        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_type_change_failed",
            source = "ManageRecordTypesSkill",
            data = new
            {
                operation = "add_field",
                message = "Field config failed validation.",
                validation = new { ok = false, errors = validationErrors.ToArray() }
            }
        });
    }

    try
    {
        var created = await typeStore.CreateFieldAsync(
            existing.Id,
            new CreateRecordTypeFieldInput(fieldKey, displayName, dataType, normalizedConfig, isRequired, sortOrder),
            context.Session.UserId,
            ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_type_change_committed",
            source = "ManageRecordTypesSkill",
            data = new
            {
                operation = "add_field",
                typeId = existing.Id,
                shortCode = existing.ShortCode,
                fieldId = created.Id,
                fieldKey = created.FieldKey
            }
        });
    }
    catch (RecordTypeValidationException ex)
    {
        return Failed("add_field", ex);
    }
}
```

- [ ] **Step 4: Run all tests to verify they pass**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ManageRecordTypesSkill"`

Expected: 27 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs \
        tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs
git commit -m "Implement add_record_type_field with sortOrder default and config validation"
```

---

## Task 8: `update_record_type_field`

**Files:**
- Modify: `src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs`
- Modify: `tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs`

Patches `displayName` / `config` / `isRequired` / `sortOrder` on a single field. Loads the field, layers the patch, calls `UpdateFieldAsync` (which takes a full `UpdateRecordTypeFieldInput`, no Optional). Builds a `fieldChanges[]` diff in the proposal envelope. System types refused.

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run new tests to verify they fail**

Expected: 4 new tests fail.

- [ ] **Step 3: Replace the placeholder `update_record_type_field` tool**

```csharp
new AgentTool(
    Name: UpdateFieldToolName,
    Description: "Update an existing field on a record type. fieldKey is the lookup, not editable. dataType cannot be changed — archive the old field and add a new one instead. ALWAYS call with confirmed=false first.",
    JsonSchema: ParseSchema("""
        {
          "type": "object",
          "properties": {
            "typeShortCode": { "type": "string" },
            "fieldKey":      { "type": "string" },
            "displayName":   { "type": "string" },
            "config":        { "type": "object" },
            "isRequired":    { "type": "boolean" },
            "sortOrder":     { "type": "integer" },
            "confirmed":     { "type": "boolean" }
          },
          "required": ["typeShortCode","fieldKey"],
          "additionalProperties": false
        }
        """),
    Invoke: InvokeUpdateFieldAsync),
```

```csharp
private static async Task<JsonElement> InvokeUpdateFieldAsync(
    JsonElement args,
    AgentToolContext context,
    CancellationToken ct)
{
    var shortCode = ReadRequiredString(args, "typeShortCode");
    if (shortCode is null) return Error(UpdateFieldToolName, "typeShortCode is required.");
    var fieldKey = ReadRequiredString(args, "fieldKey");
    if (fieldKey is null) return Error(UpdateFieldToolName, "fieldKey is required.");

    var typeStore = context.Services.GetRequiredService<IRecordTypeStore>();
    var existing = await typeStore.GetByShortCodeAsync(shortCode, ct);
    if (existing is null) return Error(UpdateFieldToolName, $"No record type with short code '{shortCode}'.");
    if (existing.IsSystem) return Error(UpdateFieldToolName, $"Record type '{shortCode}' is a system type and cannot be modified by the agent.");

    var fields = await typeStore.ListFieldsAsync(existing.Id, includeArchived: true, ct);
    var field = fields.FirstOrDefault(f => f.FieldKey == fieldKey);
    if (field is null) return Error(UpdateFieldToolName, $"No field '{fieldKey}' on record type '{shortCode}'.");

    var authorizer = context.Services.GetRequiredService<IAuthorizer>();
    var decision = await authorizer.AuthorizeAsync(
        context.Session.User, Actions.DefineFields, new EntityRef(EntityKinds.RecordType, existing.Id.ToString()), ct);
    if (!decision.IsAllowed)
        return Error(UpdateFieldToolName, $"Not authorized to define fields on '{shortCode}' ({decision.Reason}).");

    var newDisplayName = args.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(dn.GetString())
        ? dn.GetString()!
        : field.DisplayName;
    var newIsRequired = args.TryGetProperty("isRequired", out var ir) && (ir.ValueKind == JsonValueKind.True || ir.ValueKind == JsonValueKind.False)
        ? ir.ValueKind == JsonValueKind.True
        : field.IsRequired;
    var newSortOrder = args.TryGetProperty("sortOrder", out var so) && so.ValueKind == JsonValueKind.Number
        ? so.GetInt32()
        : field.SortOrder;

    var registry = context.Services.GetRequiredService<IFieldTypeRegistry>();
    if (!registry.TryGet(field.DataType, out var fieldType))
        return Error(UpdateFieldToolName, $"Unknown data_type '{field.DataType}' on existing field.");

    JsonElement newConfig = field.Config;
    if (args.TryGetProperty("config", out var cfg) && cfg.ValueKind == JsonValueKind.Object)
    {
        try { newConfig = fieldType.NormalizeConfig(cfg.Clone()); }
        catch (FieldConfigException ex)
        {
            return JsonSerializer.SerializeToElement(new
            {
                kind = "record_type_change_failed",
                source = "ManageRecordTypesSkill",
                data = new
                {
                    operation = "update_field",
                    message = ex.Message,
                    validation = new { ok = false, errors = new[] { new { code = "field_config", fieldKey, message = ex.Message } } }
                }
            });
        }
    }

    var changes = new List<object>();
    if (!string.Equals(field.DisplayName, newDisplayName, StringComparison.Ordinal))
        changes.Add(new { attribute = "displayName", before = field.DisplayName, after = newDisplayName });
    if (field.IsRequired != newIsRequired)
        changes.Add(new { attribute = "isRequired", before = field.IsRequired, after = newIsRequired });
    if (field.SortOrder != newSortOrder)
        changes.Add(new { attribute = "sortOrder", before = field.SortOrder, after = newSortOrder });
    if (!string.Equals(field.Config.GetRawText(), newConfig.GetRawText(), StringComparison.Ordinal))
        changes.Add(new { attribute = "config", before = field.Config, after = newConfig });

    var confirmed = args.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True;

    if (!confirmed)
    {
        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_type_change_proposal",
            source = "ManageRecordTypesSkill",
            data = new
            {
                operation = "update_field",
                summary = $"{shortCode}.{fieldKey}: {changes.Count} change{(changes.Count == 1 ? "" : "s")}.",
                fieldChanges = changes.ToArray(),
                validation = new { ok = true, errors = Array.Empty<object>() }
            }
        });
    }

    try
    {
        var updated = await typeStore.UpdateFieldAsync(
            existing.Id,
            field.Id,
            new UpdateRecordTypeFieldInput(newDisplayName, newConfig, newIsRequired, newSortOrder),
            context.Session.UserId,
            ct);

        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_type_change_committed",
            source = "ManageRecordTypesSkill",
            data = new
            {
                operation = "update_field",
                typeId = existing.Id,
                shortCode = existing.ShortCode,
                fieldId = updated.Id,
                fieldKey = updated.FieldKey
            }
        });
    }
    catch (RecordTypeValidationException ex)
    {
        return Failed("update_field", ex);
    }
}
```

- [ ] **Step 4: Run all tests to verify they pass**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ManageRecordTypesSkill"`

Expected: 31 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs \
        tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs
git commit -m "Implement update_record_type_field with patch-and-diff semantics"
```

---

## Task 9: `set_record_type_field_archived`

**Files:**
- Modify: `src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs`
- Modify: `tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs`

Same shape as `set_record_type_archived` but for one field. `Actions.DefineFields` (regardless of archive direction — matches `RecordTypeEndpoints` for the `/{id}/fields/{fieldId}` DELETE/restore routes which both use `DefineFields`).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task SetFieldArchived_archive_calls_SetFieldArchivedAsync()
{
    var typeStore = new FakeTypeStore { Types = { CarType }, FieldsByType = { [CarTypeId] = new() { ModelField, YearField } } };
    var authorizer = new FakeAuthorizer();
    var skill = new ManageRecordTypesSkill();

    var result = await Invoke(skill, "set_record_type_field_archived", new
    {
        typeShortCode = "CAR",
        fieldKey = "year",
        archived = true,
        confirmed = true
    }, typeStore, authorizer);

    Assert.Equal("record_type_change_committed", result.GetProperty("kind").GetString());
    var call = Assert.Single(typeStore.ArchiveFieldCalls);
    Assert.Equal(YearField.Id, call.FieldId);
    Assert.True(call.Archived);
    Assert.Contains(authorizer.Calls, c => c.Action == Actions.DefineFields && c.Target.Id == CarTypeId.ToString());
}

[Fact]
public async Task SetFieldArchived_dry_run_does_not_call_store()
{
    var typeStore = new FakeTypeStore { Types = { CarType }, FieldsByType = { [CarTypeId] = new() { ModelField } } };
    var authorizer = new FakeAuthorizer();
    var skill = new ManageRecordTypesSkill();

    var result = await Invoke(skill, "set_record_type_field_archived", new
    {
        typeShortCode = "CAR",
        fieldKey = "model",
        archived = true,
        confirmed = false
    }, typeStore, authorizer);

    Assert.Equal("record_type_change_proposal", result.GetProperty("kind").GetString());
    Assert.Empty(typeStore.ArchiveFieldCalls);
}

[Fact]
public async Task SetFieldArchived_unknown_field_returns_error()
{
    var typeStore = new FakeTypeStore { Types = { CarType }, FieldsByType = { [CarTypeId] = new() { ModelField } } };
    var authorizer = new FakeAuthorizer();
    var skill = new ManageRecordTypesSkill();

    var result = await Invoke(skill, "set_record_type_field_archived", new
    {
        typeShortCode = "CAR",
        fieldKey = "nope",
        archived = true,
        confirmed = true
    }, typeStore, authorizer);

    Assert.Equal("error", result.GetProperty("kind").GetString());
    Assert.Empty(typeStore.ArchiveFieldCalls);
}
```

- [ ] **Step 2: Run new tests to verify they fail**

Expected: 3 new tests fail.

- [ ] **Step 3: Replace the placeholder `set_record_type_field_archived` tool**

```csharp
new AgentTool(
    Name: SetFieldArchivedToolName,
    Description: "Archive or restore a field on a record type. Archiving a field hides it from forms but does NOT remove existing records' values for that field. Always narrate this consequence to the user when archiving.",
    JsonSchema: ParseSchema("""
        {
          "type": "object",
          "properties": {
            "typeShortCode": { "type": "string" },
            "fieldKey":      { "type": "string" },
            "archived":      { "type": "boolean" },
            "confirmed":     { "type": "boolean" }
          },
          "required": ["typeShortCode","fieldKey","archived"],
          "additionalProperties": false
        }
        """),
    Invoke: InvokeSetFieldArchivedAsync),
```

```csharp
private static async Task<JsonElement> InvokeSetFieldArchivedAsync(
    JsonElement args,
    AgentToolContext context,
    CancellationToken ct)
{
    var shortCode = ReadRequiredString(args, "typeShortCode");
    if (shortCode is null) return Error(SetFieldArchivedToolName, "typeShortCode is required.");
    var fieldKey = ReadRequiredString(args, "fieldKey");
    if (fieldKey is null) return Error(SetFieldArchivedToolName, "fieldKey is required.");
    if (!args.TryGetProperty("archived", out var arch) || (arch.ValueKind != JsonValueKind.True && arch.ValueKind != JsonValueKind.False))
        return Error(SetFieldArchivedToolName, "archived must be a boolean.");
    var archived = arch.ValueKind == JsonValueKind.True;

    var typeStore = context.Services.GetRequiredService<IRecordTypeStore>();
    var existing = await typeStore.GetByShortCodeAsync(shortCode, ct);
    if (existing is null) return Error(SetFieldArchivedToolName, $"No record type with short code '{shortCode}'.");
    if (existing.IsSystem) return Error(SetFieldArchivedToolName, $"Record type '{shortCode}' is a system type and cannot be modified by the agent.");

    var fields = await typeStore.ListFieldsAsync(existing.Id, includeArchived: true, ct);
    var field = fields.FirstOrDefault(f => f.FieldKey == fieldKey);
    if (field is null) return Error(SetFieldArchivedToolName, $"No field '{fieldKey}' on record type '{shortCode}'.");

    var authorizer = context.Services.GetRequiredService<IAuthorizer>();
    var decision = await authorizer.AuthorizeAsync(
        context.Session.User, Actions.DefineFields, new EntityRef(EntityKinds.RecordType, existing.Id.ToString()), ct);
    if (!decision.IsAllowed)
        return Error(SetFieldArchivedToolName, $"Not authorized to define fields on '{shortCode}' ({decision.Reason}).");

    var op = archived ? "archive_field" : "restore_field";
    var confirmed = args.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True;

    if (!confirmed)
    {
        return JsonSerializer.SerializeToElement(new
        {
            kind = "record_type_change_proposal",
            source = "ManageRecordTypesSkill",
            data = new
            {
                operation = op,
                summary = archived
                    ? $"Archive field {shortCode}.{fieldKey}. Existing records' values for this field stay in storage but disappear from forms."
                    : $"Restore field {shortCode}.{fieldKey}.",
                before = new { field.FieldKey, field.IsArchived },
                after = new { field.FieldKey, isArchived = archived },
                validation = new { ok = true, errors = Array.Empty<object>() }
            }
        });
    }

    var updated = await typeStore.SetFieldArchivedAsync(existing.Id, field.Id, archived, context.Session.UserId, ct);
    return JsonSerializer.SerializeToElement(new
    {
        kind = "record_type_change_committed",
        source = "ManageRecordTypesSkill",
        data = new
        {
            operation = op,
            typeId = existing.Id,
            shortCode = existing.ShortCode,
            fieldId = updated.Id,
            fieldKey = updated.FieldKey,
            isArchived = updated.IsArchived
        }
    });
}
```

- [ ] **Step 4: Run all tests to verify they pass**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj --filter "FullyQualifiedName~ManageRecordTypesSkill"`

Expected: 34 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs \
        tests/AutoNate.Web.Tests/ManageRecordTypesSkillTests.cs
git commit -m "Implement set_record_type_field_archived"
```

---

## Task 10: System-prompt fragment + final build

**Files:**
- Modify: `src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs`

Replace the `SystemPromptFragment` stub with the real text from the spec, so the agent knows when and how to use the new tools.

- [ ] **Step 1: Replace the `SystemPromptFragment` body**

Find:

```csharp
public string? SystemPromptFragment(AgentSessionContext context) => null;
```

Replace with:

```csharp
public string? SystemPromptFragment(AgentSessionContext context) =>
    "You can author and edit record types via create_record_type / update_record_type / " +
    "add_record_type_field / update_record_type_field / set_record_type_archived / " +
    "set_record_type_field_archived. ALWAYS call them with confirmed=false first; " +
    "the tool returns a structured proposal envelope. Present the summary and any " +
    "validation errors to the user, then ASK for explicit confirmation. Only after " +
    "plain-language approval ('yes', 'go ahead') re-call with confirmed=true and the " +
    "SAME arguments. If you change ANY value between preview and commit, run " +
    "confirmed=false again first. Before proposing changes to an existing type, call " +
    "list_record_types and describe_record_type so you can show the user a clean diff. " +
    "Be aware: archiving a field hides it from forms but does NOT remove existing " +
    "records' values for that field — narrate this when archiving. Field data_type " +
    "cannot be changed once a field is created; archive the old field and add a new " +
    "one instead.";
```

- [ ] **Step 2: Build the full solution to confirm nothing else broke**

Run: `dotnet build AutoNate.sln`

Expected: build succeeds with no errors.

- [ ] **Step 3: Run the full test suite (not just the new file)**

Run: `dotnet test tests/AutoNate.Web.Tests/AutoNate.Web.Tests.csproj`

Expected: all tests pass. The new skill should not affect any unrelated test.

- [ ] **Step 4: Commit**

```bash
git add src/AutoNate.Web/Services/Agent/Skills/ManageRecordTypesSkill.cs
git commit -m "Add system-prompt fragment for ManageRecordTypesSkill"
```

---

## Final Verification

- [ ] **Step 1: Spot-check the skill registry sees the new skill**

Add and run a temporary one-off test (or use an existing integration test that lists tools). A quick way: build, run the app locally, hit any endpoint that triggers an agent turn, and inspect the tool list logs. If a unit test for `SkillRegistry` exists, confirm the count went up by one. (No new commit needed; just verification.)

Run: `grep -c "ManageRecordTypesSkill\|create_record_type\|update_record_type" src/AutoNate.Web/`

Expected: at least the DI registration line and the skill class reference each, plus the six tool names.

- [ ] **Step 2: Sanity-check the help/docs**

If any project README or docs page enumerates agent skills (look in `docs/` or `README.md` for "ManageRecordsSkill" mentions), add the new skill to that list. If no such doc exists, skip this step. No spec doc updates are required.

- [ ] **Step 3: Done**

The chatbot can now author record types end-to-end with confirmation. Suggested manual smoke test (out-of-plan): start the app, open the chatbot sidebar, ask "Create a Cars record type with model (text, required), year (number), and color (option: red, blue, black)". Verify the agent narrates the proposal, asks for confirmation, then commits on "yes" and the type appears at `/api/record-types/`.
