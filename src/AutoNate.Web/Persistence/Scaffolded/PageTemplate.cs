using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class PageTemplate
{
    public Guid Id { get; set; }

    public string Key { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? ThumbnailUrl { get; set; }

    public string? Category { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    // Set when a plugin auto-registered this template from its
    // <pluginFolder>/PageTemplates/*.template files. Null for built-in
    // host templates. CASCADE on the plugins row ensures these go away with
    // the plugin that registered them.
    public Guid? CreatedByPluginId { get; set; }

    // "builtin" for templates the host SPA renders by key (PAGE_TEMPLATES
    // map); "jsx" for plugin templates whose source is stored in Content and
    // compiled at request time via the SPA's JsxPage component.
    public string ContentType { get; set; } = "builtin";

    // JSX source for plugin-supplied templates. NULL for built-in templates.
    public string? Content { get; set; }
}
