using System.Text.Json;

namespace AutoNate.Web.Services.Agent.Skills.Internal;

// Shared two-phase confirm-gate envelope for every Phase 3 manage skill.
// Mirrors the contract first introduced by ManageRecordsSkill: a tool with
// a `confirmed: bool` arg returns a structured proposal envelope when
// confirmed=false, and only mutates downstream when confirmed=true. The
// LLM is expected to narrate the proposal to the user, get explicit
// approval, then re-call with confirmed=true.
//
// Per-skill code stays terse — it just calls IsConfirmed(args) and, when
// false, returns Proposal(...); on success, returns Committed(...); on
// downstream failure, Failed(...). The envelope shape is identical across
// every manage skill so Phase 4 plugin authors can copy one pattern.
internal static class ConfirmGate
{
    // Reads the optional `confirmed: bool` flag from the tool arguments.
    // Treated as false when omitted, null, or non-boolean.
    public static bool IsConfirmed(JsonElement args) =>
        args.TryGetProperty("confirmed", out var c)
        && c.ValueKind == JsonValueKind.True;

    // Dry-run preview the model can read aloud. `kind` is a stable string
    // the model uses to dispatch in chat ("note_create_proposal", etc.);
    // `action` is the human verb. `preview` is whatever structured detail
    // the model needs to compose its summary (changed field list, target id,
    // etc.). Always sets needsConfirmation=true so the model knows the next
    // call must include confirmed=true.
    public static JsonElement Proposal(
        string kind,
        string action,
        object preview,
        string? message = null) =>
        JsonSerializer.SerializeToElement(new
        {
            kind,
            source = "ConfirmGate",
            data = new
            {
                action,
                confirmed = false,
                needsConfirmation = true,
                message = message ?? $"Proposal to {action}. Confirm with the user, then re-call with confirmed=true.",
                preview
            }
        });

    // Successful commit envelope. `kind` typically mirrors the proposal kind
    // with `_committed` instead of `_proposal`, e.g. "note_create_committed".
    public static JsonElement Committed(
        string kind,
        string action,
        object result,
        string? message = null) =>
        JsonSerializer.SerializeToElement(new
        {
            kind,
            source = "ConfirmGate",
            data = new
            {
                action,
                confirmed = true,
                committed = true,
                message = message ?? $"{action} committed successfully.",
                result
            }
        });

    // Downstream failure envelope — used when the store throws a validation
    // exception or the authorizer denies the commit step. The model can
    // re-narrate to the user without falling back to a generic error.
    public static JsonElement Failed(
        string kind,
        string action,
        string error,
        object? details = null) =>
        JsonSerializer.SerializeToElement(new
        {
            kind,
            source = "ConfirmGate",
            data = new
            {
                action,
                confirmed = true,
                committed = false,
                error,
                details
            }
        });

    // Validation/short-circuit envelope used before either path runs (missing
    // required args, target not found, etc.). Distinct from Failed because no
    // commit was attempted.
    public static JsonElement Rejected(
        string action,
        string error,
        object? details = null) =>
        JsonSerializer.SerializeToElement(new
        {
            kind = "manage_change_rejected",
            source = "ConfirmGate",
            data = new
            {
                action,
                rejected = true,
                error,
                details
            }
        });
}
