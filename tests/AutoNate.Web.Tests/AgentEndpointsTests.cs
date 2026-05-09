using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Services.Agent.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class AgentEndpointsTests
{
    [Fact]
    public async Task Conversation_CRUD_round_trips_for_authenticated_user()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        // Create.
        var createResp = await client.PostAsJsonAsync("/api/agent/conversations", new { pageKey = "workflow" });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("workflow", created.GetProperty("pageKey").GetString());

        // List filtered by pageKey shows it.
        var listResp = await client.GetAsync("/api/agent/conversations?pageKey=workflow");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(list.EnumerateArray().Any(c => c.GetProperty("id").GetGuid() == id));

        // Rename.
        var renameResp = await client.PatchAsJsonAsync($"/api/agent/conversations/{id}", new { title = "My investigation" });
        Assert.Equal(HttpStatusCode.OK, renameResp.StatusCode);
        var renamed = await renameResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("My investigation", renamed.GetProperty("title").GetString());

        // Get detail.
        var detailResp = await client.GetAsync($"/api/agent/conversations/{id}");
        Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);

        // Delete.
        var delResp = await client.DeleteAsync($"/api/agent/conversations/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);
        var notFound = await client.GetAsync($"/api/agent/conversations/{id}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task Send_message_streams_text_event_stream_with_data_frames()
    {
        // Scripted provider yields one text delta + end_turn. Wire a fake
        // resolver onto the host so the SSE endpoint resolves to it.
        var fakeProvider = new ScriptedProvider(new[]
        {
            new[]
            {
                (ChatStreamChunk)new ChatStreamChunk.TextDelta("Hello"),
                new ChatStreamChunk.MessageStop(ChatStopReason.EndTurn, new Usage(1, 1, null, null))
            }
        });

        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var customised = factory.WithWebHostBuilder(b =>
        {
            b.ConfigureServices(services =>
            {
                services.RemoveAll<IChatProviderResolver>();
                services.AddSingleton<IChatProviderResolver>(_ => new FakeResolver(fakeProvider));
            });
        });

        var client = customised.CreateClient();
        await PrimeAuthAsync(client);

        var createResp = await client.PostAsJsonAsync("/api/agent/conversations", new { pageKey = "workflow" });
        var conversationId = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var sendReq = new HttpRequestMessage(HttpMethod.Post, $"/api/agent/conversations/{conversationId}/messages")
        {
            Content = JsonContent.Create(new { text = "Hi assistant" })
        };
        var resp = await client.SendAsync(sendReq, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("text/event-stream", resp.Content.Headers.ContentType?.ToString() ?? "");

        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("data: {", body);
        Assert.Contains("\"text_delta\"", body);
        Assert.Contains("\"message_completed\"", body);
        Assert.Contains("\"done\"", body);
    }

    private static async Task PrimeAuthAsync(HttpClient client)
    {
        (await client.GetAsync("/api/agent/conversations")).EnsureSuccessStatusCode();
    }

    private sealed class ScriptedProvider : IChatProvider
    {
        private readonly IReadOnlyList<IReadOnlyList<ChatStreamChunk>> _scripts;
        private int _i;

        public ScriptedProvider(IReadOnlyList<IReadOnlyList<ChatStreamChunk>> scripts) => _scripts = scripts;

        public string Kind => "Scripted";
        public string ModelId => "test-model";

        public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var script = _i < _scripts.Count ? _scripts[_i] : Array.Empty<ChatStreamChunk>();
            _i++;
            foreach (var chunk in script)
            {
                yield return chunk;
                await Task.Yield();
            }
        }

        public Task<ChatProviderTestResult> TestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatProviderTestResult(true, 0, "scripted", null));
    }

    private sealed class FakeResolver : IChatProviderResolver
    {
        private readonly IChatProvider _provider;
        public FakeResolver(IChatProvider provider) => _provider = provider;
        public Task<IChatProvider?> ResolveAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IChatProvider?>(_provider);
        public Task<IChatProvider?> ResolveDefaultForKindAsync(string kind, CancellationToken ct = default) => Task.FromResult<IChatProvider?>(_provider);
    }
}
