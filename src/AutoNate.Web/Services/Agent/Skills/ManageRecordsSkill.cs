using System.Globalization;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Records;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.Agent.Skills;

// First mutating skill. Two tools: create_record and update_record. Both
// gate on a `confirmed: bool` argument; the gate is server-enforced — the
// skill simply does not call IRecordStore.Create/UpdateAsync unless the
// caller passes `confirmed: true`. The dry-run (confirmed: false) path
// builds a structured proposal envelope the agent uses to summarise the
// change for the user.
//
// A misbehaving model that "forgets" to ask first will, at worst, commit
// a change without prior narration. The audit log captures every tool
// call (including args, so dry-run vs commit is visible) and IRecordStore
// already gates by IAuthorizer against the calling user's principal —
// the skill is a translator, not a privileged backdoor.
public sealed class ManageRecordsSkill : IAgentSkill
{
    public const string CreateToolName = "create_record";
    public const string UpdateToolName = "update_record";

    public string Name => "manage-records";

    public string Description => "Create new records and update existing ones, with mandatory user confirmation before each commit.";

    public IReadOnlyList<AgentTool> Tools { get; }

    public ManageRecordsSkill()
    {
        Tools = new[]
        {
            new AgentTool(
                Name: CreateToolName,
                Description: "Create a new record. ALWAYS call with confirmed=false first to preview the change. Only call with confirmed=true after the user has explicitly approved the proposal.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "typeShortCode": { "type": "string", "description": "Short code of the record type." },
                        "name": { "type": "string", "description": "The record's display name." },
                        "values": { "type": "object", "description": "Custom field values keyed by field key (see describe_record_type)." },
                        "status": { "type": ["string", "null"], "description": "Optional initial status." },
                        "dueDate": { "type": ["string", "null"], "description": "ISO date (yyyy-MM-dd). Optional." },
                        "assigneeIds": { "type": "array", "items": { "type": "string" }, "description": "Optional list of user GUIDs to assign." },
                        "confirmed": { "type": "boolean", "description": "Set to false (default) for a dry-run. Set to true ONLY after the user approves the proposal." }
                      },
                      "required": ["typeShortCode", "name"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeCreateAsync),

            new AgentTool(
                Name: UpdateToolName,
                Description: "Update an existing record by key. ALWAYS call with confirmed=false first to preview the diff. Only call with confirmed=true after the user has explicitly approved.",
                JsonSchema: ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "key": { "type": "string", "description": "Stable record key, e.g. INC-101." },
                        "name": { "type": "string", "description": "Optional new display name." },
                        "values": { "type": "object", "description": "Only the custom fields being changed; others stay as-is. Use null to clear a field." },
                        "status": { "type": ["string", "null"], "description": "Omit to keep current; null to clear; string to set." },
                        "dueDate": { "type": ["string", "null"], "description": "Omit to keep; null to clear; ISO date to set." },
                        "assigneeIds": { "type": "array", "items": { "type": "string" }, "description": "Replaces the full assignee list when provided." },
                        "confirmed": { "type": "boolean" }
                      },
                      "required": ["key"],
                      "additionalProperties": false
                    }
                    """),
                Invoke: InvokeUpdateAsync)
        };
    }

    public string? SystemPromptFragment(AgentSessionContext context) =>
        "You can create and update records via create_record / update_record. ALWAYS call them with confirmed=false first; the tool returns a structured proposal envelope. Present the proposal's summary and any validation issues to the user, then ASK for explicit confirmation. Only after the user confirms in plain language ('yes', 'go ahead', 'do it') should you re-call the tool with confirmed=true and the same arguments. If you change ANY value between the preview and the commit, run confirmed=false again first. If you don't know the record type's fields, call describe_record_type before proposing values.";

    private static async Task<JsonElement> InvokeCreateAsync(
        JsonElement args,
        AgentToolContext context,
        CancellationToken ct)
    {
        var typeShortCode = ReadRequiredString(args, "typeShortCode");
        if (typeShortCode is null) return Error(CreateToolName, "typeShortCode is required.");

        var name = ReadRequiredString(args, "name");
        if (name is null) return Error(CreateToolName, "name is required.");

        var typeStore = context.Services.GetRequiredService<IRecordTypeStore>();
        var type = await typeStore.GetByShortCodeAsync(typeShortCode, ct);
        if (type is null)
        {
            return Error(CreateToolName, $"No record type with short code '{typeShortCode}'.");
        }

        var fields = await typeStore.ListFieldsAsync(type.Id, includeArchived: false, ct);

        // Custom field values: pass through as JsonElement (or empty object if absent).
        var valuesElement = args.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Object
            ? v.Clone()
            : EmptyObject();

        var status = args.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString()
            : null;

        DateOnly? dueDate;
        if (!TryReadOptionalDate(args, "dueDate", out dueDate, out var dueDateError))
        {
            return Error(CreateToolName, dueDateError);
        }

        if (!TryReadAssigneeIds(args, out var assigneeIds, out var assigneeError))
        {
            return Error(CreateToolName, assigneeError);
        }

        var confirmed = args.TryGetProperty("confirmed", out var c) && c.ValueKind == JsonValueKind.True;

        // Best-effort dry-run validation: required fields must be present.
        var validationErrors = new List<object>();
        foreach (var field in fields.Where(f => f.IsRequired))
        {
            if (!valuesElement.TryGetProperty(field.FieldKey, out var present)
                || present.ValueKind == JsonValueKind.Null
                || (present.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(present.GetString())))
            {
                validationErrors.Add(new { fieldKey = field.FieldKey, message = $"'{field.DisplayName}' is required." });
            }
        }

        if (!confirmed)
        {
            return JsonSerializer.SerializeToElement(new
            {
                kind = "record_change_proposal",
                source = "ManageRecordsSkill",
                data = new
                {
                    operation = "create",
                    typeShortCode = type.ShortCode,
                    summary = BuildCreateSummary(type, name, valuesElement, status),
                    name,
                    fields = ProjectProposedFields(fields, valuesElement),
                    metadata = new
                    {
                        status,
                        dueDate = dueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        assigneeIds = assigneeIds?.Select(g => g.ToString()).ToArray()
                    },
                    validation = new
                    {
                        ok = validationErrors.Count == 0,
                        errors = validationErrors.ToArray()
                    }
                }
            });
        }

        // Commit path.
        var recordStore = context.Services.GetRequiredService<IRecordStore>();
        var input = new CreateRecordInput(
            RecordTypeId: type.Id,
            Name: name,
            Status: status,
            DueDate: dueDate,
            Values: valuesElement,
            AssigneeIds: assigneeIds);

        try
        {
            var record = await recordStore.CreateAsync(input, context.Session.UserId, ct);
            return JsonSerializer.SerializeToElement(new
            {
                kind = "record_change_committed",
                source = "ManageRecordsSkill",
                data = new
                {
                    operation = "create",
                    id = record.Id,
                    key = record.Key,
                    name = record.Name,
                    status = record.Status,
                    createdAtUtc = record.CreatedAtUtc
                }
            });
        }
        catch (RecordValidationException ex)
        {
            return Failed(CreateToolName, "create", ex);
        }
    }

    private static async Task<JsonElement> InvokeUpdateAsync(
        JsonElement args,
        AgentToolContext context,
        CancellationToken ct)
    {
        var key = ReadRequiredString(args, "key");
        if (key is null) return Error(UpdateToolName, "key is required.");

        var recordStore = context.Services.GetRequiredService<IRecordStore>();
        var record = await recordStore.GetByKeyAsync(key, ct);
        if (record is null)
        {
            return Error(UpdateToolName, $"No record with key '{key}' is visible.");
        }

        var typeStore = context.Services.GetRequiredService<IRecordTypeStore>();
        var type = await typeStore.GetAsync(record.RecordTypeId, ct);
        var fields = type is null
            ? Array.Empty<RecordTypeField>()
            : (IReadOnlyList<RecordTypeField>)await typeStore.ListFieldsAsync(record.RecordTypeId, includeArchived: false, ct);

        // Build Optional<T> for each metadata field per the rule:
        //   property absent     -> Optional.None      (don't touch)
        //   property is null    -> Optional.Some(null) (clear)
        //   property is value   -> Optional.Some(value) (set)
        var nameProvided = args.TryGetProperty("name", out var nameProp);
        string? newName = null;
        if (nameProvided)
        {
            if (nameProp.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(nameProp.GetString()))
            {
                return Error(UpdateToolName, "name must be a non-empty string when provided.");
            }
            newName = nameProp.GetString();
        }

        Optional<string?> statusOpt = Optional<string?>.None;
        if (args.TryGetProperty("status", out var sProp))
        {
            statusOpt = sProp.ValueKind switch
            {
                JsonValueKind.Null => Optional<string?>.Some(null),
                JsonValueKind.String => Optional<string?>.Some(sProp.GetString()),
                _ => Optional<string?>.None
            };
        }

        Optional<DateOnly?> dueOpt = Optional<DateOnly?>.None;
        if (args.TryGetProperty("dueDate", out var dProp))
        {
            if (dProp.ValueKind == JsonValueKind.Null)
            {
                dueOpt = Optional<DateOnly?>.Some(null);
            }
            else if (dProp.ValueKind == JsonValueKind.String)
            {
                if (!DateOnly.TryParse(dProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    return Error(UpdateToolName, $"dueDate '{dProp.GetString()}' is not a valid yyyy-MM-dd date.");
                }
                dueOpt = Optional<DateOnly?>.Some(parsed);
            }
        }

        JsonElement? valuesPatch = null;
        if (args.TryGetProperty("values", out var vProp) && vProp.ValueKind == JsonValueKind.Object)
        {
            valuesPatch = vProp.Clone();
        }

        if (!TryReadAssigneeIds(args, out var assigneeIds, out var assigneeError))
        {
            return Error(UpdateToolName, assigneeError);
        }

        var confirmed = args.TryGetProperty("confirmed", out var cProp) && cProp.ValueKind == JsonValueKind.True;

        if (!confirmed)
        {
            // Build a structured diff so the agent can narrate what changes.
            var fieldChanges = new List<object>();
            if (nameProvided && !string.Equals(record.Name, newName, StringComparison.Ordinal))
            {
                fieldChanges.Add(new { key = "(name)", displayName = "Name", before = record.Name, after = newName });
            }
            if (statusOpt.HasValue && !string.Equals(record.Status, statusOpt.Value, StringComparison.Ordinal))
            {
                fieldChanges.Add(new { key = "(status)", displayName = "Status", before = record.Status, after = statusOpt.Value });
            }
            if (dueOpt.HasValue && record.DueDate != dueOpt.Value)
            {
                fieldChanges.Add(new
                {
                    key = "(dueDate)",
                    displayName = "Due date",
                    before = record.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    after = dueOpt.Value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                });
            }
            if (assigneeIds is not null && !record.AssigneeIds.SequenceEqual(assigneeIds))
            {
                fieldChanges.Add(new
                {
                    key = "(assigneeIds)",
                    displayName = "Assignees",
                    before = record.AssigneeIds.Select(g => g.ToString()).ToArray(),
                    after = assigneeIds.Select(g => g.ToString()).ToArray()
                });
            }
            if (valuesPatch is JsonElement patch)
            {
                foreach (var prop in patch.EnumerateObject())
                {
                    var fieldDef = fields.FirstOrDefault(f => f.FieldKey == prop.Name);
                    JsonElement? before = null;
                    if (record.Values.ValueKind == JsonValueKind.Object
                        && record.Values.TryGetProperty(prop.Name, out var existing))
                    {
                        before = existing.Clone();
                    }
                    var afterValue = prop.Value.Clone();
                    if (before is JsonElement b && JsonElementEquals(b, afterValue))
                    {
                        continue; // unchanged value — skip
                    }
                    fieldChanges.Add(new
                    {
                        key = prop.Name,
                        displayName = fieldDef?.DisplayName ?? prop.Name,
                        before,
                        after = afterValue
                    });
                }
            }

            return JsonSerializer.SerializeToElement(new
            {
                kind = "record_change_proposal",
                source = "ManageRecordsSkill",
                data = new
                {
                    operation = "update",
                    key = record.Key,
                    summary = BuildUpdateSummary(record, fieldChanges),
                    fieldChanges = fieldChanges.ToArray(),
                    validation = new { ok = true, errors = Array.Empty<object>() }
                }
            });
        }

        // Commit path.
        var input = new UpdateRecordInput(
            Name: newName,
            Status: statusOpt,
            DueDate: dueOpt,
            Values: valuesPatch,
            AssigneeIds: assigneeIds);

        try
        {
            var updated = await recordStore.UpdateAsync(record.Id, input, context.Session.UserId, ct);
            return JsonSerializer.SerializeToElement(new
            {
                kind = "record_change_committed",
                source = "ManageRecordsSkill",
                data = new
                {
                    operation = "update",
                    id = updated.Id,
                    key = updated.Key,
                    name = updated.Name,
                    status = updated.Status,
                    updatedAtUtc = updated.UpdatedAtUtc
                }
            });
        }
        catch (RecordValidationException ex)
        {
            return Failed(UpdateToolName, "update", ex);
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

    private static bool TryReadOptionalDate(JsonElement args, string property, out DateOnly? value, out string error)
    {
        value = null;
        error = string.Empty;
        if (!args.TryGetProperty(property, out var prop)) return true;
        if (prop.ValueKind == JsonValueKind.Null) return true;
        if (prop.ValueKind != JsonValueKind.String)
        {
            error = $"{property} must be a yyyy-MM-dd string when provided.";
            return false;
        }
        if (!DateOnly.TryParse(prop.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            error = $"{property} '{prop.GetString()}' is not a valid yyyy-MM-dd date.";
            return false;
        }
        value = parsed;
        return true;
    }

    private static bool TryReadAssigneeIds(JsonElement args, out IReadOnlyList<Guid>? ids, out string error)
    {
        ids = null;
        error = string.Empty;
        if (!args.TryGetProperty("assigneeIds", out var prop)) return true;
        if (prop.ValueKind == JsonValueKind.Null) return true;
        if (prop.ValueKind != JsonValueKind.Array)
        {
            error = "assigneeIds must be an array of GUID strings.";
            return false;
        }
        var list = new List<Guid>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !Guid.TryParse(item.GetString(), out var g))
            {
                error = $"assigneeIds entry '{item.ToString()}' is not a valid GUID.";
                return false;
            }
            list.Add(g);
        }
        ids = list;
        return true;
    }

    private static JsonElement EmptyObject()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static bool JsonElementEquals(JsonElement a, JsonElement b) =>
        string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal);

    private static object[] ProjectProposedFields(IReadOnlyList<RecordTypeField> fields, JsonElement values)
    {
        var byKey = fields.ToDictionary(f => f.FieldKey, StringComparer.Ordinal);
        var result = new List<object>();
        if (values.ValueKind != JsonValueKind.Object) return result.ToArray();
        foreach (var prop in values.EnumerateObject())
        {
            byKey.TryGetValue(prop.Name, out var def);
            result.Add(new
            {
                fieldKey = prop.Name,
                displayName = def?.DisplayName ?? prop.Name,
                dataType = def?.DataType,
                value = prop.Value.Clone()
            });
        }
        return result.ToArray();
    }

    private static string BuildCreateSummary(RecordType type, string name, JsonElement values, string? status)
    {
        var sb = new StringBuilder();
        sb.Append("Create ").Append(type.ShortCode).Append(": '").Append(name).Append('\'');
        if (!string.IsNullOrEmpty(status)) sb.Append(" [status=").Append(status).Append(']');

        if (values.ValueKind == JsonValueKind.Object)
        {
            var pairs = new List<string>();
            foreach (var prop in values.EnumerateObject())
            {
                pairs.Add($"{prop.Name}={JsonValueToDisplay(prop.Value)}");
            }
            if (pairs.Count > 0)
            {
                sb.Append(" with ").Append(string.Join(", ", pairs));
            }
        }
        return sb.ToString();
    }

    private static string BuildUpdateSummary(Record record, IReadOnlyList<object> fieldChanges)
    {
        if (fieldChanges.Count == 0)
        {
            return $"{record.Key}: no changes.";
        }
        return $"{record.Key}: {fieldChanges.Count} change{(fieldChanges.Count == 1 ? string.Empty : "s")}.";
    }

    private static string JsonValueToDisplay(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.ToString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => element.GetRawText()
    };

    private static JsonElement Error(string source, string message) =>
        JsonSerializer.SerializeToElement(new
        {
            kind = "error",
            source,
            data = new { message }
        });

    private static JsonElement Failed(string source, string operation, RecordValidationException ex) =>
        JsonSerializer.SerializeToElement(new
        {
            kind = "record_change_failed",
            source = "ManageRecordsSkill",
            data = new
            {
                operation,
                message = ex.Message,
                validation = new
                {
                    ok = false,
                    errors = ex.Errors.Select(e => new { code = e.Code, message = e.Message }).ToArray()
                }
            }
        });

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
