using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Content.Bindings;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Records.Fields;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BindingDto = AutoNate.Web.Endpoints.ContentDocumentBindingEndpoints.DocumentBindingDto;
using BindingListResponse = AutoNate.Web.Endpoints.ContentDocumentBindingEndpoints.DocumentBindingListResponse;
using RefreshAllResponse = AutoNate.Web.Endpoints.ContentDocumentBindingEndpoints.RefreshAllResponse;
using DocCommentDto = AutoNate.Web.Endpoints.ContentDocumentCommentEndpoints.DocumentCommentDto;
using DocCommentListResponse = AutoNate.Web.Endpoints.ContentDocumentCommentEndpoints.DocumentCommentListResponse;

namespace AutoNate.Web.Tests;

// #90: ContentDocumentBindingEndpoints + ContentDocumentCommentEndpoints had
// only authorizer unit tests — nothing exercised the routes themselves, so
// nothing would have caught a wrong (EntityKind, Action) pair on a filter or a
// resolver that ran under the wrong principal.
//
// Two harness flavours, because the two halves need opposite postures:
//   • Harness.CreateAsync()      — seeded admin is SuperAdmin, so
//     ContentAuthorizer short-circuits and the handler bodies are reachable
//     (including the `documentExists` 404 branch, which is otherwise masked by
//     the route filter denying on "no project ancestor for resource").
//   • Harness.CreateGatedAsync() — no SuperAdmin and Authorization:Enforcement
//     = full, so every call is decided by the explicit grants the test adds.
//     This is the flavour that proves the gates.
//
// Note ContentAuthorizer does NOT consult Authorization:Enabled — content kinds
// are always enforced — which is why the gated harness seeds its fixtures
// through the service layer rather than over HTTP.
[Trait("Category", "Integration")]
public sealed class ContentDocumentBindingAndCommentEndpointTests
{
    // ---------------------------------------------------------------
    // Bindings — round trip
    // ---------------------------------------------------------------

    [Fact]
    public async Task CreateBinding_RecordFieldKind_ReturnsResolvedSnapshotAndSuggestedLabel()
    {
        await using var h = await Harness.CreateAsync();
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));

        var response = await h.Client.PostAsJsonAsync(
            h.BindingsUrl(),
            new { kind = DocumentBindingKinds.RecordField, configJsonb = RecordFieldConfig(recordId, "title"), label = (string?)null });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<BindingDto>();
        Assert.NotNull(created);
        Assert.Equal(h.DocumentId, created!.DocumentId);
        Assert.Equal(DocumentBindingKinds.RecordField, created.Kind);
        Assert.Equal(RecordFieldConfig(recordId, "title"), created.ConfigJsonb);
        // Resolve-on-create is the whole point of the create handler doing more
        // than an INSERT: the caller sees the value it is about to embed.
        Assert.Equal("Acme", ResolvedText(created));
        Assert.Equal("text", ResolvedType(created));
        Assert.NotNull(created.LastResolvedAtUtc);
        // Whose permissions produced the snapshot — the column the audit story
        // for bindings rests on.
        Assert.Equal(Harness.AdminUserId, created.LastResolvedByUserId);
        // No label supplied, so the resolver's suggestion is stamped instead.
        Assert.Equal("Invoice.title", created.Label);
    }

    [Fact]
    public async Task ListBindings_AfterCreate_ReturnsThePersistedBinding()
    {
        await using var h = await Harness.CreateAsync();
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));
        var created = await h.CreateBindingAsync(recordId, "title");

        var listed = await h.Client.GetFromJsonAsync<BindingListResponse>(h.BindingsUrl());

        Assert.NotNull(listed);
        var only = Assert.Single(listed!.Items);
        Assert.Equal(created.Id, only.Id);
        Assert.Equal("Acme", ResolvedText(only));
        Assert.Equal("Invoice.title", only.Label);
    }

    [Fact]
    public async Task PatchBinding_ChangingConfig_ReResolvesAgainstTheNewField()
    {
        await using var h = await Harness.CreateAsync();
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"), ("subtitle", "Beta"));
        var created = await h.CreateBindingAsync(recordId, "title");

        var response = await h.Client.PatchAsJsonAsync(
            h.BindingUrl(created.Id),
            new { configJsonb = RecordFieldConfig(recordId, "subtitle"), label = (string?)null });

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<BindingDto>();
        Assert.NotNull(updated);
        Assert.Equal(RecordFieldConfig(recordId, "subtitle"), updated!.ConfigJsonb);
        // A config edit that saved without re-resolving would leave the old
        // value rendering under the new config — the bug this asserts against.
        Assert.Equal("Beta", ResolvedText(updated));

        var listed = await h.Client.GetFromJsonAsync<BindingListResponse>(h.BindingsUrl());
        Assert.Equal("Beta", ResolvedText(Assert.Single(listed!.Items)));
    }

    [Fact]
    public async Task PatchBinding_LabelOnly_LeavesConfigAndSnapshotUntouched()
    {
        await using var h = await Harness.CreateAsync();
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));
        await h.CreateBindingAsync(recordId, "title");
        // Read back through the API so the comparison baseline has already been
        // through Postgres' timestamp precision — comparing against the create
        // response's in-memory DateTime would be flaky on the sub-microsecond
        // digits alone.
        var before = Assert.Single(
            (await h.Client.GetFromJsonAsync<BindingListResponse>(h.BindingsUrl()))!.Items);

        var response = await h.Client.PatchAsJsonAsync(
            h.BindingUrl(before.Id),
            new { configJsonb = (string?)null, label = "  Renamed  " });

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<BindingDto>();
        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated!.Label); // trimmed
        Assert.Equal(before.ConfigJsonb, updated.ConfigJsonb);
        Assert.Equal(before.LastResolvedValueJsonb, updated.LastResolvedValueJsonb);
        // A label rename must not look like a refresh: the SPA's in-document
        // sync keys off LastResolvedAtUtc to repaint the node.
        Assert.Equal(before.LastResolvedAtUtc, updated.LastResolvedAtUtc);
    }

    [Fact]
    public async Task PatchBinding_EmptyLabel_ClearsLabelToNull()
    {
        await using var h = await Harness.CreateAsync();
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));
        var created = await h.CreateBindingAsync(recordId, "title", label: "Mine");
        Assert.Equal("Mine", created.Label);

        var response = await h.Client.PatchAsJsonAsync(
            h.BindingUrl(created.Id),
            new { configJsonb = (string?)null, label = "" });

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<BindingDto>();
        // Documented three-state contract on UpdateDocumentBindingRequest.Label:
        // null = leave alone, "" = clear, text = set.
        Assert.Null(updated!.Label);
        var listed = await h.Client.GetFromJsonAsync<BindingListResponse>(h.BindingsUrl());
        Assert.Null(Assert.Single(listed!.Items).Label);
    }

    [Fact]
    public async Task DeleteBinding_RemovesItFromTheList()
    {
        await using var h = await Harness.CreateAsync();
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));
        var created = await h.CreateBindingAsync(recordId, "title");

        var response = await h.Client.DeleteAsync(h.BindingUrl(created.Id));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var listed = await h.Client.GetFromJsonAsync<BindingListResponse>(h.BindingsUrl());
        Assert.Empty(listed!.Items);
    }

    // ---------------------------------------------------------------
    // Bindings — refresh
    // ---------------------------------------------------------------

    [Fact]
    public async Task RefreshBinding_AfterRecordValueChanged_PersistsTheNewSnapshot()
    {
        await using var h = await Harness.CreateAsync();
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));
        var created = await h.CreateBindingAsync(recordId, "title");
        await h.SetRecordValuesAsync(recordId, """{"title":"Acme Updated"}""");

        var response = await h.Client.PostAsync(h.BindingUrl(created.Id) + "/refresh", content: null);

        response.EnsureSuccessStatusCode();
        var refreshed = await response.Content.ReadFromJsonAsync<BindingDto>();
        Assert.Equal("Acme Updated", ResolvedText(refreshed!));
        // Snapshot-on-open semantics: the refresh has to be written back, not
        // merely returned, or the next reader sees the stale value again.
        var listed = await h.Client.GetFromJsonAsync<BindingListResponse>(h.BindingsUrl());
        Assert.Equal("Acme Updated", ResolvedText(Assert.Single(listed!.Items)));
    }

    [Fact]
    public async Task RefreshBinding_KeepsUserSuppliedLabel()
    {
        await using var h = await Harness.CreateAsync();
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));
        var created = await h.CreateBindingAsync(recordId, "title", label: "Customer name");

        var response = await h.Client.PostAsync(h.BindingUrl(created.Id) + "/refresh", content: null);

        response.EnsureSuccessStatusCode();
        var refreshed = await response.Content.ReadFromJsonAsync<BindingDto>();
        // The resolver always returns a suggestion ("Invoice.title"); the
        // handler must only apply it when the row has no label of its own.
        Assert.Equal("Customer name", refreshed!.Label);
    }

    [Fact]
    public async Task RefreshAll_AfterRecordValueChanged_PersistsTheNewSnapshotForEveryBinding()
    {
        await using var h = await Harness.CreateAsync();
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"), ("subtitle", "Beta"));
        await h.CreateBindingAsync(recordId, "title");
        await h.CreateBindingAsync(recordId, "subtitle");
        await h.SetRecordValuesAsync(recordId, """{"title":"Acme Updated","subtitle":"Beta Updated"}""");

        var response = await h.Client.PostAsync(h.BindingsUrl() + "refresh-all", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RefreshAllResponse>();
        Assert.NotNull(body);
        Assert.Equal(2, body!.Items.Count);
        Assert.Empty(body.Failures);
        Assert.Equal(
            new[] { "Acme Updated", "Beta Updated" },
            body.Items.Select(ResolvedText).OrderBy(t => t, StringComparer.Ordinal).ToArray());

        var listed = await h.Client.GetFromJsonAsync<BindingListResponse>(h.BindingsUrl());
        Assert.All(listed!.Items, b => Assert.EndsWith("Updated", ResolvedText(b), StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAll_OneBindingFailsToResolve_RefreshesTheRestAndReportsTheFailure()
    {
        await using var h = await Harness.CreateAsync();
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));
        var good = await h.CreateBindingAsync(recordId, "title");
        // Inserted straight into Postgres because the create route resolves
        // first and would have rejected this config with a 400 — the row shape
        // this covers is a binding whose config went bad *after* it was saved
        // (record removed from the config, resolver validation tightened, …).
        var broken = await h.InsertBindingAsync(
            DocumentBindingKinds.RecordField, """{"fieldKey":"title"}""");
        await h.SetRecordValuesAsync(recordId, """{"title":"Acme Updated"}""");

        var response = await h.Client.PostAsync(h.BindingsUrl() + "refresh-all", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RefreshAllResponse>();
        Assert.NotNull(body);
        var failure = Assert.Single(body!.Failures);
        var failureJson = Assert.IsType<JsonElement>(failure);
        Assert.Equal(broken, failureJson.GetProperty("bindingId").GetGuid());
        Assert.Contains("recordId", failureJson.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
        // One bad binding must not abort the batch — the healthy one still got
        // its new value, and it was saved.
        var listed = await h.Client.GetFromJsonAsync<BindingListResponse>(h.BindingsUrl());
        Assert.Equal("Acme Updated", ResolvedText(listed!.Items.Single(b => b.Id == good.Id)));
    }

    [Fact]
    public async Task RefreshAll_DocumentWithNoBindings_ReturnsEmptyList()
    {
        await using var h = await Harness.CreateAsync();

        var response = await h.Client.PostAsync(h.BindingsUrl() + "refresh-all", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // #186 item 4: the zero-binding branch used to short-circuit with a
        // DocumentBindingListResponse while every other path returned a
        // RefreshAllResponse, so a client reading `failures.length` got
        // undefined for a document with no bindings. Deserializing as the
        // refresh shape is what proves the two agree now.
        var refreshBody = await response.Content.ReadFromJsonAsync<RefreshAllBody>();
        Assert.NotNull(refreshBody);
        Assert.Empty(refreshBody!.Items);
        Assert.Empty(refreshBody.Failures);

    }

    // ---------------------------------------------------------------
    // Bindings — failure modes
    // ---------------------------------------------------------------

    [Fact]
    public async Task CreateBinding_MalformedBody_Returns400()
    {
        await using var h = await Harness.CreateAsync();
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));

        var unknownKind = await h.Client.PostAsJsonAsync(
            h.BindingsUrl(),
            new { kind = "chart", configJsonb = RecordFieldConfig(recordId, "title"), label = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, unknownKind.StatusCode);
        Assert.Contains("chart", await unknownKind.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var blankConfig = await h.Client.PostAsJsonAsync(
            h.BindingsUrl(),
            new { kind = DocumentBindingKinds.RecordField, configJsonb = "   ", label = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, blankConfig.StatusCode);

        // Well-formed JSON the resolver still rejects — the resolver's
        // DocumentBindingResolveException.StatusCode has to reach the wire.
        var badConfig = await h.Client.PostAsJsonAsync(
            h.BindingsUrl(),
            new { kind = DocumentBindingKinds.RecordField, configJsonb = """{"fieldKey":"title"}""", label = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, badConfig.StatusCode);

        // None of the three may have left a row behind.
        var listed = await h.Client.GetFromJsonAsync<BindingListResponse>(h.BindingsUrl());
        Assert.Empty(listed!.Items);
    }

    [Fact]
    public async Task CreateBinding_UnknownDocumentId_Returns404()
    {
        await using var h = await Harness.CreateAsync();
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));

        var response = await h.Client.PostAsJsonAsync(
            $"/api/content/documents/{Guid.NewGuid()}/bindings/",
            new { kind = DocumentBindingKinds.RecordField, configJsonb = RecordFieldConfig(recordId, "title"), label = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BindingByIdRoutes_UnknownBindingId_Return404()
    {
        await using var h = await Harness.CreateAsync();
        var missing = h.BindingUrl(Guid.NewGuid());

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await h.Client.PostAsync(missing + "/refresh", content: null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await h.Client.PatchAsJsonAsync(missing, new { configJsonb = (string?)null, label = "x" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await h.Client.DeleteAsync(missing)).StatusCode);
    }

    [Fact]
    public async Task BindingByIdRoutes_BindingFromAnotherDocument_Return404()
    {
        // Every by-id route filters on (DocumentId, BindingId). Dropping the
        // documentId half would let a caller authorized on document A mutate a
        // binding that belongs to document B just by holding its GUID.
        await using var h = await Harness.CreateAsync();
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));
        var otherDocumentId = await h.SeedSecondDocumentAsync();
        var created = await h.CreateBindingAsync(recordId, "title");
        var crossDoc = $"/api/content/documents/{otherDocumentId}/bindings/{created.Id}";

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await h.Client.PostAsync(crossDoc + "/refresh", content: null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await h.Client.PatchAsJsonAsync(crossDoc, new { configJsonb = (string?)null, label = "x" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await h.Client.DeleteAsync(crossDoc)).StatusCode);

        // …and the binding is untouched on its own document.
        var listed = await h.Client.GetFromJsonAsync<BindingListResponse>(h.BindingsUrl());
        Assert.Equal(created.Id, Assert.Single(listed!.Items).Id);
    }

    // ---------------------------------------------------------------
    // Comments — round trip
    // ---------------------------------------------------------------

    [Fact]
    public async Task CreateComment_ReturnsRootCommentWithAuthorNameAndOwnThreadId()
    {
        await using var h = await Harness.CreateAsync();

        var response = await h.Client.PostAsJsonAsync(
            h.CommentsUrl(), new { number = 1, bodyText = "  needs a citation  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<DocCommentDto>();
        Assert.NotNull(created);
        Assert.Equal("needs a citation", created!.BodyText); // trimmed
        Assert.Equal(1, created.Number);
        Assert.Null(created.ParentCommentId);
        // A root comment IS its own thread — replies key off this.
        Assert.Equal(created.Id, created.ThreadId);
        Assert.Equal(Harness.AdminUserId, created.AuthorId);
        Assert.Equal("Admin User", created.AuthorName);
        Assert.Null(created.ResolvedAtUtc);

        var listed = await h.Client.GetFromJsonAsync<DocCommentListResponse>(h.CommentsUrl());
        var only = Assert.Single(listed!.Items);
        Assert.Equal(created.Id, only.Id);
        Assert.Equal("needs a citation", only.BodyText);
    }

    [Fact]
    public async Task CreateComment_BlankBody_Returns400()
    {
        await using var h = await Harness.CreateAsync();

        var response = await h.Client.PostAsJsonAsync(
            h.CommentsUrl(), new { number = 1, bodyText = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var listed = await h.Client.GetFromJsonAsync<DocCommentListResponse>(h.CommentsUrl());
        Assert.Empty(listed!.Items);
    }

    [Fact]
    public async Task CreateComment_DuplicateNumber_Returns409WithNextFreeNumber()
    {
        await using var h = await Harness.CreateAsync();
        await h.CreateCommentAsync(number: 1, "first");
        await h.CreateCommentAsync(number: 7, "second");

        var response = await h.Client.PostAsJsonAsync(
            h.CommentsUrl(), new { number = 7, bodyText = "collides" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        // The SPA retries with this number, so a wrong value here turns one
        // collision into an infinite retry loop.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(8, body.GetProperty("suggestedNumber").GetInt32());

        var listed = await h.Client.GetFromJsonAsync<DocCommentListResponse>(h.CommentsUrl());
        Assert.Equal(2, listed!.Items.Count);
    }

    [Fact]
    public async Task CreateComment_UnknownDocumentId_Returns404()
    {
        await using var h = await Harness.CreateAsync();

        var response = await h.Client.PostAsJsonAsync(
            $"/api/content/documents/{Guid.NewGuid()}/comments/",
            new { number = 1, bodyText = "orphan" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reply_CarriesParentThreadIdAndParentCommentId()
    {
        await using var h = await Harness.CreateAsync();
        var root = await h.CreateCommentAsync(number: 1, "root");

        var response = await h.Client.PostAsJsonAsync(
            h.CommentUrl(root.Id) + "/replies", new { number = 2, bodyText = "reply" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var reply = await response.Content.ReadFromJsonAsync<DocCommentDto>();
        Assert.NotNull(reply);
        Assert.Equal(root.Id, reply!.ParentCommentId);
        // Replies inherit the ROOT's thread id, not the parent's own id —
        // otherwise a reply-to-a-reply would fork the conversation and the
        // resolve/reopen thread sweeps would miss half of it.
        Assert.Equal(root.ThreadId, reply.ThreadId);
        Assert.Equal("reply", reply.BodyText);

        var nested = await h.Client.PostAsJsonAsync(
            h.CommentUrl(reply.Id) + "/replies", new { number = 3, bodyText = "nested" });
        nested.EnsureSuccessStatusCode();
        var nestedDto = await nested.Content.ReadFromJsonAsync<DocCommentDto>();
        Assert.Equal(root.ThreadId, nestedDto!.ThreadId);
        Assert.Equal(reply.Id, nestedDto.ParentCommentId);
    }

    [Fact]
    public async Task Reply_ToUnknownParent_Returns404()
    {
        await using var h = await Harness.CreateAsync();

        var response = await h.Client.PostAsJsonAsync(
            h.CommentUrl(Guid.NewGuid()) + "/replies", new { number = 2, bodyText = "reply" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_MarksEveryCommentInTheThreadResolved()
    {
        await using var h = await Harness.CreateAsync();
        var root = await h.CreateCommentAsync(number: 1, "root");
        var reply = await h.ReplyAsync(root.Id, number: 2, "reply");
        var untouched = await h.CreateCommentAsync(number: 3, "other thread");

        // Resolve is issued against the REPLY: resolving is a thread-level act,
        // so the root has to come along even though it wasn't the target.
        var response = await h.Client.PostAsync(h.CommentUrl(reply.Id) + "/resolve", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var listed = await h.ListCommentsAsync();
        foreach (var c in listed.Where(c => c.ThreadId == root.ThreadId))
        {
            Assert.NotNull(c.ResolvedAtUtc);
            Assert.Equal(Harness.AdminUserId, c.ResolvedByUserId);
            Assert.Equal("Admin User", c.ResolvedByUserName);
        }
        // The unrelated thread must be left alone.
        Assert.Null(listed.Single(c => c.Id == untouched.Id).ResolvedAtUtc);
    }

    [Fact]
    public async Task Resolve_AlreadyResolvedThread_IsNoOpAndKeepsOriginalResolution()
    {
        await using var h = await Harness.CreateAsync();
        var root = await h.CreateCommentAsync(number: 1, "root");
        (await h.Client.PostAsync(h.CommentUrl(root.Id) + "/resolve", content: null))
            .EnsureSuccessStatusCode();
        var firstResolvedAt = (await h.ListCommentsAsync()).Single().ResolvedAtUtc;

        var second = await h.Client.PostAsync(h.CommentUrl(root.Id) + "/resolve", content: null);

        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        // Re-resolving must not restamp the row — that would rewrite who closed
        // the thread and when, which the sidebar shows verbatim.
        Assert.Equal(firstResolvedAt, (await h.ListCommentsAsync()).Single().ResolvedAtUtc);
    }

    [Fact]
    public async Task Reopen_ClearsResolutionForTheWholeThread()
    {
        await using var h = await Harness.CreateAsync();
        var root = await h.CreateCommentAsync(number: 1, "root");
        await h.ReplyAsync(root.Id, number: 2, "reply");
        (await h.Client.PostAsync(h.CommentUrl(root.Id) + "/resolve", content: null))
            .EnsureSuccessStatusCode();

        var response = await h.Client.PostAsync(h.CommentUrl(root.Id) + "/reopen", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var listed = await h.ListCommentsAsync();
        Assert.Equal(2, listed.Count);
        Assert.All(listed, c =>
        {
            Assert.Null(c.ResolvedAtUtc);
            Assert.Null(c.ResolvedByUserId);
        });

        // Reopening something already open is the same idempotent no-op.
        var again = await h.Client.PostAsync(h.CommentUrl(root.Id) + "/reopen", content: null);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [Fact]
    public async Task ListComments_IncludeResolvedFalse_OmitsResolvedThreads()
    {
        await using var h = await Harness.CreateAsync();
        var resolved = await h.CreateCommentAsync(number: 1, "closed");
        var open = await h.CreateCommentAsync(number: 2, "still open");
        (await h.Client.PostAsync(h.CommentUrl(resolved.Id) + "/resolve", content: null))
            .EnsureSuccessStatusCode();

        var withResolved = await h.Client.GetFromJsonAsync<DocCommentListResponse>(h.CommentsUrl());
        var withoutResolved = await h.Client.GetFromJsonAsync<DocCommentListResponse>(
            h.CommentsUrl() + "?includeResolved=false");

        // Default is "include" so the editor can render resolved threads
        // collapsed; only the explicit false filters them out.
        Assert.Equal(2, withResolved!.Items.Count);
        Assert.Equal(open.Id, Assert.Single(withoutResolved!.Items).Id);
    }

    [Fact]
    public async Task DeleteComment_OwnComment_RemovesIt()
    {
        await using var h = await Harness.CreateAsync();
        var comment = await h.CreateCommentAsync(number: 1, "mine");

        var response = await h.Client.DeleteAsync(h.CommentUrl(comment.Id));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await h.ListCommentsAsync());
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await h.Client.DeleteAsync(h.CommentUrl(comment.Id))).StatusCode);
    }

    // ---------------------------------------------------------------
    // Authorization — the gates each route actually declares
    // ---------------------------------------------------------------

    [Fact]
    public async Task ListBindings_WithoutViewGrant_IsForbidden()
    {
        await using var h = await Harness.CreateGatedAsync();

        var response = await h.Client.GetAsync(h.BindingsUrl());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListBindings_WithViewGrant_Succeeds()
    {
        await using var h = await Harness.CreateGatedAsync();
        await h.GrantOnDocumentAsync(Actions.View);
        await h.InsertBindingAsync(
            DocumentBindingKinds.RecordField,
            RecordFieldConfig(Guid.NewGuid(), "title"),
            label: "Seeded");

        var listed = await h.Client.GetFromJsonAsync<BindingListResponse>(h.BindingsUrl());

        Assert.Equal("Seeded", Assert.Single(listed!.Items).Label);
    }

    [Fact]
    public async Task CreateBinding_WithViewGrantOnly_IsForbiddenAndWritesNothing()
    {
        // Reading a document must not carry the right to embed live data in it.
        await using var h = await Harness.CreateGatedAsync();
        await h.GrantOnDocumentAsync(Actions.View);
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));

        var response = await h.Client.PostAsJsonAsync(
            h.BindingsUrl(),
            new { kind = DocumentBindingKinds.RecordField, configJsonb = RecordFieldConfig(recordId, "title"), label = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var listed = await h.Client.GetFromJsonAsync<BindingListResponse>(h.BindingsUrl());
        Assert.Empty(listed!.Items);
    }

    [Fact]
    public async Task CreateBinding_WithCommentGrant_IsForbidden()
    {
        // Document.Comment is the Commenter role's ceiling. If binding writes
        // were ever gated on Comment instead of Edit, a commenter could rewrite
        // what the document displays without touching its body.
        await using var h = await Harness.CreateGatedAsync();
        await h.GrantOnDocumentAsync(Actions.View);
        await h.GrantOnDocumentAsync(Actions.Comment);
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));

        var response = await h.Client.PostAsJsonAsync(
            h.BindingsUrl(),
            new { kind = DocumentBindingKinds.RecordField, configJsonb = RecordFieldConfig(recordId, "title"), label = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateAndDeleteBinding_WithEditGrant_Succeed()
    {
        await using var h = await Harness.CreateGatedAsync();
        await h.GrantOnDocumentAsync(Actions.View);
        await h.GrantOnDocumentAsync(Actions.Edit);
        // Record.View too, so create-time resolution returns the real value
        // rather than the "(no permission)" placeholder — see
        // RefreshAll_WithoutRecordViewGrant_… for that half.
        await h.GrantOnRecordsAsync(Actions.View);
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));

        var created = await h.Client.PostAsJsonAsync(
            h.BindingsUrl(),
            new { kind = DocumentBindingKinds.RecordField, configJsonb = RecordFieldConfig(recordId, "title"), label = (string?)null });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<BindingDto>();
        Assert.Equal("Acme", ResolvedText(dto!));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await h.Client.DeleteAsync(h.BindingUrl(dto!.Id))).StatusCode);
    }

    [Fact]
    public async Task RefreshAll_WithEditGrantButNoRefreshBindingsGrant_IsForbidden()
    {
        // The load-bearing (EntityKind, Action) assertion for bindings:
        // refresh is gated on Document.RefreshBindings, deliberately split from
        // Document.Edit so the Commenter role could later be promoted to
        // refresh without gaining edit. Collapsing the two back into Edit —
        // in either direction — fails here.
        await using var h = await Harness.CreateGatedAsync();
        await h.GrantOnDocumentAsync(Actions.View);
        await h.GrantOnDocumentAsync(Actions.Edit);
        await h.InsertBindingAsync(
            DocumentBindingKinds.RecordField, RecordFieldConfig(Guid.NewGuid(), "title"));

        var refreshAll = await h.Client.PostAsync(h.BindingsUrl() + "refresh-all", content: null);
        var bindingId = Assert.Single(
            (await h.Client.GetFromJsonAsync<BindingListResponse>(h.BindingsUrl()))!.Items).Id;
        var refreshOne = await h.Client.PostAsync(h.BindingUrl(bindingId) + "/refresh", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, refreshAll.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refreshOne.StatusCode);
    }

    [Fact]
    public async Task RefreshAll_WithRefreshBindingsGrant_Succeeds()
    {
        await using var h = await Harness.CreateGatedAsync();
        await h.GrantOnDocumentAsync(Actions.View);
        await h.GrantOnDocumentAsync(Actions.RefreshBindings);
        await h.GrantOnRecordsAsync(Actions.View);
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));
        await h.InsertBindingAsync(
            DocumentBindingKinds.RecordField, RecordFieldConfig(recordId, "title"));

        var response = await h.Client.PostAsync(h.BindingsUrl() + "refresh-all", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RefreshAllResponse>();
        Assert.Equal("Acme", ResolvedText(Assert.Single(body!.Items)));
    }

    [Fact]
    public async Task RefreshAll_WithoutRecordViewGrant_SnapshotsDeniedInsteadOfTheValue()
    {
        // The leak #90 was filed over: if bindings resolved under anything but
        // the caller's own grants, this refresh would stamp "Acme" into a
        // document readable by someone with no right to that record.
        await using var h = await Harness.CreateGatedAsync();
        await h.GrantOnDocumentAsync(Actions.View);
        await h.GrantOnDocumentAsync(Actions.RefreshBindings);
        var recordId = await h.SeedRecordAsync("Invoice", ("title", "Acme"));
        await h.InsertBindingAsync(
            DocumentBindingKinds.RecordField, RecordFieldConfig(recordId, "title"));

        var response = await h.Client.PostAsync(h.BindingsUrl() + "refresh-all", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RefreshAllResponse>();
        var binding = Assert.Single(body!.Items);
        Assert.Equal("denied", ResolvedType(binding));
        Assert.Equal("(no permission)", ResolvedText(binding));
        Assert.DoesNotContain("Acme", binding.LastResolvedValueJsonb!, StringComparison.Ordinal);

        // …and the denied snapshot is what got persisted, not a leftover value.
        var listed = await h.Client.GetFromJsonAsync<BindingListResponse>(h.BindingsUrl());
        Assert.Equal("denied", ResolvedType(Assert.Single(listed!.Items)));
    }

    [Fact]
    public async Task CreateComment_WithViewGrantOnly_IsForbidden()
    {
        // Reads are Document.View; posting a comment needs Document.Comment.
        await using var h = await Harness.CreateGatedAsync();
        await h.GrantOnDocumentAsync(Actions.View);

        var response = await h.Client.PostAsJsonAsync(
            h.CommentsUrl(), new { number = 1, bodyText = "sneaky" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var listed = await h.Client.GetFromJsonAsync<DocCommentListResponse>(h.CommentsUrl());
        Assert.Empty(listed!.Items);
    }

    [Fact]
    public async Task CommentLifecycle_WithCommentGrant_Succeeds()
    {
        // Comment covers create/reply/resolve/reopen — the whole Commenter
        // surface — without any Document.Edit anywhere in the grant set.
        await using var h = await Harness.CreateGatedAsync();
        await h.GrantOnDocumentAsync(Actions.View);
        await h.GrantOnDocumentAsync(Actions.Comment);

        var created = await h.Client.PostAsJsonAsync(
            h.CommentsUrl(), new { number = 1, bodyText = "please clarify" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var root = await created.Content.ReadFromJsonAsync<DocCommentDto>();

        Assert.Equal(
            HttpStatusCode.Created,
            (await h.Client.PostAsJsonAsync(
                h.CommentUrl(root!.Id) + "/replies", new { number = 2, bodyText = "on it" })).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await h.Client.PostAsync(h.CommentUrl(root.Id) + "/resolve", content: null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await h.Client.PostAsync(h.CommentUrl(root.Id) + "/reopen", content: null)).StatusCode);

        var listed = await h.Client.GetFromJsonAsync<DocCommentListResponse>(h.CommentsUrl());
        Assert.Equal(2, listed!.Items.Count);
        Assert.All(listed.Items, c => Assert.Null(c.ResolvedAtUtc));
    }

    [Fact]
    public async Task DeleteComment_AuthoredByAnotherUser_WithCommentGrantOnly_IsForbidden()
    {
        // The route filter only asks for Document.Comment; the handler layers a
        // Document.Edit check on top when the caller isn't the author. Losing
        // that inner check would let any commenter prune other people's threads.
        await using var h = await Harness.CreateGatedAsync();
        await h.GrantOnDocumentAsync(Actions.View);
        await h.GrantOnDocumentAsync(Actions.Comment);
        var someoneElses = await h.InsertCommentAsync(number: 1, "not yours", authorId: Guid.NewGuid());

        var response = await h.Client.DeleteAsync(h.CommentUrl(someoneElses));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var listed = await h.Client.GetFromJsonAsync<DocCommentListResponse>(h.CommentsUrl());
        Assert.Equal(someoneElses, Assert.Single(listed!.Items).Id);
    }

    [Fact]
    public async Task DeleteComment_AuthoredByAnotherUser_WithEditGrant_Succeeds()
    {
        await using var h = await Harness.CreateGatedAsync();
        await h.GrantOnDocumentAsync(Actions.View);
        await h.GrantOnDocumentAsync(Actions.Comment);
        await h.GrantOnDocumentAsync(Actions.Edit);
        var someoneElses = await h.InsertCommentAsync(number: 1, "not yours", authorId: Guid.NewGuid());

        var response = await h.Client.DeleteAsync(h.CommentUrl(someoneElses));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var listed = await h.Client.GetFromJsonAsync<DocCommentListResponse>(h.CommentsUrl());
        Assert.Empty(listed!.Items);
    }

    // ---------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------

    private static string RecordFieldConfig(Guid recordId, string fieldKey) =>
        $$"""{"recordId":"{{recordId}}","fieldKey":"{{fieldKey}}"}""";

    private static string ResolvedText(BindingDto binding) =>
        ResolvedProperty(binding, "text");

    private static string ResolvedType(BindingDto binding) =>
        ResolvedProperty(binding, "type");

    private static string ResolvedProperty(BindingDto binding, string name)
    {
        Assert.NotNull(binding.LastResolvedValueJsonb);
        using var doc = JsonDocument.Parse(binding.LastResolvedValueJsonb!);
        return doc.RootElement.GetProperty(name).GetString()!;
    }

    private static JsonElement Json(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private sealed class Harness : IAsyncDisposable
    {
        // Seeded by infra/postgres/init/02-create-autonate-app-schema.sql; the
        // dev auto-login middleware signs the test client in as this user.
        public static readonly Guid AdminUserId =
            Guid.Parse("11111111-1111-1111-1111-111111111111");

        private readonly AutoNateWebApplicationFactory _factory;

        private Harness(
            AutoNateWebApplicationFactory factory,
            HttpClient client,
            Guid projectId,
            Guid folderId,
            Guid documentId)
        {
            _factory = factory;
            Client = client;
            ProjectId = projectId;
            FolderId = folderId;
            DocumentId = documentId;
        }

        public HttpClient Client { get; }
        public Guid ProjectId { get; }
        public Guid FolderId { get; }
        public Guid DocumentId { get; }

        // Admin holds SuperAdmin, so ContentAuthorizer short-circuits and the
        // handler bodies (not the gates) are what these tests measure.
        public static Task<Harness> CreateAsync() =>
            BuildAsync(new Dictionary<string, string?>
            {
                ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true"
            });

        // No SuperAdmin and full enforcement: every decision comes from the
        // grants a test adds explicitly — for records as well as documents.
        public static Task<Harness> CreateGatedAsync() =>
            BuildAsync(new Dictionary<string, string?>
            {
                ["Authorization:Enabled"] = "true",
                ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
                ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
            });

        private static async Task<Harness> BuildAsync(Dictionary<string, string?> config)
        {
            var factory = await AutoNateWebApplicationFactory.CreateAsync(config);
            var client = factory.CreateClient();
            // Dev auto-login skips POSTs — land the auth cookie with a GET first.
            (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

            var (projectId, folderId, documentId) = await SeedProjectAndDocumentAsync(factory);
            return new Harness(factory, client, projectId, folderId, documentId);
        }

        public string BindingsUrl() => $"/api/content/documents/{DocumentId}/bindings/";

        public string BindingUrl(Guid bindingId) => BindingsUrl() + bindingId;

        public string CommentsUrl() => $"/api/content/documents/{DocumentId}/comments/";

        public string CommentUrl(Guid commentId) => CommentsUrl() + commentId;

        // ---- seeding (service layer, so endpoint authorization never
        // interferes with fixture setup under the gated harness) ----

        private static async Task<(Guid ProjectId, Guid FolderId, Guid DocumentId)> SeedProjectAndDocumentAsync(
            AutoNateWebApplicationFactory factory)
        {
            using var scope = factory.Services.CreateScope();
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            var tree = scope.ServiceProvider.GetRequiredService<IContentTreeService>();

            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = "p-" + Guid.NewGuid().ToString("N")[..8],
                DeletionsLocked = false,
                IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = AdminUserId, UpdatedBy = AdminUserId
            };
            var folder = new Folder
            {
                Id = Guid.NewGuid(), ProjectId = project.Id, ParentFolderId = null,
                Name = "f", SortOrder = 0, IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = AdminUserId, UpdatedBy = AdminUserId
            };
            var document = NewDocument(project.Id, folder.Id, "Doc", now);

            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                db.Projects.Add(project);
                db.Folders.Add(folder);
                db.Documents.Add(document);
                await db.SaveChangesAsync();
            }

            foreach (var (kind, id) in new[]
            {
                (ContentKinds.Project, project.Id),
                (ContentKinds.Folder, folder.Id),
                (ContentKinds.Document, document.Id)
            })
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                await tree.InsertSelfWithAncestorsAsync(db, kind, id, default);
            }

            return (project.Id, folder.Id, document.Id);
        }

        // A second document under the SAME project, so a caller authorized on
        // one is authorized on the other and only the handler's
        // (DocumentId, BindingId) predicate can separate them.
        public async Task<Guid> SeedSecondDocumentAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            var tree = scope.ServiceProvider.GetRequiredService<IContentTreeService>();

            var document = NewDocument(ProjectId, FolderId, "Other doc", DateTime.UtcNow);
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                db.Documents.Add(document);
                await db.SaveChangesAsync();
            }
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                await tree.InsertSelfWithAncestorsAsync(db, ContentKinds.Document, document.Id, default);
            }
            return document.Id;
        }

        private static Document NewDocument(Guid projectId, Guid folderId, string title, DateTime now) =>
            new()
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId, FolderId = folderId,
                Kind = DocumentKinds.Document,
                Title = title, BodyJsonb = "{}",
                CurrentVersionNumber = 1, SortOrder = 0,
                IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = AdminUserId, UpdatedBy = AdminUserId
            };

        // One record type + text fields + one record, all through the stores so
        // record-endpoint authorization is out of the picture.
        public async Task<Guid> SeedRecordAsync(string name, params (string Key, string Value)[] fields)
        {
            using var scope = _factory.Services.CreateScope();
            var types = scope.ServiceProvider.GetRequiredService<IRecordTypeStore>();
            var records = scope.ServiceProvider.GetRequiredService<IRecordStore>();

            var recordType = await types.CreateAsync(
                new CreateRecordTypeInput("bind", "Bindable", null, null, null), AdminUserId);
            var sortOrder = 0;
            foreach (var (key, _) in fields)
            {
                await types.CreateFieldAsync(
                    recordType.Id,
                    new CreateRecordTypeFieldInput(key, key, FieldTypeNames.Text, Json("{}"), false, sortOrder++),
                    AdminUserId);
            }

            var values = Json("{" + string.Join(",", fields.Select(f =>
                $"{JsonSerializer.Serialize(f.Key)}:{JsonSerializer.Serialize(f.Value)}")) + "}");
            var record = await records.CreateAsync(
                new CreateRecordInput(recordType.Id, name, null, null, values, null), AdminUserId);
            return record.Id;
        }

        public async Task SetRecordValuesAsync(Guid recordId, string valuesJson)
        {
            using var scope = _factory.Services.CreateScope();
            var records = scope.ServiceProvider.GetRequiredService<IRecordStore>();
            await records.UpdateAsync(
                recordId,
                new UpdateRecordInput(
                    Name: null,
                    Status: Optional<string?>.None,
                    DueDate: Optional<DateOnly?>.None,
                    Values: Json(valuesJson),
                    AssigneeIds: null),
                AdminUserId);
        }

        // ---- direct row writes, for states the routes can't produce ----

        public async Task<Guid> InsertBindingAsync(string kind, string configJsonb, string? label = null)
        {
            using var scope = _factory.Services.CreateScope();
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            var now = DateTime.UtcNow;
            var binding = new DocumentBinding
            {
                Id = Guid.NewGuid(),
                DocumentId = DocumentId,
                Kind = kind,
                ConfigJsonb = configJsonb,
                Label = label,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = AdminUserId, UpdatedBy = AdminUserId
            };
            await using var db = await dbFactory.CreateDbContextAsync();
            db.DocumentBindings.Add(binding);
            await db.SaveChangesAsync();
            return binding.Id;
        }

        // Auto-login pins the caller to `admin`, so a comment by anyone else has
        // to be written directly.
        public async Task<Guid> InsertCommentAsync(int number, string bodyText, Guid authorId)
        {
            using var scope = _factory.Services.CreateScope();
            var dbFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            var now = DateTime.UtcNow;
            var id = Guid.NewGuid();
            await using var db = await dbFactory.CreateDbContextAsync();
            db.DocumentComments.Add(new DocumentComment
            {
                Id = id,
                DocumentId = DocumentId,
                Number = number,
                ParentCommentId = null,
                ThreadId = id,
                AuthorId = authorId,
                BodyText = bodyText,
                CreatedAtUtc = now, UpdatedAtUtc = now
            });
            await db.SaveChangesAsync();
            return id;
        }

        // ---- grants ----

        public Task GrantOnDocumentAsync(string action) =>
            GrantAsync(action, $"/document/{DocumentId}");

        public Task GrantOnRecordsAsync(string action) =>
            GrantAsync(action, $"/{EntityKinds.Record}/*");

        private async Task GrantAsync(string action, string selector)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
            await grants.CreateAsync(
                new CreatePermissionGrantInput(
                    EntityKinds.User, AdminUserId.ToString(), action, selector, "allow", 0),
                AdminUserId);
        }

        // ---- HTTP conveniences ----

        public async Task<BindingDto> CreateBindingAsync(Guid recordId, string fieldKey, string? label = null)
        {
            var response = await Client.PostAsJsonAsync(
                BindingsUrl(),
                new
                {
                    kind = DocumentBindingKinds.RecordField,
                    configJsonb = RecordFieldConfig(recordId, fieldKey),
                    label
                });
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<BindingDto>();
            Assert.NotNull(dto);
            return dto!;
        }

        public async Task<DocCommentDto> CreateCommentAsync(int number, string bodyText)
        {
            var response = await Client.PostAsJsonAsync(CommentsUrl(), new { number, bodyText });
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<DocCommentDto>();
            Assert.NotNull(dto);
            return dto!;
        }

        public async Task<DocCommentDto> ReplyAsync(Guid parentId, int number, string bodyText)
        {
            var response = await Client.PostAsJsonAsync(
                CommentUrl(parentId) + "/replies", new { number, bodyText });
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<DocCommentDto>();
            Assert.NotNull(dto);
            return dto!;
        }

        public async Task<List<DocCommentDto>> ListCommentsAsync()
        {
            var listed = await Client.GetFromJsonAsync<DocCommentListResponse>(CommentsUrl());
            Assert.NotNull(listed);
            return listed!.Items;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _factory.DisposeAsync();
        }
    }
    private sealed record RefreshAllBody(
        List<JsonElement> Items,
        List<JsonElement> Failures);

}
