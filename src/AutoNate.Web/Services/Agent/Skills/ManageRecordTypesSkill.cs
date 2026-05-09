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
