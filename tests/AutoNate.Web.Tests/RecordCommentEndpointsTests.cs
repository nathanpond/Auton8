using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Endpoints;
using RecordCommentEntity = AutoNate.Web.Persistence.Scaffolded.RecordComment;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class RecordCommentEndpointsTests
{
    [Fact]
    public async Task ListComments_OnNewRecord_ReturnsEmpty()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var comments = await fixture.Client.GetFromJsonAsync<CommentDto[]>(
            $"/api/records/{fixture.RecordId}/comments/");

        Assert.NotNull(comments);
        Assert.Empty(comments);
    }

    [Fact]
    public async Task CreateComment_RoundTripsAndAppearsInList()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var createResponse = await fixture.Client.PostAsJsonAsync(
            $"/api/records/{fixture.RecordId}/comments/",
            new CreateCommentRequest("Hello"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<CommentDto>();
        Assert.NotNull(created);
        Assert.Equal("Hello", created.Body);
        Assert.False(created.IsEdited);
        Assert.False(created.IsDeleted);

        var comments = await fixture.Client.GetFromJsonAsync<CommentDto[]>(
            $"/api/records/{fixture.RecordId}/comments/");
        Assert.NotNull(comments);
        Assert.Single(comments);
    }

    [Fact]
    public async Task CreateComment_EmptyBody_Returns400()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var response = await fixture.Client.PostAsJsonAsync(
            $"/api/records/{fixture.RecordId}/comments/",
            new CreateCommentRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchComment_UpdatesBodyAndMarksEdited()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var comment = await fixture.CreateCommentAsync("first");

        var response = await fixture.Client.PatchAsJsonAsync(
            $"/api/records/{fixture.RecordId}/comments/{comment.Id}",
            new UpdateCommentRequest("second"));
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<CommentDto>();
        Assert.NotNull(updated);
        Assert.Equal("second", updated.Body);
        Assert.True(updated.IsEdited);
    }

    [Fact]
    public async Task PatchComment_WrongRecordId_Returns404()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var comment = await fixture.CreateCommentAsync("hi");

        var response = await fixture.Client.PatchAsJsonAsync(
            $"/api/records/{Guid.NewGuid()}/comments/{comment.Id}",
            new UpdateCommentRequest("changed"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_MarksAsDeleted()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var comment = await fixture.CreateCommentAsync("bye");

        var response = await fixture.Client.DeleteAsync(
            $"/api/records/{fixture.RecordId}/comments/{comment.Id}");
        response.EnsureSuccessStatusCode();

        var deleted = await response.Content.ReadFromJsonAsync<CommentDto>();
        Assert.NotNull(deleted);
        Assert.True(deleted.IsDeleted);
    }

    [Fact]
    public async Task GetRevisions_AfterEdit_ReturnsPriorBody()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var comment = await fixture.CreateCommentAsync("original");

        var editResponse = await fixture.Client.PatchAsJsonAsync(
            $"/api/records/{fixture.RecordId}/comments/{comment.Id}",
            new UpdateCommentRequest("edited"));
        editResponse.EnsureSuccessStatusCode();

        var revisions = await fixture.Client.GetFromJsonAsync<CommentRevisionDto[]>(
            $"/api/records/{fixture.RecordId}/comments/{comment.Id}/revisions");
        Assert.NotNull(revisions);
        Assert.Contains(revisions, r => r.Body == "original");
    }

    [Fact]
    public async Task PatchComment_AsNonAuthor_Returns403()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var commentId = await fixture.SeedCommentAsAsync(Guid.NewGuid(), "not yours");

        var response = await fixture.Client.PatchAsJsonAsync(
            $"/api/records/{fixture.RecordId}/comments/{commentId}",
            new UpdateCommentRequest("changed"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_AsNonAuthor_Returns403()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var commentId = await fixture.SeedCommentAsAsync(Guid.NewGuid(), "not yours");

        var response = await fixture.Client.DeleteAsync(
            $"/api/records/{fixture.RecordId}/comments/{commentId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetRevisions_OnUnknownComment_Returns404()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var response = await fixture.Client.GetAsync(
            $"/api/records/{fixture.RecordId}/comments/{Guid.NewGuid()}/revisions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly AutoNateWebApplicationFactory _factory;

        private TestFixture(
            AutoNateWebApplicationFactory factory,
            HttpClient client,
            Guid recordId)
        {
            _factory = factory;
            Client = client;
            RecordId = recordId;
        }

        public HttpClient Client { get; }
        public Guid RecordId { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var factory = await AutoNateWebApplicationFactory.CreateAsync();
            var client = factory.CreateClient();
            (await client.GetAsync("/api/record-types/")).EnsureSuccessStatusCode();

            var recordTypeResponse = await client.PostAsJsonAsync(
                "/api/record-types/",
                new CreateRecordTypeRequest("task", "Task", null, null, null));
            recordTypeResponse.EnsureSuccessStatusCode();
            var recordType = await recordTypeResponse.Content.ReadFromJsonAsync<RecordTypeDto>();

            var recordResponse = await client.PostAsJsonAsync(
                "/api/records/",
                new CreateRecordRequest(
                    RecordTypeId: recordType!.Id,
                    Name: "Item",
                    Status: null,
                    DueDate: null,
                    Values: JsonDocument.Parse("{}").RootElement,
                    AssigneeIds: null));
            recordResponse.EnsureSuccessStatusCode();
            var record = await recordResponse.Content.ReadFromJsonAsync<RecordDto>();

            return new TestFixture(factory, client, record!.Id);
        }

        public async Task<CommentDto> CreateCommentAsync(string body)
        {
            var response = await Client.PostAsJsonAsync(
                $"/api/records/{RecordId}/comments/",
                new CreateCommentRequest(body));
            response.EnsureSuccessStatusCode();
            var comment = await response.Content.ReadFromJsonAsync<CommentDto>();
            Assert.NotNull(comment);
            return comment;
        }

        public async Task<Guid> SeedCommentAsAsync(Guid authorId, string body)
        {
            await using var db = _factory.Database.CreateDbContext();
            var entity = new RecordCommentEntity
            {
                Id = Guid.NewGuid(),
                RecordId = RecordId,
                AuthorId = authorId,
                Body = body,
                CreatedAtUtc = DateTime.UtcNow,
                BodyUpdatedAtUtc = DateTime.UtcNow,
                IsDeleted = false
            };
            db.RecordComments.Add(entity);
            await db.SaveChangesAsync();
            return entity.Id;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _factory.DisposeAsync();
        }
    }
}
