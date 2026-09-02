using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// Regression coverage for archived-151: the SPA's comment-audit proxy
// (lib/yjs/commentAudit.ts) POSTs `{ documentName, threadId, commentId?,
// eventType }` — the Yjs document name it minted the ticket with — and the
// endpoint used to require a bare `pageId`, so every comment event was a
// 400. These tests post the exact client body shape and assert the event
// reaches the audit publisher with the *page* the thread belongs to.
[Trait("Category", "Integration")]
public sealed class YjsCommentEventEndpointTests
{
    [Fact]
    public async Task PageDocumentName_PublishesCommentCreatedForThatPage()
    {
        await using var ctx = await TestContext.CreateAsync();
        var (pageId, _) = await ctx.SeedPageWithNoteAsync();
        ctx.Factory.RecordedAuditEvents.Clear();

        var response = await ctx.Client.PostAsJsonAsync("/api/yjs/comment-event", new
        {
            documentName = $"page:{pageId}",
            threadId = "thread-1",
            commentId = "comment-1",
            eventType = "created"
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var evt = Assert.Single(
            ctx.Factory.RecordedAuditEvents.Events,
            e => e.EventType == ContentEventTypes.CommentCreated);
        Assert.Equal(ContentEventTopic.TopicName, evt.Topic);
        Assert.Equal(ContentResourceKinds.Comment, evt.ResourceKind);

        var resource = evt.Resource!;
        Assert.Equal(pageId, ReadProp<Guid>(resource, "pageId"));
        Assert.Null(ReadProp<Guid?>(resource, "noteId"));
        Assert.Equal($"page:{pageId}", ReadProp<string>(resource, "documentName"));
        Assert.Equal("thread-1", ReadProp<string>(resource, "threadId"));
        Assert.Equal("comment-1", ReadProp<string?>(resource, "commentId"));
    }

    [Fact]
    public async Task NoteDocumentName_ResolvesToParentPageAndCarriesNoteId()
    {
        await using var ctx = await TestContext.CreateAsync();
        var (pageId, noteId) = await ctx.SeedPageWithNoteAsync();
        ctx.Factory.RecordedAuditEvents.Clear();

        var response = await ctx.Client.PostAsJsonAsync("/api/yjs/comment-event", new
        {
            documentName = $"note:{noteId}",
            threadId = "thread-2",
            eventType = "resolved"
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var evt = Assert.Single(
            ctx.Factory.RecordedAuditEvents.Events,
            e => e.EventType == ContentEventTypes.CommentResolved);
        var resource = evt.Resource!;
        Assert.Equal(pageId, ReadProp<Guid>(resource, "pageId"));
        Assert.Equal(noteId, ReadProp<Guid?>(resource, "noteId"));
        Assert.Equal($"note:{noteId}", ReadProp<string>(resource, "documentName"));
        Assert.Null(ReadProp<string?>(resource, "commentId"));
    }

    [Fact]
    public async Task LegacyPageIdBody_StillAccepted()
    {
        await using var ctx = await TestContext.CreateAsync();
        var (pageId, _) = await ctx.SeedPageWithNoteAsync();
        ctx.Factory.RecordedAuditEvents.Clear();

        var response = await ctx.Client.PostAsJsonAsync("/api/yjs/comment-event", new
        {
            pageId = pageId.ToString(),
            threadId = "thread-3",
            eventType = "deleted"
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var evt = Assert.Single(
            ctx.Factory.RecordedAuditEvents.Events,
            e => e.EventType == ContentEventTypes.CommentDeleted);
        Assert.Equal(pageId, ReadProp<Guid>(evt.Resource!, "pageId"));
    }

    [Theory]
    [InlineData("bogus")]                                   // no kind prefix
    [InlineData("page:not-a-guid")]                         // bad id
    [InlineData("pagemeta:00000000-0000-0000-0000-000000000001")] // no threads on the tab strip
    [InlineData("documents:00000000-0000-0000-0000-000000000001")] // not a BlockNote doc
    public async Task UnusableDocumentName_Returns400_AndPublishesNothing(string documentName)
    {
        await using var ctx = await TestContext.CreateAsync();
        await ctx.SeedPageWithNoteAsync();
        ctx.Factory.RecordedAuditEvents.Clear();

        var response = await ctx.Client.PostAsJsonAsync("/api/yjs/comment-event", new
        {
            documentName,
            threadId = "thread-4",
            eventType = "created"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(
            ctx.Factory.RecordedAuditEvents.Events,
            e => e.ResourceKind == ContentResourceKinds.Comment);
    }

    [Fact]
    public async Task NeitherDocumentNameNorPageId_Returns400()
    {
        await using var ctx = await TestContext.CreateAsync();
        await ctx.SeedPageWithNoteAsync();

        var response = await ctx.Client.PostAsJsonAsync("/api/yjs/comment-event", new
        {
            threadId = "thread-5",
            eventType = "created"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownNote_Returns404()
    {
        await using var ctx = await TestContext.CreateAsync();
        await ctx.SeedPageWithNoteAsync();

        var response = await ctx.Client.PostAsJsonAsync("/api/yjs/comment-event", new
        {
            documentName = $"note:{Guid.NewGuid()}",
            threadId = "thread-6",
            eventType = "created"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static T? ReadProp<T>(object resource, string name)
    {
        var prop = resource.GetType().GetProperty(name);
        Assert.NotNull(prop);
        return (T?)prop.GetValue(resource);
    }

    private sealed class TestContext : IAsyncDisposable
    {
        private TestContext(AutoNateWebApplicationFactory factory, HttpClient client)
        {
            Factory = factory;
            Client = client;
        }

        public AutoNateWebApplicationFactory Factory { get; }
        public HttpClient Client { get; }

        public static async Task<TestContext> CreateAsync()
        {
            var factory = await AutoNateWebApplicationFactory.CreateAsync(
                new Dictionary<string, string?>
                {
                    // The handler authorizes Page.View through ContentAuthorizer;
                    // grant the seeded admin SuperAdmin so that check passes.
                    ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true"
                });
            var client = factory.CreateClient();
            // Dev auto-login skips POSTs — land the auth cookie with a GET first.
            (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
            return new TestContext(factory, client);
        }

        // Project → cabinet → notebook → page (in the content-ancestor
        // closure, as the authorizer expects) plus one richtext note on it.
        public async Task<(Guid PageId, Guid NoteId)> SeedPageWithNoteAsync()
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
                Id = Guid.NewGuid(), Name = "comment-event-tests",
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
            var page = new Page
            {
                Id = Guid.NewGuid(), NotebookId = notebook.Id, ParentPageId = null,
                Title = "p", BodyJsonb = "{}", CurrentVersionNumber = 1, SortOrder = 0,
                IsArchived = false,
                CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
            };
            var note = new Note
            {
                Id = Guid.NewGuid(), PageId = page.Id, PageNoteIndex = 1,
                NoteKind = "richtext", Title = "n", ContentJsonb = "{}",
                CurrentVersionNumber = 1, SortOrder = 0,
                CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = actorId, UpdatedBy = actorId
            };

            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                db.Projects.Add(project);
                db.Cabinets.Add(cabinet);
                db.Notebooks.Add(notebook);
                db.Pages.Add(page);
                db.Notes.Add(note);
                await db.SaveChangesAsync();
            }

            foreach (var (kind, id) in new[]
            {
                (ContentKinds.Project, project.Id),
                (ContentKinds.Cabinet, cabinet.Id),
                (ContentKinds.Notebook, notebook.Id),
                (ContentKinds.Page, page.Id)
            })
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                await tree.InsertSelfWithAncestorsAsync(db, kind, id, default);
            }

            return (page.Id, note.Id);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }
}
