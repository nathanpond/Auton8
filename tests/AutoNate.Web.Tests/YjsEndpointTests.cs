using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// Regression coverage for #80: the Yjs collaboration endpoints and both
// shared-secret endpoint filters shipped with zero tests.
//
//   POST /api/yjs/ticket        cookie-authenticated; mints the HMAC ticket
//                               the SPA hands to HocuspocusProvider
//   POST /internal/yjs-auth     YjsInternalSecretEndpointFilter; the *only*
//                               authorization decision on the realtime
//                               editing path
//   POST /internal/yjs-webhook  YjsInternalSecretEndpointFilter + an HMAC
//                               over the raw body; writes the Y.Doc snapshot
//                               back into body_jsonb / content_jsonb
//   POST /api/workflow-behaviors/{key}/execute
//                               SharedSecretEndpointFilter, the sibling
//                               filter guarding the Flowable callback
//
// The filters are the sidecar's authentication boundary, so the tests below
// insist on three things for every rejection: it is a 401, it carries no
// actor identity, and it is byte-identical whether or not the addressed
// resource exists — a rejection that leaks existence turns an unauthenticated
// endpoint into an enumeration oracle.
//
// Mirror state is read back from the database rather than through the content
// API: the webhook's whole job is the mirror write, and reading it directly
// keeps these tests independent of the content endpoints' own authorization.
[Trait("Category", "Integration")]
public sealed class YjsEndpointTests
{
    private const string YjsSecret = "yjs-endpoint-tests-shared-secret";
    private const string WorkflowSecret = "workflow-endpoint-tests-shared-secret";
    private const string WsUrl = "ws://yjs-endpoint-tests:1234";

    private const string PopulatedBody =
        "[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"from the Y.Doc\"}]}]";
    private const string OtherBody =
        "[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"second flush\"}]}]";
    private const string EmptyBlockNoteBody = "[{\"type\":\"paragraph\"}]";

    // ---- /api/yjs/ticket -----------------------------------------------

    [Fact]
    public async Task Ticket_ForPage_ReturnsEditorRoleAndConfiguredWsUrl()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();

        var ticket = await ctx.MintTicketAsync($"page:{seed.PageId}");

        Assert.Equal(WsUrl, ticket.WsUrl);
        Assert.Equal(60, ticket.ExpiresInSeconds);
        Assert.Equal(YjsEndpoints.RoleEditor, ticket.Role);
        // `<base64url(payload)>.<base64url(hmac)>` — two segments, both non-empty.
        var parts = ticket.Ticket.Split('.');
        Assert.Equal(2, parts.Length);
        Assert.All(parts, p => Assert.NotEmpty(p));
    }

    // The doc-name prefix picks the editor the SPA mounts, so a `note:` ticket
    // that resolves to a drawing would boot BlockNote against Excalidraw state.
    [Fact]
    public async Task Ticket_ForNoteWithPrefixThatDoesNotMatchTheKind_Returns400()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();

        var resp = await ctx.PostJsonAsync(
            "/api/yjs/ticket", JsonSerializer.Serialize(new { documentName = $"note:{seed.DrawingNoteId}" }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("richtext", await resp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("no-prefix")]
    [InlineData("page:not-a-guid")]
    [InlineData("chart:00000000-0000-0000-0000-000000000001")]
    public async Task Ticket_ForUnparseableDocumentName_Returns400(string documentName)
    {
        await using var ctx = await TestContext.CreateAsync();
        await ctx.SeedAsync();

        var resp = await ctx.PostJsonAsync(
            "/api/yjs/ticket", JsonSerializer.Serialize(new { documentName }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ---- /internal/yjs-auth: the happy path ----------------------------

    [Fact]
    public async Task YjsAuth_WithFreshTicketAndCorrectSecret_ReturnsActorIdentityAndRole()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();
        var ticket = await ctx.MintTicketAsync($"page:{seed.PageId}");

        var resp = await ctx.PostYjsAuthAsync(ticket.Ticket, $"page:{seed.PageId}", YjsSecret);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var auth = await resp.Content.ReadFromJsonAsync<YjsEndpoints.YjsAuthResponse>();
        Assert.NotNull(auth);
        Assert.Equal(seed.ActorId, auth!.UserId);
        Assert.Equal(YjsEndpoints.RoleEditor, auth.Role);
        Assert.False(string.IsNullOrWhiteSpace(auth.DisplayName));
    }

    // Tickets are single-use: the jti is burned on first presentation so a
    // ticket captured off the wire can't be replayed into a second session.
    [Fact]
    public async Task YjsAuth_WithReplayedTicket_IsUnauthorized()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();
        var ticket = await ctx.MintTicketAsync($"page:{seed.PageId}");

        var first = await ctx.PostYjsAuthAsync(ticket.Ticket, $"page:{seed.PageId}", YjsSecret);
        var replay = await ctx.PostYjsAuthAsync(ticket.Ticket, $"page:{seed.PageId}", YjsSecret);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.DoesNotContain(
            seed.ActorId.ToString(),
            await replay.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    // The ticket binds to one document. Without this check a ticket for a page
    // the actor can read would open an editing session on any other document.
    [Fact]
    public async Task YjsAuth_WithTicketMintedForADifferentDocument_IsUnauthorized()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();
        var ticket = await ctx.MintTicketAsync($"page:{seed.PageId}");

        var resp = await ctx.PostYjsAuthAsync(
            ticket.Ticket, $"page:{seed.OtherPageId}", YjsSecret);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task YjsAuth_WithTamperedTicketSignature_IsUnauthorized()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();
        var ticket = await ctx.MintTicketAsync($"page:{seed.PageId}");

        var resp = await ctx.PostYjsAuthAsync(
            TamperWithSignature(ticket.Ticket), $"page:{seed.PageId}", YjsSecret);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // Ticket TTL is the containment window for a leaked ticket. The factory
    // hands out an already-expired ticket so the expiry branch is exercised
    // without a sleep; the ExpiresInSeconds assertion proves the override
    // actually reached the options rather than the test passing by accident.
    [Fact]
    public async Task YjsAuth_WithExpiredTicket_IsUnauthorized()
    {
        await using var ctx = await TestContext.CreateAsync(
            new Dictionary<string, string?> { ["YjsServer:TicketTtlSeconds"] = "-5" });
        var seed = await ctx.SeedAsync();

        var ticket = await ctx.MintTicketAsync($"page:{seed.PageId}");
        Assert.Equal(-5, ticket.ExpiresInSeconds);

        var resp = await ctx.PostYjsAuthAsync(ticket.Ticket, $"page:{seed.PageId}", YjsSecret);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ---- /internal/yjs-auth: the shared-secret filter -------------------

    // null   → header absent entirely
    // ""     → header present but empty
    // others → wrong value, including a same-length near-miss and a
    //          correct-prefix-plus-suffix, which a prefix comparison or a
    //          length-only check would wave through.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-shared-secret")]
    [InlineData(YjsSecret + "X")]
    [InlineData("Yjs-endpoint-tests-shared-secret")]
    public async Task YjsAuth_WithMissingOrWrongSecret_IsUnauthorizedAndLeaksNoIdentity(string? secret)
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();
        var ticket = await ctx.MintTicketAsync($"page:{seed.PageId}");

        var resp = await ctx.PostYjsAuthAsync(ticket.Ticket, $"page:{seed.PageId}", secret);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(seed.ActorId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(YjsEndpoints.RoleEditor, body, StringComparison.Ordinal);
    }

    // The filter must reject before the handler ever looks a document up, so a
    // caller without the secret cannot use yjs-auth to enumerate page ids.
    [Fact]
    public async Task YjsAuth_WithWrongSecret_AnswersIdenticallyForRealAndUnknownDocuments()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();
        var ticket = await ctx.MintTicketAsync($"page:{seed.PageId}");

        var real = await ctx.PostYjsAuthAsync(
            ticket.Ticket, $"page:{seed.PageId}", "not-the-shared-secret");
        var missing = await ctx.PostYjsAuthAsync(
            ticket.Ticket, $"page:{Guid.NewGuid()}", "not-the-shared-secret");

        Assert.Equal(HttpStatusCode.Unauthorized, real.StatusCode);
        Assert.Equal(missing.StatusCode, real.StatusCode);
        Assert.Equal(
            await missing.Content.ReadAsStringAsync(),
            await real.Content.ReadAsStringAsync());

        // Non-vacuous: the very same request with the right secret succeeds,
        // so the paired 401s above are the filter's doing and nothing else's.
        var authorized = await ctx.PostYjsAuthAsync(
            ticket.Ticket, $"page:{seed.PageId}", YjsSecret);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }

    // ---- /internal/yjs-webhook -----------------------------------------

    [Fact]
    public async Task YjsWebhook_ForPage_MirrorsBodyAndSnapshotsThePriorVersion()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();

        var resp = await ctx.PostWebhookAsync(
            $"page:{seed.PageId}", seed.ActorId, PopulatedBody, YjsSecret, signWith: YjsSecret);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        AssertJsonEquals(PopulatedBody, await ctx.GetPageBodyAsync(seed.PageId));

        // The pre-webhook mirror is preserved as a version so History still
        // shows a restorable entry for the state the session started from.
        var versions = await ctx.GetPageVersionsAsync(seed.PageId);
        var snapshot = Assert.Single(versions);
        Assert.Equal("{}", snapshot.BodyJsonb);
        Assert.Equal(ContentVersionKinds.Autosave, snapshot.Kind);
    }

    [Fact]
    public async Task YjsWebhook_ForRichtextNote_MirrorsContent()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();

        var resp = await ctx.PostWebhookAsync(
            $"note:{seed.RichtextNoteId}", seed.ActorId, PopulatedBody, YjsSecret, signWith: YjsSecret);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        AssertJsonEquals(PopulatedBody, await ctx.GetNoteContentAsync(seed.RichtextNoteId));
    }

    // The doc-prefix/kind cross-check again, on the write path: a `napkin:`
    // document must never be able to overwrite a richtext note's blocks.
    [Fact]
    public async Task YjsWebhook_ForNoteWithPrefixThatDoesNotMatchTheKind_IsRejectedAndKeepsContent()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();

        var resp = await ctx.PostWebhookAsync(
            $"napkin:{seed.RichtextNoteId}", seed.ActorId, PopulatedBody, YjsSecret, signWith: YjsSecret);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("{}", await ctx.GetNoteContentAsync(seed.RichtextNoteId));
    }

    // The cold-load empty-clobber guard: an editor that mounts before the
    // sidecar seeds the Y.Doc autosaves a blank document, which used to wipe
    // bodies written by REST or the chatbot.
    [Fact]
    public async Task YjsWebhook_WithEmptyDocumentOverAPopulatedPage_IsIgnored()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();
        (await ctx.PostWebhookAsync(
            $"page:{seed.PageId}", seed.ActorId, PopulatedBody, YjsSecret, signWith: YjsSecret))
            .EnsureSuccessStatusCode();

        var resp = await ctx.PostWebhookAsync(
            $"page:{seed.PageId}", seed.ActorId, EmptyBlockNoteBody, YjsSecret, signWith: YjsSecret);

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        AssertJsonEquals(PopulatedBody, await ctx.GetPageBodyAsync(seed.PageId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-shared-secret")]
    [InlineData(YjsSecret + "X")]
    public async Task YjsWebhook_WithMissingOrWrongSecret_IsUnauthorizedAndLeavesMirrorUnchanged(
        string? secret)
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();

        // Body signature is valid — only the shared-secret header is wrong, so
        // a failure here can only be the filter's doing.
        var resp = await ctx.PostWebhookAsync(
            $"page:{seed.PageId}", seed.ActorId, PopulatedBody, secret, signWith: YjsSecret);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal("{}", await ctx.GetPageBodyAsync(seed.PageId));
        Assert.Empty(await ctx.GetPageVersionsAsync(seed.PageId));
    }

    // The header and the body HMAC are independent gates: holding the shared
    // secret is not enough to post a body the sidecar didn't sign.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=deadbeef")]
    [InlineData("not-even-prefixed")]
    public async Task YjsWebhook_WithCorrectSecretButBadBodySignature_LeavesMirrorUnchanged(
        string? signature)
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();

        var resp = await ctx.PostWebhookAsync(
            $"page:{seed.PageId}", seed.ActorId, PopulatedBody, YjsSecret,
            signWith: null, rawSignature: signature);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal("{}", await ctx.GetPageBodyAsync(seed.PageId));
    }

    // A signature computed over a *different* body must not carry: otherwise
    // any captured webhook envelope could be replayed with swapped content.
    [Fact]
    public async Task YjsWebhook_WithSignatureForADifferentBody_LeavesMirrorUnchanged()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();
        var signedFor = WebhookBody($"page:{seed.PageId}", seed.ActorId, OtherBody);

        var resp = await ctx.PostWebhookRawAsync(
            WebhookBody($"page:{seed.PageId}", seed.ActorId, PopulatedBody),
            YjsSecret,
            Sign(signedFor, YjsSecret));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal("{}", await ctx.GetPageBodyAsync(seed.PageId));
    }

    [Fact]
    public async Task YjsWebhook_WithWrongSecret_AnswersIdenticallyForRealAndUnknownPages()
    {
        await using var ctx = await TestContext.CreateAsync();
        var seed = await ctx.SeedAsync();

        var real = await ctx.PostWebhookAsync(
            $"page:{seed.PageId}", seed.ActorId, PopulatedBody,
            "not-the-shared-secret", signWith: "not-the-shared-secret");
        var missing = await ctx.PostWebhookAsync(
            $"page:{Guid.NewGuid()}", seed.ActorId, PopulatedBody,
            "not-the-shared-secret", signWith: "not-the-shared-secret");

        Assert.Equal(HttpStatusCode.Unauthorized, real.StatusCode);
        Assert.Equal(missing.StatusCode, real.StatusCode);
        Assert.Equal(
            await missing.Content.ReadAsStringAsync(),
            await real.Content.ReadAsStringAsync());

        // Non-vacuous control: with the right secret the real page is written
        // and the unknown one is a 404, so the two do differ once past the gate.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await ctx.PostWebhookAsync(
                $"page:{seed.PageId}", seed.ActorId, PopulatedBody, YjsSecret, signWith: YjsSecret))
                .StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ctx.PostWebhookAsync(
                $"page:{Guid.NewGuid()}", seed.ActorId, PopulatedBody, YjsSecret, signWith: YjsSecret))
                .StatusCode);
    }

    // ---- SharedSecretEndpointFilter (workflow-behavior callback) --------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-shared-secret")]
    [InlineData(WorkflowSecret + "X")]
    // The Yjs secret must not open the workflow door: the two filters share a
    // header name on purpose so the secrets can rotate independently.
    [InlineData(YjsSecret)]
    public async Task WorkflowBehaviorExecute_WithMissingOrWrongSecret_IsUnauthorized(string? secret)
    {
        await using var ctx = await TestContext.CreateAsync();

        var resp = await ctx.PostBehaviorExecuteAsync("autonate.unlock-account", secret);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task WorkflowBehaviorExecute_WithCorrectSecret_ReachesTheBehavior()
    {
        await using var ctx = await TestContext.CreateAsync();

        var resp = await ctx.PostBehaviorExecuteAsync("autonate.unlock-account", WorkflowSecret);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        // No `userId` process variable was supplied, so the behavior itself
        // answers — proving the request got past the filter into the handler.
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("missingUserId", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkflowBehaviorExecute_WithWrongSecret_AnswersIdenticallyForKnownAndUnknownKeys()
    {
        await using var ctx = await TestContext.CreateAsync();

        var known = await ctx.PostBehaviorExecuteAsync(
            "autonate.unlock-account", "not-the-shared-secret");
        var unknown = await ctx.PostBehaviorExecuteAsync(
            "definitely.not.a.behavior", "not-the-shared-secret");

        Assert.Equal(HttpStatusCode.Unauthorized, known.StatusCode);
        Assert.Equal(unknown.StatusCode, known.StatusCode);
        Assert.Equal(
            await unknown.Content.ReadAsStringAsync(),
            await known.Content.ReadAsStringAsync());

        // With the secret the two keys are clearly distinguishable (200 vs
        // 404) — the filter is what collapses them into one answer.
        Assert.Equal(
            HttpStatusCode.OK,
            (await ctx.PostBehaviorExecuteAsync("autonate.unlock-account", WorkflowSecret)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ctx.PostBehaviorExecuteAsync("definitely.not.a.behavior", WorkflowSecret)).StatusCode);
    }

    // ---- known weakness ------------------------------------------------

    // KNOWN LEAK, asserted as it actually behaves rather than as it should.
    //
    // POST /api/yjs/ticket resolves the note (and, on the `documents:` branch,
    // the document) row BEFORE it calls the authorizer, so it answers 404 for
    // an id that does not exist and 403 for one that does. Any authenticated
    // user with no grants at all can therefore probe note/document ids for
    // existence. The `page:` branch does not have the problem — it authorizes
    // first and returns 403 either way.
    //
    // Fixing it means moving the existence lookups behind the authorization
    // decision (or collapsing both outcomes onto 403). Flip the expectations
    // here when that lands; see the report attached to #80.
    [Fact]
    public async Task Ticket_WithoutAnyGrant_StillDistinguishesExistingNotesFromMissingOnes()
    {
        await using var ctx = await TestContext.CreateAsync(
            new Dictionary<string, string?>
            {
                ["Authorization:Enabled"] = "true",
                ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
                ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
            },
            superAdmin: false);
        var seed = await ctx.SeedAsync();

        var existing = await ctx.PostJsonAsync(
            "/api/yjs/ticket",
            JsonSerializer.Serialize(new { documentName = $"note:{seed.RichtextNoteId}" }));
        var absent = await ctx.PostJsonAsync(
            "/api/yjs/ticket",
            JsonSerializer.Serialize(new { documentName = $"note:{Guid.NewGuid()}" }));

        Assert.Equal(HttpStatusCode.Forbidden, existing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.NotEqual(existing.StatusCode, absent.StatusCode);

        // The page branch is the shape the note branch should have: identical
        // answers whether or not the page is real.
        var realPage = await ctx.PostJsonAsync(
            "/api/yjs/ticket",
            JsonSerializer.Serialize(new { documentName = $"page:{seed.PageId}" }));
        var fakePage = await ctx.PostJsonAsync(
            "/api/yjs/ticket",
            JsonSerializer.Serialize(new { documentName = $"page:{Guid.NewGuid()}" }));
        Assert.Equal(HttpStatusCode.Forbidden, realPage.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, fakePage.StatusCode);
    }

    // ---- helpers -------------------------------------------------------

    private static string Sign(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string WebhookBody(string documentName, Guid userId, string bodyJsonb) =>
        JsonSerializer.Serialize(new
        {
            @event = "change",
            documentName,
            userId = userId.ToString(),
            bodyJsonb
        });

    // Tamper with the signature by flipping a bit in the decoded bytes, not by
    // editing a base64 character.
    //
    // The original version replaced the ticket's last character ('A' <-> 'B'),
    // which is not reliably a tamper at all: a 32-byte HMAC is 43 base64url
    // characters, so the final character carries four significant bits and two
    // padding bits. 'A' is 000000 and 'B' is 000001 — they differ only in the
    // padding — so whenever a ticket happened to end in 'A', the "tampered"
    // value decoded to identical bytes, the signature verified, and this
    // security assertion passed a request it was written to reject. Roughly
    // one run in sixty-four; it went green locally for weeks and failed the
    // first time CI drew an unlucky ticket.
    private static string TamperWithSignature(string ticket)
    {
        var parts = ticket.Split('.');
        Assert.Equal(2, parts.Length);

        var sig = Base64UrlDecode(parts[1]);
        sig[0] ^= 0xFF;
        return parts[0] + "." + Base64UrlEncode(sig);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record Seeded(
        Guid ActorId, Guid PageId, Guid OtherPageId, Guid RichtextNoteId, Guid DrawingNoteId);

    private sealed class TestContext : IAsyncDisposable
    {
        private TestContext(AutoNateWebApplicationFactory factory, HttpClient client)
        {
            Factory = factory;
            Client = client;
        }

        public AutoNateWebApplicationFactory Factory { get; }
        public HttpClient Client { get; }

        public static async Task<TestContext> CreateAsync(
            IReadOnlyDictionary<string, string?>? extraConfig = null,
            bool superAdmin = true)
        {
            var settings = new Dictionary<string, string?>
            {
                // Pin both secrets so a test can present a known-correct value
                // and known-wrong ones; without this the tests would inherit
                // the dev fallbacks from appsettings.Development.json.
                ["YjsServer:InternalSharedSecret"] = YjsSecret,
                ["YjsServer:HocuspocusWsUrl"] = WsUrl,
                ["YjsServer:TicketTtlSeconds"] = "60",
                ["WorkflowBehaviors:CallbackSharedSecret"] = WorkflowSecret
            };
            if (superAdmin)
            {
                // /api/yjs/ticket authorizes through IContentAuthorizer, which
                // enforces regardless of Authorization:Enabled.
                settings["Authorization:AssignSuperAdminToAllExistingUsers"] = "true";
            }
            if (extraConfig is not null)
            {
                foreach (var (key, value) in extraConfig) settings[key] = value;
            }

            var factory = await AutoNateWebApplicationFactory.CreateAsync(settings);
            var client = factory.CreateClient();
            // Dev auto-login skips POSTs — land the auth cookie with a GET first.
            (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
            return new TestContext(factory, client);
        }

        // Project → cabinet → notebook → two pages (in the content-ancestor
        // closure the authorizer walks) with a richtext and a drawing note on
        // the first page. Bodies start at the "{}" default-row sentinel so the
        // webhook's empty-clobber guard doesn't reject the first real write.
        public async Task<Seeded> SeedAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var dbFactory = sp.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            var tree = sp.GetRequiredService<IContentTreeService>();

            Guid actorId;
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                actorId = await db.LocalUsers.AsNoTracking()
                    .Where(u => u.Username == "admin")
                    .Select(u => u.UserId)
                    .FirstAsync();
            }

            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(), Name = "yjs-endpoint-tests",
                DeletionsLocked = false, IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
            };
            var cabinet = new Cabinet
            {
                Id = Guid.NewGuid(), ProjectId = project.Id, Name = "cab",
                IsArchived = false, SortOrder = 0,
                CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
            };
            var notebook = new Notebook
            {
                Id = Guid.NewGuid(), CabinetId = cabinet.Id, Name = "nb",
                IsArchived = false, SortOrder = 0,
                CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
            };
            var page = NewPage(notebook.Id, "p1", actorId, now);
            var otherPage = NewPage(notebook.Id, "p2", actorId, now);
            var richtext = NewNote(page.Id, "richtext", 1, actorId, now);
            var drawing = NewNote(page.Id, "drawing", 2, actorId, now);

            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                db.Projects.Add(project);
                db.Cabinets.Add(cabinet);
                db.Notebooks.Add(notebook);
                db.Pages.Add(page);
                db.Pages.Add(otherPage);
                db.Notes.Add(richtext);
                db.Notes.Add(drawing);
                await db.SaveChangesAsync();
            }

            foreach (var (kind, id) in new[]
            {
                (ContentKinds.Project, project.Id),
                (ContentKinds.Cabinet, cabinet.Id),
                (ContentKinds.Notebook, notebook.Id),
                (ContentKinds.Page, page.Id),
                (ContentKinds.Page, otherPage.Id)
            })
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                await tree.InsertSelfWithAncestorsAsync(db, kind, id, default);
            }

            return new Seeded(actorId, page.Id, otherPage.Id, richtext.Id, drawing.Id);
        }

        private static Page NewPage(Guid notebookId, string title, Guid actorId, DateTime now) => new()
        {
            Id = Guid.NewGuid(), NotebookId = notebookId, ParentPageId = null,
            Title = title, BodyJsonb = "{}", CurrentVersionNumber = 1, SortOrder = 0,
            IsArchived = false,
            CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
        };

        private static Note NewNote(Guid pageId, string kind, int index, Guid actorId, DateTime now) => new()
        {
            Id = Guid.NewGuid(), PageId = pageId, PageNoteIndex = index,
            NoteKind = kind, Title = kind, ContentJsonb = "{}",
            CurrentVersionNumber = 1, SortOrder = 0,
            CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
        };

        public Task<HttpResponseMessage> PostJsonAsync(string url, string json) =>
            SendAsync(url, json, headers: null);

        public async Task<YjsEndpoints.TicketResponse> MintTicketAsync(string documentName)
        {
            var resp = await PostJsonAsync(
                "/api/yjs/ticket", JsonSerializer.Serialize(new { documentName }));
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<YjsEndpoints.TicketResponse>())!;
        }

        public Task<HttpResponseMessage> PostYjsAuthAsync(
            string token, string documentName, string? secret)
        {
            var headers = new List<(string, string)>();
            if (secret is not null)
            {
                headers.Add((YjsInternalSecretEndpointFilter.HeaderName, secret));
            }
            return SendAsync(
                "/internal/yjs-auth",
                JsonSerializer.Serialize(new { token, documentName }),
                headers);
        }

        // `signWith` computes a valid HMAC over the body with that secret;
        // `rawSignature` sets the header verbatim (including omitting it when
        // both are null) so the signature gate can be probed on its own.
        public Task<HttpResponseMessage> PostWebhookAsync(
            string documentName, Guid userId, string bodyJsonb, string? secret,
            string? signWith, string? rawSignature = null)
        {
            var body = WebhookBody(documentName, userId, bodyJsonb);
            var signature = signWith is null ? rawSignature : Sign(body, signWith);
            return PostWebhookRawAsync(body, secret, signature);
        }

        public Task<HttpResponseMessage> PostWebhookRawAsync(
            string body, string? secret, string? signature)
        {
            var headers = new List<(string, string)>();
            if (secret is not null)
            {
                headers.Add((YjsInternalSecretEndpointFilter.HeaderName, secret));
            }
            if (signature is not null)
            {
                headers.Add(("X-AutoNate-Yjs-Signature", signature));
            }
            return SendAsync("/internal/yjs-webhook", body, headers);
        }

        public Task<HttpResponseMessage> PostBehaviorExecuteAsync(string key, string? secret)
        {
            var headers = new List<(string, string)>();
            if (secret is not null)
            {
                headers.Add((SharedSecretEndpointFilter.HeaderName, secret));
            }
            var body = JsonSerializer.Serialize(new
            {
                processInstanceId = "pi-1",
                executionId = "ex-1",
                processDefinitionKey = "k-1",
                processName = "Test process",
                activityId = "task-1",
                businessKey = (string?)null,
                correlationId = "corr-1",
                variables = new Dictionary<string, object>()
            });
            return SendAsync($"/api/workflow-behaviors/{key}/execute", body, headers);
        }

        private Task<HttpResponseMessage> SendAsync(
            string url, string json, IReadOnlyList<(string Name, string Value)>? headers)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (headers is not null)
            {
                foreach (var (name, value) in headers)
                {
                    request.Headers.TryAddWithoutValidation(name, value);
                }
            }
            return Client.SendAsync(request);
        }

        public async Task<string> GetPageBodyAsync(Guid pageId)
        {
            using var scope = Factory.Services.CreateScope();
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            return await db.Pages.AsNoTracking()
                .Where(p => p.Id == pageId).Select(p => p.BodyJsonb).FirstAsync();
        }

        public async Task<string> GetNoteContentAsync(Guid noteId)
        {
            using var scope = Factory.Services.CreateScope();
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            return await db.Notes.AsNoTracking()
                .Where(n => n.Id == noteId).Select(n => n.ContentJsonb).FirstAsync();
        }

        public async Task<List<PageVersion>> GetPageVersionsAsync(Guid pageId)
        {
            using var scope = Factory.Services.CreateScope();
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            return await db.PageVersions.AsNoTracking()
                .Where(v => v.PageId == pageId)
                .OrderBy(v => v.VersionNumber)
                .ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }

    // Postgres reserializes jsonb: whitespace is normalized and object keys
    // come back in its own order, so a raw string compare would be testing the
    // formatter rather than whether the mirror write landed.
    private static void AssertJsonEquals(string expected, string? actual)
    {
        Assert.NotNull(actual);
        using var e = JsonDocument.Parse(expected);
        using var a = JsonDocument.Parse(actual!);
        Assert.Equal(Canonicalize(e.RootElement), Canonicalize(a.RootElement));
    }

    private static string Canonicalize(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", element.EnumerateObject()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{JsonSerializer.Serialize(p.Name)}:{Canonicalize(p.Value)}")) + "}",
        JsonValueKind.Array => "[" + string.Join(",",
            element.EnumerateArray().Select(Canonicalize)) + "]",
        _ => element.GetRawText()
    };

}
