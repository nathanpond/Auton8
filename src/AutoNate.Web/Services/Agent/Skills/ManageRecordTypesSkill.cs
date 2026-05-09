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
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) => null;

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
        var normalizedFields = new List<(FieldInput Raw, JsonElement NormalizedConfig, int ResolvedSortOrder)>();
        for (var i = 0; i < fieldInputs.Count; i++)
        {
            var field = fieldInputs[i];
            var resolvedSortOrder = field.SortOrder ?? (i * 10);

            if (!registry.TryGet(field.DataType, out var fieldType))
            {
                validationErrors.Add(new { code = "unknown_data_type", fieldKey = field.FieldKey, message = $"Unknown data_type '{field.DataType}'." });
                continue;
            }
            try
            {
                var normalized = fieldType.NormalizeConfig(field.Config);
                normalizedFields.Add((field, normalized, resolvedSortOrder));
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
        foreach (var (raw, normalizedConfig, resolvedSortOrder) in normalizedFields)
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
                        resolvedSortOrder),
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
                    after = new { shortCode = existing.ShortCode, name = newName, description = newDescription, icon = newIcon, color = newColor, isArchived = existing.IsArchived },
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

    private static object SnapshotType(RecordType type) => new
    {
        shortCode = type.ShortCode,
        name = type.Name,
        description = type.Description,
        icon = type.Icon,
        color = type.Color,
        isArchived = type.IsArchived
    };

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
