namespace AutoNate.Web.Persistence.Scaffolded;

// One thread of conversation between a user and the agent. PageKey is derived
// in the SPA from the current route (e.g. "workflow", "records",
// "system-issues") so the right-side chat sidebar can show the user just the
// conversations relevant to where they are. ProviderKind and ModelId are
// locked at conversation creation so token accounting and replay stay
// consistent across messages — switching provider mid-conversation would
// confuse cache-read accounting and tool-schema translation.
public partial class AgentConversation
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string PageKey { get; set; } = null!;

    public string? Title { get; set; }

    public string? ProviderKind { get; set; }

    public string? ModelId { get; set; }

    // Nullable so deleting an external connection only blanks the link; the
    // conversation itself stays readable. Sending a NEW message after the
    // connection is gone is the agent loop's responsibility to handle (it
    // re-resolves a default for the kind or surfaces a friendly error).
    public Guid? ConnectionId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? LastMessageAtUtc { get; set; }
}
