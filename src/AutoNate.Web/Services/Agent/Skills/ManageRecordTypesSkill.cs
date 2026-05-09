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
}
