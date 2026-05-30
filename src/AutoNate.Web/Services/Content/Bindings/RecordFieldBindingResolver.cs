using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Records;

namespace AutoNate.Web.Services.Content.Bindings;

// The simplest binding kind: dereference one field from one record.
//
// Config shape (camelCase wire convention):
//   { recordId: guid, fieldKey: string }
//
// Resolved value shape:
//   { text: string, type: "text" | "number" | "date" | "missing" | "denied",
//     rawValue: any | null }
//
// `text` is what the in-document widget renders by default; `type` lets
// the widget right-align numbers or format dates without re-parsing.
// `rawValue` is the JSON-typed value for callers who want it (export,
// downstream resolvers).
//
// Permission model: this resolver authorizes Record.View on the target
// record via IAuthorizer.AuthorizeAsync. A denied lookup returns a
// "denied"-type value rather than throwing — the user inserting the
// binding may have access while a later viewer doesn't, and we want the
// document to render legibly in both cases rather than 403'ing the
// whole refresh.
public sealed class RecordFieldBindingResolver : IDocumentBindingResolver
{
    private readonly IAuthorizer _authorizer;
    private readonly IRecordStore _records;

    public RecordFieldBindingResolver(
        IAuthorizer authorizer,
        IRecordStore records)
    {
        _authorizer = authorizer;
        _records = records;
    }

    public string Kind => DocumentBindingKinds.RecordField;

    public async Task<DocumentBindingResolveResult> ResolveAsync(
        string configJsonb,
        ClaimsPrincipal actor,
        CancellationToken ct)
    {
        var config = ParseConfig(configJsonb);

        var decision = await _authorizer.AuthorizeAsync(
            actor,
            Actions.View,
            new EntityRef(EntityKinds.Record, config.RecordId.ToString()),
            ct);
        if (!decision.IsAllowed)
        {
            // Don't throw — the binding still has a meaningful render:
            // "(no permission)". Stamping a denied snapshot also makes
            // it visible in audit which user couldn't see what.
            return Build(
                JsonSerializer.Serialize(new
                {
                    text = "(no permission)",
                    type = "denied",
                    rawValue = (object?)null
                }),
                $"Record field (denied)");
        }

        var record = await _records.GetAsync(config.RecordId, ct);
        if (record is null)
        {
            return Build(
                JsonSerializer.Serialize(new
                {
                    text = "(record deleted)",
                    type = "missing",
                    rawValue = (object?)null
                }),
                $"Record field (missing)");
        }

        if (!record.Values.TryGetProperty(config.FieldKey, out var fieldEl)
            || fieldEl.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Build(
                JsonSerializer.Serialize(new
                {
                    text = "(empty)",
                    type = "missing",
                    rawValue = (object?)null
                }),
                $"Record field: {config.FieldKey}");
        }

        var (text, type) = FormatFieldValue(fieldEl);
        var rawValueJson = fieldEl.GetRawText();
        // Build the resolved value as a JSON object whose `rawValue` is
        // the field's original JSON (not stringified) so consumers can
        // round-trip non-string types. JsonSerializer doesn't easily mix
        // typed object literals with raw JSON; assemble by hand.
        var resolved =
            "{" +
            $"\"text\":{JsonSerializer.Serialize(text)}," +
            $"\"type\":{JsonSerializer.Serialize(type)}," +
            $"\"rawValue\":{rawValueJson}" +
            "}";

        return Build(resolved, $"{record.Name}.{config.FieldKey}");
    }

    // Format the JSON element to a display string + a coarse type tag
    // for the widget to style. Dates are stored as ISO strings in JSONB
    // and surface as `JsonValueKind.String` — we leave them as-is so
    // the widget can choose its own date format (Mantine's `dayjs`
    // helpers, the user's locale, etc.).
    private static (string text, string type) FormatFieldValue(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => (el.GetString() ?? string.Empty, GuessStringType(el.GetString())),
            JsonValueKind.Number => (el.GetRawText(), "number"),
            JsonValueKind.True or JsonValueKind.False => (el.GetBoolean() ? "true" : "false", "boolean"),
            JsonValueKind.Array or JsonValueKind.Object => (el.GetRawText(), "json"),
            _ => (string.Empty, "missing")
        };
    }

    // ISO 8601 date / datetime detection on a string — coarse enough to
    // tag formatting. Anything more sophisticated would need the
    // record-type's schema, which lives in `record_type_fields`; v1
    // skips that join for simplicity.
    private static string GuessStringType(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "text";
        if (s.Length >= 10 && s[4] == '-' && s[7] == '-' &&
            DateOnly.TryParse(s.AsSpan(0, 10), out _))
        {
            return "date";
        }
        return "text";
    }

    private static RecordFieldBindingConfig ParseConfig(string configJsonb)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<RecordFieldBindingConfig>(
                configJsonb,
                ConfigJsonOpts);
            if (parsed is null) throw new DocumentBindingResolveException(
                "record-field config is empty.");
            if (parsed.RecordId == Guid.Empty) throw new DocumentBindingResolveException(
                "record-field config: recordId is required.");
            if (string.IsNullOrWhiteSpace(parsed.FieldKey)) throw new DocumentBindingResolveException(
                "record-field config: fieldKey is required.");
            return parsed;
        }
        catch (JsonException ex)
        {
            throw new DocumentBindingResolveException(
                $"record-field config: malformed JSON ({ex.Message}).");
        }
    }

    private static DocumentBindingResolveResult Build(string resolvedJson, string label) =>
        new(resolvedJson, label);

    private static readonly JsonSerializerOptions ConfigJsonOpts =
        new(JsonSerializerDefaults.Web);

    private sealed record RecordFieldBindingConfig(Guid RecordId, string FieldKey);
}
