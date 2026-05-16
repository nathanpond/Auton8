using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Yjs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Endpoints;

// Yjs / Hocuspocus integration endpoints.
//
// Three routes:
//   POST /api/yjs/ticket
//     Browser-facing. Cookie-authenticated. Authorizes the actor against
//     the requested document and mints a short-lived single-use HMAC
//     ticket. The SPA hands this to HocuspocusProvider as the `token`.
//
//   POST /internal/yjs-auth
//     Hocuspocus-facing. Shared-secret gated. Hocuspocus's onAuthenticate
//     hook calls this for every WS connection; we re-verify the ticket,
//     consume its jti so it can't be replayed, re-run the authorizer
//     (in case the user's permissions changed in the 60s window), and
//     return the user identity + display name back to Hocuspocus.
//
//   POST /internal/yjs-webhook
//     Hocuspocus-facing. Shared-secret gated AND HMAC-signed body.
//     The sidecar's onStoreDocument hook materializes the Y.Doc into
//     BlockNote-shape JSON and POSTs it here. We write the snapshot
//     mirror into `body_jsonb` / `content_jsonb`, fold a version row in
//     via ContentVersionService (session-rollup keeps autosave churn
//     out of the history), and emit the same audit event the REST PATCH
//     used to emit. HistoryModal keeps working unchanged.
public static class YjsEndpoints
{
    public static IEndpointRouteBuilder MapYjsEndpoints(this IEndpointRouteBuilder app)
    {
        // -- /api/yjs/ticket -------------------------------------------------
        var browserGroup = app.MapGroup("/api/yjs").RequireAuthorization();

        browserGroup.MapPost("/ticket", async (
            TicketRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IOptions<YjsServerOptions> options,
            CancellationToken ct) =>
        {
            if (!TryParseDocumentName(request.DocumentName, out var kind, out var entityId))
                return Results.BadRequest(new { error = "Unrecognized document name." });

            var secret = options.Value.InternalSharedSecret;
            if (string.IsNullOrEmpty(secret))
                return Results.Problem("Yjs server is not configured.", statusCode: 500);

            await using var db = await dbFactory.CreateDbContextAsync(ct);

            // Translate the document target into a Page-scoped authorization
            // check. Notes inherit page permissions (design D10), so for
            // note:/napkin:/diagram: docs we look up the parent pageId and
            // authorize on that. pagemeta: addresses the page directly, just
            // a sibling channel to page: for the live notes-list metadata.
            Guid pageId;
            if (kind == DocKinds.Page || kind == DocKinds.PageMeta)
            {
                pageId = entityId;
            }
            else if (IsNoteDocKind(kind))
            {
                var note = await db.Notes.AsNoTracking()
                    .Where(n => n.Id == entityId)
                    .Select(n => new { n.PageId, n.NoteKind })
                    .FirstOrDefaultAsync(ct);
                if (note is null) return Results.NotFound();
                var expected = ExpectedNoteKindForDocKind(kind);
                if (!string.Equals(note.NoteKind, expected, StringComparison.Ordinal))
                    return Results.BadRequest(new
                    {
                        error = $"Document prefix '{kind}' requires note kind '{expected}', but note is '{note.NoteKind}'."
                    });
                pageId = note.PageId;
            }
            else
            {
                return Results.BadRequest(new { error = "Unsupported document kind." });
            }

            var viewDecision = await authorizer.AuthorizeAsync(
                http.User, ContentKinds.Page, pageId, Actions.View, ct);
            if (!viewDecision.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

            // Edit determines role. Users with View but no Edit get a
            // ticket with role=viewer; Hocuspocus's auth hook flips the
            // connection to readOnly. Server-side enforcement is the
            // security boundary — the role in the response is just a
            // UX hint so the SPA can render read-only chrome up front.
            var editDecision = await authorizer.AuthorizeAsync(
                http.User, ContentKinds.Page, pageId, Actions.Edit, ct);
            var role = editDecision.IsAllowed ? RoleEditor : RoleViewer;

            var actorId = http.GetActorId();
            var displayName = http.User.Identity?.Name ?? actorId.ToString();
            var ttl = options.Value.TicketTtlSeconds;
            var ticket = MintTicket(request.DocumentName, actorId, displayName, role, ttl, secret);

            return Results.Ok(new TicketResponse(ticket, options.Value.HocuspocusWsUrl, ttl, role));
        }).DisableAntiforgery();

        // -- /internal/yjs-* -------------------------------------------------
        var internalGroup = app.MapGroup("/internal")
            .AllowAnonymous();

        internalGroup.MapPost("/yjs-auth", async (
            YjsAuthRequest request,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IOptions<YjsServerOptions> options,
            IMemoryCache jtiCache,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("YjsAuth");
            var secret = options.Value.InternalSharedSecret;
            if (string.IsNullOrEmpty(secret)) return Results.Unauthorized();
            if (!TryVerifyTicket(request.Token, secret, out var payload))
            {
                log.LogWarning("Rejected Yjs ticket: invalid signature or expired.");
                return Results.Unauthorized();
            }

            if (!string.Equals(payload.DocumentName, request.DocumentName, StringComparison.Ordinal))
            {
                log.LogWarning(
                    "Rejected Yjs ticket: documentName mismatch. ticket={Ticket}, request={Request}.",
                    payload.DocumentName, request.DocumentName);
                return Results.Unauthorized();
            }

            // jti single-use enforcement. TTL set slightly above ticket TTL
            // so the entry survives a few seconds past expiry in case a
            // racing reuse arrives at the very end of the window.
            var jtiKey = $"yjs-jti:{payload.Jti}";
            if (jtiCache.TryGetValue(jtiKey, out _))
            {
                log.LogWarning("Rejected Yjs ticket: jti {Jti} already consumed.", payload.Jti);
                return Results.Unauthorized();
            }
            jtiCache.Set(jtiKey, true, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow =
                    TimeSpan.FromSeconds(options.Value.TicketTtlSeconds + 60)
            });

            // Re-run the authorizer. A user could have lost access in the
            // 60-second window between ticket mint and Hocuspocus
            // connecting. The ticket carries the documentName, not a
            // permission grant.
            if (!TryParseDocumentName(payload.DocumentName, out var kind, out var entityId))
                return Results.Unauthorized();

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            Guid pageId;
            if (kind == DocKinds.Page || kind == DocKinds.PageMeta) pageId = entityId;
            else if (IsNoteDocKind(kind))
            {
                var noteRow = await db.Notes.AsNoTracking()
                    .Where(n => n.Id == entityId)
                    .Select(n => new { n.PageId, n.NoteKind })
                    .FirstOrDefaultAsync(ct);
                if (noteRow is null) return Results.Unauthorized();
                // Re-verify the prefix vs note-kind match in case the
                // ticket was minted before a hypothetical kind mutation.
                var expected = ExpectedNoteKindForDocKind(kind);
                if (!string.Equals(noteRow.NoteKind, expected, StringComparison.Ordinal))
                    return Results.Unauthorized();
                pageId = noteRow.PageId;
            }
            else return Results.Unauthorized();

            // The yjs-auth caller is Hocuspocus (no HttpContext.User from
            // a cookie). Authorize against a synthetic principal carrying
            // just the ticket's NameIdentifier — enough for the
            // ContentAuthorizer's actor-id lookups.
            var principal = SyntheticPrincipal.FromUserId(payload.UserId);
            var viewDecision = await authorizer.AuthorizeAsync(
                principal, ContentKinds.Page, pageId, Actions.View, ct);
            if (!viewDecision.IsAllowed)
            {
                log.LogWarning(
                    "Rejected Yjs ticket: user {UserId} no longer has access to {Document}.",
                    payload.UserId, payload.DocumentName);
                return Results.Unauthorized();
            }

            // Re-evaluate Edit. The ticket payload's `Role` is a hint —
            // we trust the live authorizer in case the user was demoted
            // (or promoted) between mint and connect.
            var editDecision = await authorizer.AuthorizeAsync(
                principal, ContentKinds.Page, pageId, Actions.Edit, ct);
            var role = editDecision.IsAllowed ? RoleEditor : RoleViewer;

            return Results.Ok(new YjsAuthResponse(payload.UserId, payload.DisplayName, role));
        })
        .DisableAntiforgery()
        .AddEndpointFilter<YjsInternalSecretEndpointFilter>();

        internalGroup.MapPost("/yjs-webhook", async (
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentVersionService versions,
            IAuditEventPublisher auditPublisher,
            IOptions<YjsServerOptions> options,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("YjsWebhook");
            var secret = options.Value.InternalSharedSecret;
            if (string.IsNullOrEmpty(secret)) return Results.Unauthorized();

            // Read raw body for HMAC verification; deserialize after.
            http.Request.EnableBuffering();
            string body;
            using (var reader = new StreamReader(
                http.Request.Body, Encoding.UTF8, leaveOpen: true))
            {
                body = await reader.ReadToEndAsync(ct);
                http.Request.Body.Position = 0;
            }

            var signature = http.Request.Headers["X-AutoNate-Yjs-Signature"].ToString();
            if (!VerifyBodySignature(body, signature, secret))
            {
                log.LogWarning("Rejected Yjs webhook: HMAC signature mismatch.");
                return Results.Unauthorized();
            }

            YjsWebhookPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<YjsWebhookPayload>(body, WebhookJsonOpts);
            }
            catch (JsonException ex)
            {
                log.LogWarning(ex, "Rejected Yjs webhook: malformed JSON.");
                return Results.BadRequest();
            }

            if (payload is null || string.IsNullOrEmpty(payload.DocumentName)
                || string.IsNullOrEmpty(payload.Event))
                return Results.BadRequest();

            // Phase 1 only handles "change". Disconnect events are
            // forwarded by the sidecar for future awareness/presence work.
            if (!string.Equals(payload.Event, "change", StringComparison.Ordinal))
                return Results.NoContent();

            if (!TryParseDocumentName(payload.DocumentName, out var kind, out var entityId))
            {
                log.LogWarning(
                    "Rejected Yjs webhook: unrecognized document name {Name}.",
                    payload.DocumentName);
                return Results.BadRequest();
            }

            if (string.IsNullOrEmpty(payload.BodyJsonb))
            {
                log.LogWarning(
                    "Rejected Yjs webhook: missing bodyJsonb for {Name}.",
                    payload.DocumentName);
                return Results.BadRequest();
            }

            var actorId = Guid.TryParse(payload.UserId, out var parsed) ? parsed : Guid.Empty;
            var now = DateTime.UtcNow;

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            int? newVersionNumber;

            if (kind == DocKinds.Page)
            {
                var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == entityId, ct);
                if (page is null) return Results.NotFound();

                // Skip the no-op case: if the snapshot already matches, the
                // webhook is just a duplicate from the debounce flush.
                if (page.BodyJsonb == payload.BodyJsonb)
                {
                    await tx.CommitAsync(ct);
                    return Results.NoContent();
                }

                newVersionNumber = await versions.SnapshotPageBeforeChangeAsync(
                    db, page.Id, page.Title, page.BodyJsonb,
                    ContentVersionKinds.Autosave, null, actorId, now, ct);
                page.BodyJsonb = payload.BodyJsonb;
                page.UpdatedAtUtc = now;
                page.UpdatedBy = actorId;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                if (newVersionNumber is { } vn)
                {
                    await auditPublisher.PublishAsync(
                        ContentEventTopic.TopicName,
                        ContentEventTypes.PageVersionCreated,
                        ContentResourceKinds.PageVersion,
                        resource: new
                        {
                            pageId = page.Id,
                            versionNumber = vn - 1,
                            kind = ContentVersionKinds.Autosave
                        },
                        details: null,
                        ct);
                }
                await auditPublisher.PublishAsync(
                    ContentEventTopic.TopicName,
                    ContentEventTypes.PageUpdated,
                    ContentResourceKinds.Page,
                    resource: new { id = page.Id, title = page.Title },
                    details: new { fields = PageBodyFields, newVersionNumber, source = "yjs" },
                    ct);
            }
            else if (IsNoteDocKind(kind))
            {
                var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == entityId, ct);
                if (note is null) return Results.NotFound();
                if (!YjsManagedContentGuard.IsYjsManagedNoteKind(note.NoteKind))
                {
                    log.LogWarning(
                        "Rejected Yjs webhook for note {NoteId}: kind is {Kind}, not Yjs-managed.",
                        note.Id, note.NoteKind);
                    return Results.BadRequest();
                }
                var expectedKind = ExpectedNoteKindForDocKind(kind);
                if (!string.Equals(note.NoteKind, expectedKind, StringComparison.Ordinal))
                {
                    log.LogWarning(
                        "Rejected Yjs webhook for note {NoteId}: doc prefix '{Prefix}' requires kind '{Expected}' but note is '{Actual}'.",
                        note.Id, kind, expectedKind, note.NoteKind);
                    return Results.BadRequest();
                }
                if (note.ContentJsonb == payload.BodyJsonb)
                {
                    await tx.CommitAsync(ct);
                    return Results.NoContent();
                }

                newVersionNumber = await versions.SnapshotNoteBeforeChangeAsync(
                    db, note.Id, note.Title, note.NoteKind, note.ContentJsonb,
                    ContentVersionKinds.Autosave, null, actorId, now, ct);
                note.ContentJsonb = payload.BodyJsonb;
                note.UpdatedAtUtc = now;
                note.UpdatedBy = actorId;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                if (newVersionNumber is { } vn)
                {
                    await auditPublisher.PublishAsync(
                        ContentEventTopic.TopicName,
                        ContentEventTypes.NoteVersionCreated,
                        ContentResourceKinds.NoteVersion,
                        resource: new
                        {
                            noteId = note.Id,
                            versionNumber = vn - 1,
                            kind = ContentVersionKinds.Autosave
                        },
                        details: null,
                        ct);
                }
                await auditPublisher.PublishAsync(
                    ContentEventTopic.TopicName,
                    ContentEventTypes.NoteUpdated,
                    ContentResourceKinds.Note,
                    resource: new { id = note.Id },
                    details: new { fields = NoteContentFields, newVersionNumber, source = "yjs" },
                    ct);
            }
            else
            {
                return Results.BadRequest();
            }

            return Results.NoContent();
        })
        .DisableAntiforgery()
        .AddEndpointFilter<YjsInternalSecretEndpointFilter>();

        return app;
    }

    // -- ticket format ---------------------------------------------------

    // Compact custom ticket: `<base64url(payload)>.<base64url(hmac)>`.
    // Smaller than a JWT (no header section), same security properties
    // (HMAC-SHA256 over the payload, server-side single-use enforcement
    // via jti cache).
    private static string MintTicket(
        string documentName, Guid userId, string displayName, string role,
        int ttlSeconds, string secret)
    {
        var payload = new TicketPayload(
            documentName,
            userId,
            displayName,
            role,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds,
            Guid.NewGuid().ToString("N"));
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload, TicketJsonOpts);
        var payloadB64 = Base64UrlEncode(payloadJson);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
        var sigB64 = Base64UrlEncode(sigBytes);
        return $"{payloadB64}.{sigB64}";
    }

    private static bool TryVerifyTicket(string? ticket, string secret, out TicketPayload payload)
    {
        payload = default!;
        if (string.IsNullOrEmpty(ticket)) return false;
        var parts = ticket.Split('.');
        if (parts.Length != 2) return false;
        byte[] payloadBytes;
        byte[] sigBytes;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            sigBytes = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[0]));
        if (!CryptographicOperations.FixedTimeEquals(expected, sigBytes)) return false;

        TicketPayload? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<TicketPayload>(payloadBytes, TicketJsonOpts);
        }
        catch (JsonException)
        {
            return false;
        }
        if (parsed is null) return false;
        if (parsed.Exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
        payload = parsed;
        return true;
    }

    private static bool VerifyBodySignature(string body, string signatureHeader, string secret)
    {
        if (string.IsNullOrEmpty(signatureHeader)) return false;
        const string prefix = "sha256=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var hex = signatureHeader.AsSpan(prefix.Length);
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return false;
        }
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        var s = Convert.ToBase64String(bytes);
        return s.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }

    // -- document-name routing -------------------------------------------

    private static class DocKinds
    {
        public const string Page = "page";
        public const string PageMeta = "pagemeta"; // → page's notes-list Y.Doc (live tab strip)
        public const string Note = "note";         // → richtext note (BlockNote)
        public const string Napkin = "napkin";     // → drawing note (Excalidraw)
        public const string Diagram = "diagram";   // → diagram note (draw.io)
    }

    // Maps a doc-name prefix to the NoteKind it's allowed to address.
    // Returns null for non-note prefixes (i.e. `page`).
    private static string? ExpectedNoteKindForDocKind(string kind) => kind switch
    {
        DocKinds.Note => "richtext",
        DocKinds.Napkin => "drawing",
        DocKinds.Diagram => "diagram",
        _ => null
    };

    private static bool IsNoteDocKind(string kind) =>
        kind == DocKinds.Note || kind == DocKinds.Napkin || kind == DocKinds.Diagram;

    private static bool TryParseDocumentName(string? raw, out string kind, out Guid id)
    {
        kind = string.Empty;
        id = Guid.Empty;
        if (string.IsNullOrEmpty(raw)) return false;
        var sep = raw.IndexOf(':');
        if (sep <= 0 || sep == raw.Length - 1) return false;
        var k = raw[..sep];
        if (k != DocKinds.Page && k != DocKinds.PageMeta && !IsNoteDocKindRaw(k)) return false;
        if (!Guid.TryParse(raw.AsSpan(sep + 1), out var g)) return false;
        kind = k;
        id = g;
        return true;
    }

    private static bool IsNoteDocKindRaw(string k) =>
        k == DocKinds.Note || k == DocKinds.Napkin || k == DocKinds.Diagram;

    // -- DTOs + helpers --------------------------------------------------

    // Connection roles. "editor" = full read/write Y.Doc; "viewer" =
    // read-only (Hocuspocus's `connection.readOnly = true`). Surface as
    // public consts so other endpoint code can match values exactly.
    public const string RoleEditor = "editor";
    public const string RoleViewer = "viewer";

    public sealed record TicketRequest(string DocumentName);
    public sealed record TicketResponse(string Ticket, string WsUrl, int ExpiresInSeconds, string Role);
    public sealed record YjsAuthRequest(string Token, string DocumentName);
    public sealed record YjsAuthResponse(Guid UserId, string DisplayName, string Role);

    private sealed record TicketPayload(
        string DocumentName,
        Guid UserId,
        string DisplayName,
        // "editor" or "viewer". yjs-auth re-checks Edit and may downgrade
        // even if the ticket said editor (covers permission demotion
        // between mint and connect).
        string Role,
        long Exp,
        string Jti);

    private sealed record YjsWebhookPayload
    {
        public string Event { get; init; } = string.Empty;
        public string DocumentName { get; init; } = string.Empty;
        public string? UserId { get; init; }
        public string? BodyJsonb { get; init; }
    }

    private static readonly JsonSerializerOptions TicketJsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions WebhookJsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly string[] PageBodyFields = { "bodyJsonb" };
    private static readonly string[] NoteContentFields = { "contentJsonb" };

    // Synthetic ClaimsPrincipal so the yjs-auth callback (no cookie session)
    // can re-run the ContentAuthorizer. Only the NameIdentifier claim is
    // populated — that's all the authorizer needs to look up the user's
    // group/role memberships.
    private static class SyntheticPrincipal
    {
        public static System.Security.Claims.ClaimsPrincipal FromUserId(Guid userId)
        {
            var identity = new System.Security.Claims.ClaimsIdentity("yjs-internal");
            identity.AddClaim(new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.NameIdentifier,
                userId.ToString()));
            return new System.Security.Claims.ClaimsPrincipal(identity);
        }
    }
}
