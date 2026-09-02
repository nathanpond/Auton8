using System.Security.Cryptography;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Endpoints;

public static class PageAttachmentEndpoints
{
    public static IEndpointRouteBuilder MapPageAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var pageScoped = app.MapGroup("/api/content/pages/{pageId:guid}/attachments")
            .RequireAuthorization();
        var directScoped = app.MapGroup("/api/content/attachments").RequireAuthorization();

        pageScoped.MapGet("/", async (
            Guid pageId,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var items = await db.PageAttachments.AsNoTracking()
                .Where(a => a.PageId == pageId)
                .OrderBy(a => a.CreatedAtUtc)
                .ToListAsync(ct);
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageAttachmentListViewed,
                ContentResourceKinds.PageAttachment,
                resource: new { pageId },
                details: new { resultCount = items.Count },
                ct);
            return Results.Ok(items.Select(MapDto));
        }).RequirePermission(EntityKinds.Page, Actions.View, "pageId");

        // Multipart upload. Streams to disk via the configured store after
        // first validating size + content-type and writing the metadata row.
        pageScoped.MapPost("/", async (
            Guid pageId,
            HttpRequest req,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAttachmentStore store,
            IOptions<ContentAttachmentOptions> opts,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (!req.HasFormContentType)
            {
                return Results.BadRequest(new { error = "multipart/form-data required." });
            }
            var form = await req.ReadFormAsync(ct);
            if (form.Files.Count == 0)
            {
                return Results.BadRequest(new { error = "No file provided." });
            }
            var file = form.Files[0];
            var options = opts.Value;
            if (file.Length > options.MaxBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }
            if (!IsContentTypeAllowed(options, file.ContentType))
            {
                return Results.BadRequest(new
                {
                    error = $"Content type '{file.ContentType}' is not allowed."
                });
            }

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            // Resolve project id for the storage key. Going via content_ancestors
            // keeps this independent of any path-walking logic in the endpoint.
            var projectId = await db.ContentAncestors.AsNoTracking()
                .Where(ca => ca.DescendantKind == ContentKinds.Page
                             && ca.DescendantId == pageId
                             && ca.AncestorKind == ContentKinds.Project)
                .Select(ca => (Guid?)ca.AncestorId)
                .FirstOrDefaultAsync(ct);
            if (projectId is null) return Results.BadRequest(new { error = "Page not found." });

            var actorId = http.GetActorId();
            var attachmentId = Guid.NewGuid();

            // SHA-256 + persist bytes in one stream pass; metadata then commits.
            await using var stream = file.OpenReadStream();
            await using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            // Strict magic-byte check. The client-supplied Content-Type
            // is untrusted — a script could claim image/png while
            // uploading HTML or SVG. Reject anything whose bytes don't
            // match a known-safe binary signature, or whose claimed type
            // doesn't agree with the sniffed family.
            var sniffed = ContentTypeSniffer.Sniff(bytes);
            if (sniffed is null)
            {
                return Results.BadRequest(new
                {
                    error = "Unsupported file format. The uploaded bytes do " +
                            "not match any allowed file type."
                });
            }
            if (!ContentTypeSniffer.ClientTypeMatchesSniff(sniffed, file.ContentType))
            {
                return Results.BadRequest(new
                {
                    error = $"Content type '{file.ContentType}' does not " +
                            "match the file's actual format."
                });
            }

            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            using var writeStream = new MemoryStream(bytes, writable: false);
            var storageKey = await store.WriteAsync(projectId.Value, attachmentId, writeStream, ct);

            var now = DateTime.UtcNow;
            var safeFileName = SanitizeFileName(file.FileName);
            var attachment = new PageAttachment
            {
                Id = attachmentId,
                PageId = pageId,
                FileName = safeFileName,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                    ? "application/octet-stream"
                    : file.ContentType,
                ByteSize = bytes.LongLength,
                Sha256Hex = hash,
                StorageKey = storageKey,
                IsArchived = false,
                CreatedAtUtc = now,
                CreatedBy = actorId,
                UpdatedAtUtc = now,
                UpdatedBy = actorId
            };
            db.PageAttachments.Add(attachment);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch
            {
                // Roll back the bytes if the metadata insert fails so we don't
                // orphan the file.
                await store.DeleteAsync(storageKey, ct);
                throw;
            }

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageAttachmentUploaded,
                ContentResourceKinds.PageAttachment,
                resource: new
                {
                    id = attachment.Id,
                    pageId = attachment.PageId,
                    fileName = attachment.FileName,
                    contentType = attachment.ContentType,
                    byteSize = attachment.ByteSize,
                    sha256Hex = attachment.Sha256Hex
                },
                details: null,
                ct);

            return Results.Created($"/api/content/attachments/{attachment.Id}", MapDto(attachment));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Page, Actions.Edit, "pageId");

        directScoped.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var attachment = await db.PageAttachments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id, ct);
            if (attachment is null) return Results.NotFound();
            if (!await CheckPageActionAsync(authorizer, http, attachment.PageId, Actions.View, ct))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Ok(MapDto(attachment));
        }).AuthorizedInHandler(
            "Page.View via AuthorizeAsync on the attachment's owning page.");

        directScoped.MapGet("/{id:guid}/download", async (
            Guid id,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentAttachmentStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var attachment = await db.PageAttachments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id, ct);
            if (attachment is null) return Results.NotFound();
            if (!await CheckPageActionAsync(authorizer, http, attachment.PageId, Actions.View, ct))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageAttachmentDownloaded,
                ContentResourceKinds.PageAttachment,
                resource: new
                {
                    id = attachment.Id,
                    pageId = attachment.PageId,
                    fileName = attachment.FileName,
                    byteSize = attachment.ByteSize
                },
                details: null,
                ct);

            var stream = await store.ReadAsync(attachment.StorageKey, ct);
            // Defense in depth on the way out: belt-and-braces against
            // rows that pre-date the strict upload sniff, or against any
            // future code path that bypasses it. Rewrite active-content
            // types to octet-stream, refuse MIME sniffing, and sandbox
            // any sub-resource embed.
            var responseContentType = SanitizeResponseContentType(attachment.ContentType);
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            http.Response.Headers["Content-Security-Policy"] =
                "default-src 'none'; sandbox; frame-ancestors 'none'";
            return Results.File(stream, responseContentType, attachment.FileName);
        }).AuthorizedInHandler(
            "Page.View via AuthorizeAsync on the attachment's owning page.");

        directScoped.MapPatch("/{id:guid}", async (
            Guid id,
            RenameAttachmentRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var attachment = await db.PageAttachments.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (attachment is null) return Results.NotFound();
            if (!await CheckPageActionAsync(authorizer, http, attachment.PageId, Actions.Edit, ct))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (string.IsNullOrWhiteSpace(request.FileName))
                return Results.BadRequest(new { error = "fileName cannot be empty." });

            var previousName = attachment.FileName;
            attachment.FileName = SanitizeFileName(request.FileName);
            attachment.UpdatedAtUtc = DateTime.UtcNow;
            attachment.UpdatedBy = http.GetActorId();
            await db.SaveChangesAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageAttachmentRenamed,
                ContentResourceKinds.PageAttachment,
                resource: new
                {
                    id = attachment.Id,
                    pageId = attachment.PageId,
                    fileName = attachment.FileName
                },
                details: new { previousFileName = previousName },
                ct);

            return Results.Ok(MapDto(attachment));
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "Page.Edit via AuthorizeAsync on the attachment's owning page.");

        directScoped.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentAttachmentStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var attachment = await db.PageAttachments.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (attachment is null) return Results.NotFound();
            // Delete on the page — also subject to deletions_locked because
            // attachments live inside the project's locked-content envelope.
            if (!await CheckPageActionAsync(authorizer, http, attachment.PageId, Actions.Delete, ct))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var snapshot = new
            {
                id = attachment.Id,
                pageId = attachment.PageId,
                fileName = attachment.FileName,
                contentType = attachment.ContentType,
                byteSize = attachment.ByteSize
            };
            var storageKey = attachment.StorageKey;
            db.PageAttachments.Remove(attachment);
            await db.SaveChangesAsync(ct);
            // Best-effort bytes cleanup after the metadata row is gone — the
            // store logs + swallows IOExceptions so orphan bytes don't fail
            // the request.
            await store.DeleteAsync(storageKey, ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.PageAttachmentDeleted,
                ContentResourceKinds.PageAttachment,
                resource: snapshot,
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "Page.Delete via AuthorizeAsync on the attachment's owning " +
              "page; that decision also honours the project's deletions " +
              "lock.");

        return app;
    }

    private static async Task<bool> CheckPageActionAsync(
        IContentAuthorizer authorizer, HttpContext http, Guid pageId, string action,
        CancellationToken ct)
    {
        var decision = await authorizer.AuthorizeAsync(
            http.User, ContentKinds.Page, pageId, action, ct);
        return decision.IsAllowed;
    }

    private static bool IsContentTypeAllowed(ContentAttachmentOptions options, string? contentType)
    {
        if (options.AllowedContentTypes.Count == 0) return true;
        var ct = string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType;
        foreach (var pattern in options.AllowedContentTypes)
        {
            if (string.Equals(pattern, "*/*", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(pattern, ct, StringComparison.OrdinalIgnoreCase)) return true;
            if (pattern.EndsWith("/*", StringComparison.Ordinal))
            {
                var prefix = pattern[..^1]; // includes trailing slash
                if (ct.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    // Content types that browsers may render as active content even with
    // Content-Disposition: attachment when embedded via <iframe>, <object>,
    // <embed>, or <img> (SVG). On download these are forced to
    // application/octet-stream.
    // Shared with the datastore download so the two cannot drift (archived-65).
    private static string SanitizeResponseContentType(string? contentType) =>
        ResponseContentTypes.Sanitize(contentType);

    private static string SanitizeFileName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "attachment";
        // Strip path components — never trust client-provided directory paths.
        var name = Path.GetFileName(raw).Trim();
        if (string.IsNullOrWhiteSpace(name)) return "attachment";
        if (name.Length > 256) name = name[..256];
        return name;
    }

    internal static PageAttachmentDto MapDto(PageAttachment a) => new(
        a.Id, a.PageId, a.FileName, a.ContentType, a.ByteSize, a.Sha256Hex,
        a.IsArchived, a.CreatedAtUtc, a.UpdatedAtUtc, a.CreatedBy, a.UpdatedBy);

    public sealed record RenameAttachmentRequest(string FileName);

    public sealed record PageAttachmentDto(
        Guid Id, Guid PageId, string FileName, string ContentType, long ByteSize,
        string Sha256Hex, bool IsArchived,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid CreatedBy, Guid UpdatedBy);
}
