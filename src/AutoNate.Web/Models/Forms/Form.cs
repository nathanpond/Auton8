namespace AutoNate.Web.Models.Forms;

public sealed record class Form
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string ShortCode { get; init; } = string.Empty;

    public string FormCode { get; init; } = string.Empty;

    public bool SiteAvailable { get; init; }

    public bool IsDraft { get; init; } = true;

    public int DraftVersionNumber { get; init; } = 1;

    public int? PublishedVersionNumber { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedBy { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public Guid UpdatedBy { get; init; }
}

public sealed record class FormVersion
{
    public Guid Id { get; init; }

    public Guid FormId { get; init; }

    public int VersionNumber { get; init; }

    public string Name { get; init; } = string.Empty;

    public string ShortCode { get; init; } = string.Empty;

    public string FormCode { get; init; } = string.Empty;

    public bool SiteAvailable { get; init; }

    // 'save' | 'publish' | 'restore'
    public string Kind { get; init; } = FormVersionKinds.Save;

    public string? Note { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedBy { get; init; }
}

public static class FormVersionKinds
{
    public const string Save = "save";
    public const string Publish = "publish";
    public const string Restore = "restore";
}

public sealed record class CreateFormRequest(
    string Name,
    string ShortCode,
    string? FormCode,
    bool? SiteAvailable);

public sealed record class SaveFormRequest(
    string Name,
    string ShortCode,
    string FormCode,
    bool SiteAvailable);

public sealed record class FormSummary(
    Guid Id,
    string Name,
    string ShortCode,
    bool SiteAvailable,
    bool IsDraft,
    int DraftVersionNumber,
    int? PublishedVersionNumber,
    DateTimeOffset UpdatedAtUtc);

public sealed record class FormDraftSnapshot(
    Guid Id,
    string Name,
    string ShortCode,
    string FormCode,
    bool SiteAvailable,
    int DraftVersionNumber,
    int? PublishedVersionNumber);

public sealed record class FormPublishedSnapshot(
    Guid FormId,
    string Name,
    string ShortCode,
    string FormCode,
    int VersionNumber,
    DateTimeOffset PublishedAtUtc);

public sealed record class FormWorkflowSnapshot(
    Guid FormId,
    string Name,
    string ShortCode,
    string FormCode,
    int? PublishedVersionNumber,
    bool IsDraftFallback);
