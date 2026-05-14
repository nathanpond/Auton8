namespace AutoNate.Web.Services.Content;

// History-event flavours stored in page_versions.kind and note_versions.kind.
// 'autosave' is for SPA-driven periodic saves (reserved for future SPA work);
// 'manual' is for explicit save / patch-driven snapshots; 'restore' is the
// snapshot of `current` taken right before a restore overwrites it.
public static class ContentVersionKinds
{
    public const string Autosave = "autosave";
    public const string Manual = "manual";
    public const string Restore = "restore";
}
