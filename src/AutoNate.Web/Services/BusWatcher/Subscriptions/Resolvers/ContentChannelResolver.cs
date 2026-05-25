using System.Collections.Concurrent;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions.Resolvers;

// Maps a content.events message to instance + ancestor channels:
//   project:{id}    — for any event whose closest project ancestor is `id`.
//   cabinet:{id}    — for any event whose closest cabinet ancestor is `id`.
//   notebook:{id}   — for any event whose closest notebook ancestor is `id`.
//   page:{id}       — for any page event (kept for back-compat with the
//                     existing per-page channel subscribers).
//
// Lookups go through `content_ancestors` rather than enriching publishers
// with the full chain, so per-event payload size stays bounded. A short-TTL
// LRU keeps the lookup off the hot path under normal traffic where the same
// page/notebook is touched repeatedly. Per-recipient IContentAuthorizer view
// gates run downstream in SubscriptionManager so each subscriber only sees
// events for resources they can already view.
//
// Notes don't have closure-table rows — they hang off a page. We resolve the
// note's pageId via the notes table, then reuse the page's ancestor chain.
public sealed class ContentChannelResolver(ContentAncestorCache cache) : IChannelResolver
{
    public string Topic => ContentEventTopic.TopicName;

    // Required by the interface but unused — all our delivery decisions need
    // ResolveAsync. Return empty so any accidental sync caller is a no-op
    // rather than a partial answer.
    public IReadOnlyList<ResolvedDelivery> Resolve(BusWatcherStreamService.BusWatcherMessage message) =>
        Array.Empty<ResolvedDelivery>();

    public async Task<IReadOnlyList<ResolvedDelivery>> ResolveAsync(
        BusWatcherStreamService.BusWatcherMessage message,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!TryParseEnvelope(message.Payload, out var resourceKind, out var resourceId, out var details))
        {
            return Array.Empty<ResolvedDelivery>();
        }

        // Translate (resourceKind, id) to the leaf in the content closure.
        // For page/notebook/cabinet/project the id IS the leaf. For note /
        // comment / page-version / note-version we resolve to the owning
        // page.
        var (leafKind, leafId) = await ResolveLeafAsync(
            services, resourceKind, resourceId, details, cancellationToken);
        if (leafKind is null || leafId is null) return Array.Empty<ResolvedDelivery>();

        var deliveries = new List<ResolvedDelivery>(4);
        var seenChannels = new HashSet<string>(StringComparer.Ordinal);

        // Always emit the leaf-kind delivery — even when the closure-table
        // lookup returns no rows (e.g. the entity was deleted, or the event
        // refers to a synthetic id from a test). This preserves the legacy
        // per-page channel behavior unconditionally.
        AppendDelivery(leafKind, leafId.Value, deliveries, seenChannels);

        await EmitAncestorDeliveriesAsync(
            services, leafKind, leafId.Value, deliveries, seenChannels, cancellationToken);

        // PageMoved / NotebookMoved / CabinetMoved also need to fan out to
        // the previous-location ancestor chain so the source tree refreshes.
        await EmitMoveSourceDeliveriesAsync(
            services, resourceKind, details, deliveries, seenChannels, cancellationToken);

        return deliveries;
    }

    // ---- payload parsing -------------------------------------------------

    private static bool TryParseEnvelope(
        string payload,
        out string resourceKind,
        out string resourceId,
        out JsonElement details)
    {
        resourceKind = string.Empty;
        resourceId = string.Empty;
        details = default;
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

            if (!doc.RootElement.TryGetProperty("resourceKind", out var kindEl)
                || kindEl.ValueKind != JsonValueKind.String) return false;
            var kind = kindEl.GetString();
            if (string.IsNullOrEmpty(kind)) return false;

            if (!doc.RootElement.TryGetProperty("resource", out var resourceEl)
                || resourceEl.ValueKind != JsonValueKind.Object) return false;
            if (!resourceEl.TryGetProperty("id", out var idEl)
                || idEl.ValueKind != JsonValueKind.String) return false;
            var id = idEl.GetString();
            if (string.IsNullOrEmpty(id)) return false;

            resourceKind = kind;
            resourceId = id;
            details = doc.RootElement.TryGetProperty("details", out var d) ? d.Clone() : default;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // ---- leaf resolution -------------------------------------------------

    private async Task<(string? kind, Guid? id)> ResolveLeafAsync(
        IServiceProvider services,
        string resourceKind,
        string resourceId,
        JsonElement details,
        CancellationToken cancellationToken)
    {
        switch (resourceKind)
        {
            case ContentResourceKinds.Project:
                return Guid.TryParse(resourceId, out var pid) ? (ContentKinds.Project, pid) : (null, null);
            case ContentResourceKinds.Cabinet:
                return Guid.TryParse(resourceId, out var cid) ? (ContentKinds.Cabinet, cid) : (null, null);
            case ContentResourceKinds.Notebook:
                return Guid.TryParse(resourceId, out var nid) ? (ContentKinds.Notebook, nid) : (null, null);
            case ContentResourceKinds.Page:
                return Guid.TryParse(resourceId, out var pgid) ? (ContentKinds.Page, pgid) : (null, null);

            // page.version events carry resource = { pageId, versionNumber } —
            // the closure lives under the page.
            case ContentResourceKinds.PageVersion:
                return TryReadGuidProp(details, "pageId", out var pvPageId)
                    ? (ContentKinds.Page, pvPageId)
                    : (null, null);
            case ContentResourceKinds.PageAttachment:
                return TryReadGuidProp(details, "pageId", out var paPageId)
                    ? (ContentKinds.Page, paPageId)
                    : (null, null);

            // notes / note.versions / comments hang off a page; look up the
            // owning pageId so we can fan out to the page's ancestors.
            case ContentResourceKinds.Note:
                if (!Guid.TryParse(resourceId, out var noteId)) return (null, null);
                {
                    var pageId = await cache.GetNotePageIdAsync(services, noteId, cancellationToken);
                    return pageId is { } p ? (ContentKinds.Page, p) : (null, null);
                }
            case ContentResourceKinds.NoteVersion:
                if (!TryReadGuidProp(details, "noteId", out var nvNoteId)) return (null, null);
                {
                    var pageId = await cache.GetNotePageIdAsync(services, nvNoteId, cancellationToken);
                    return pageId is { } p ? (ContentKinds.Page, p) : (null, null);
                }
            case ContentResourceKinds.Comment:
                return TryReadGuidProp(details, "pageId", out var cmPageId)
                    ? (ContentKinds.Page, cmPageId)
                    : (null, null);

            // ProjectMember events scope to a project — fan out to project:{id}.
            case ContentResourceKinds.ProjectMember:
                return TryReadGuidProp(details, "projectId", out var pmProjectId)
                    ? (ContentKinds.Project, pmProjectId)
                    : (null, null);

            default:
                return (null, null);
        }
    }

    // ---- ancestor fan-out ------------------------------------------------

    private async Task EmitAncestorDeliveriesAsync(
        IServiceProvider services,
        string leafKind,
        Guid leafId,
        List<ResolvedDelivery> output,
        HashSet<string> seenChannels,
        CancellationToken cancellationToken)
    {
        var chain = await cache.GetAncestorsAsync(services, leafKind, leafId, cancellationToken);
        AppendChainDeliveries(chain, output, seenChannels);
    }

    private async Task EmitMoveSourceDeliveriesAsync(
        IServiceProvider services,
        string resourceKind,
        JsonElement details,
        List<ResolvedDelivery> output,
        HashSet<string> seenChannels,
        CancellationToken cancellationToken)
    {
        if (details.ValueKind != JsonValueKind.Object) return;

        // Page moved between notebooks → also notify the source notebook /
        // cabinet / project so trees on that side refresh.
        if (resourceKind == ContentResourceKinds.Page
            && TryReadGuidProp(details, "previousNotebookId", out var prevNotebookId))
        {
            AppendDelivery(ContentKinds.Notebook, prevNotebookId, output, seenChannels);
            var chain = await cache.GetAncestorsAsync(
                services, ContentKinds.Notebook, prevNotebookId, cancellationToken);
            AppendChainDeliveries(chain, output, seenChannels);
        }
        else if (resourceKind == ContentResourceKinds.Notebook
            && TryReadGuidProp(details, "previousCabinetId", out var prevCabinetId))
        {
            AppendDelivery(ContentKinds.Cabinet, prevCabinetId, output, seenChannels);
            var chain = await cache.GetAncestorsAsync(
                services, ContentKinds.Cabinet, prevCabinetId, cancellationToken);
            AppendChainDeliveries(chain, output, seenChannels);
        }
        else if (resourceKind == ContentResourceKinds.Cabinet
            && TryReadGuidProp(details, "previousProjectId", out var prevProjectId))
        {
            AppendDelivery(ContentKinds.Project, prevProjectId, output, seenChannels);
            var chain = await cache.GetAncestorsAsync(
                services, ContentKinds.Project, prevProjectId, cancellationToken);
            AppendChainDeliveries(chain, output, seenChannels);
        }
    }

    private static void AppendChainDeliveries(
        ContentAncestorCache.AncestorChain chain,
        List<ResolvedDelivery> output,
        HashSet<string> seenChannels)
    {
        if (chain.ProjectId is { } pid)
            AppendDelivery(ContentKinds.Project, pid, output, seenChannels);
        if (chain.CabinetId is { } cid)
            AppendDelivery(ContentKinds.Cabinet, cid, output, seenChannels);
        if (chain.NotebookId is { } nid)
            AppendDelivery(ContentKinds.Notebook, nid, output, seenChannels);
        if (chain.PageId is { } pgid)
            AppendDelivery(ContentKinds.Page, pgid, output, seenChannels);
    }

    private static void AppendDelivery(
        string kind, Guid id, List<ResolvedDelivery> output, HashSet<string> seenChannels)
    {
        var channelName = $"{kind}:{id}";
        if (!seenChannels.Add(channelName)) return;
        var entityKind = kind switch
        {
            ContentKinds.Project => EntityKinds.Project,
            ContentKinds.Cabinet => EntityKinds.Cabinet,
            ContentKinds.Notebook => EntityKinds.Notebook,
            ContentKinds.Page => EntityKinds.Page,
            _ => kind
        };
        var target = new EntityRef(entityKind, id.ToString());
        output.Add(new ResolvedDelivery(channelName, target, FastGate: null));
    }

    // ---- helpers ---------------------------------------------------------

    private static bool TryReadGuidProp(JsonElement element, string name, out Guid value)
    {
        value = Guid.Empty;
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (!element.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) return false;
        return Guid.TryParse(v.GetString(), out value);
    }
}

public static class ContentChannelNames
{
    public const string ProjectInstanceKind = "project";
    public const string CabinetInstanceKind = "cabinet";
    public const string NotebookInstanceKind = "notebook";
    public const string PageInstanceKind = "page";
}

// Singleton ancestor cache. Two caches under the hood:
//   - chain: (descendantKind, descendantId) → closest ancestor of each kind.
//   - notePage: noteId → owning pageId.
// Both use a per-entry expiry so structural moves age out without the
// resolver needing to listen for invalidation events. Entries are cheap;
// content trees aren't deep and moves are rare relative to events.
public sealed class ContentAncestorCache
{
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<(string Kind, Guid Id), CacheEntry<AncestorChain>> _chains = new();
    private readonly ConcurrentDictionary<Guid, CacheEntry<Guid?>> _notePages = new();

    public async Task<AncestorChain> GetAncestorsAsync(
        IServiceProvider services, string descendantKind, Guid descendantId, CancellationToken ct)
    {
        var key = (descendantKind, descendantId);
        if (_chains.TryGetValue(key, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
        {
            return cached.Value;
        }

        var dbFactory = services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ContentAncestors.AsNoTracking()
            .Where(a => a.DescendantKind == descendantKind && a.DescendantId == descendantId)
            .Select(a => new { a.AncestorKind, a.AncestorId })
            .ToListAsync(ct);

        var chain = new AncestorChain();
        foreach (var row in rows)
        {
            switch (row.AncestorKind)
            {
                case ContentKinds.Project: chain.ProjectId = row.AncestorId; break;
                case ContentKinds.Cabinet: chain.CabinetId = row.AncestorId; break;
                case ContentKinds.Notebook: chain.NotebookId = row.AncestorId; break;
                case ContentKinds.Page: chain.PageId = row.AncestorId; break;
            }
        }
        _chains[key] = new CacheEntry<AncestorChain>(chain, DateTime.UtcNow + _ttl);
        return chain;
    }

    public async Task<Guid?> GetNotePageIdAsync(
        IServiceProvider services, Guid noteId, CancellationToken ct)
    {
        if (_notePages.TryGetValue(noteId, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
        {
            return cached.Value;
        }
        var dbFactory = services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pageId = await db.Notes.AsNoTracking()
            .Where(n => n.Id == noteId)
            .Select(n => (Guid?)n.PageId)
            .FirstOrDefaultAsync(ct);
        _notePages[noteId] = new CacheEntry<Guid?>(pageId, DateTime.UtcNow + _ttl);
        return pageId;
    }

    public sealed class AncestorChain
    {
        public Guid? ProjectId { get; set; }
        public Guid? CabinetId { get; set; }
        public Guid? NotebookId { get; set; }
        // PageId only populated for descendants whose closest page ancestor
        // exists (i.e. note/comment leaves where the page itself is in the
        // chain, and page descendants are themselves the page).
        public Guid? PageId { get; set; }
    }

    private readonly record struct CacheEntry<T>(T Value, DateTime ExpiresAtUtc);
}
