namespace AutoNate.Web.Persistence.Scaffolded;

// Catalogued LLM model row. Drives the External Connections dropdown and
// the agent loop's per-model context-window lookup. ModelId is the canonical
// identifier sent to the provider (e.g. "claude-sonnet-4-6"); DisplayName
// is the friendly label admins see. Costs are stored per million tokens.
// Archive (don't delete) so a connection still pinned to a retired model can
// resolve its context window for trimmer/compactor calculations.
public partial class AgentModel
{
    public Guid Id { get; set; }

    public string ModelId { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string Provider { get; set; } = null!;

    public int ContextWindowTokens { get; set; }

    public decimal? InputCostPerMillionTokens { get; set; }

    public decimal? OutputCostPerMillionTokens { get; set; }

    public string CostCurrency { get; set; } = "USD";

    public DateTime? CostPublishedAtUtc { get; set; }

    public string? Description { get; set; }

    public bool IsArchived { get; set; }

    // Per-provider default. AgentSession picks this row when a connection
    // doesn't explicitly pin a model. Enforced unique-per-provider via a
    // partial index on (provider) WHERE is_default = TRUE.
    public bool IsDefault { get; set; }

    // Whether the agent may select this model for autonomous task routing.
    // Independent of IsArchived: an archived row is hidden from the UI
    // entirely; an unavailable-but-not-archived row stays visible (and
    // selectable as a connection's pinned model) but the agent's task
    // router won't pick it on its own. Routing parameters themselves are
    // out of scope for the current iteration.
    public bool IsAvailable { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
