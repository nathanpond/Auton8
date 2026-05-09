using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AutoNate.Web.Services.Agent;
using AutoNate.Web.Services.Agent.Loop;
using AutoNate.Web.Services.Agent.Providers;
using AutoNate.Web.Services.SiteSettings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class AgentInternetAccessGatingTests
{
    private static readonly Guid TestUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task With_setting_off_internet_tools_are_NOT_offered_to_the_provider()
    {
        var capture = new ToolCapturingProvider();
        await using var factory = await BuildFactoryAsync(capture);
        // Setting defaults to false on a fresh DB — no admin update needed.

        await RunOneTurnAsync(factory);

        var seen = Assert.Single(capture.RequestsSeen);
        Assert.DoesNotContain(seen, t => t.Name == "fetch_url");
        Assert.DoesNotContain(seen, t => t.Name == "web_search");
        // Sanity: other diagnostic tools should still be there.
        Assert.Contains(seen, t => t.Name == "find_workflow");
    }

    [Fact]
    public async Task With_setting_on_internet_tools_ARE_offered_to_the_provider()
    {
        var capture = new ToolCapturingProvider();
        await using var factory = await BuildFactoryAsync(capture);
        await EnableInternetAccessAsync(factory);

        await RunOneTurnAsync(factory);

        var seen = Assert.Single(capture.RequestsSeen);
        Assert.Contains(seen, t => t.Name == "fetch_url");
        Assert.Contains(seen, t => t.Name == "web_search");
    }

    private static async Task<WebApplicationFactoryHandle> BuildFactoryAsync(IChatProvider provider)
    {
        var raw = await AutoNateWebApplicationFactory.CreateAsync();
        var customised = raw.WithWebHostBuilder(b =>
        {
            b.ConfigureServices(services =>
            {
                services.RemoveAll<IChatProviderResolver>();
                services.AddSingleton<IChatProviderResolver>(_ => new SingleProviderResolver(provider));
            });
        });
        return new WebApplicationFactoryHandle(raw, customised);
    }

    private static async Task RunOneTurnAsync(WebApplicationFactoryHandle handle)
    {
        _ = handle.Customised.CreateClient();
        using var scope = handle.Customised.Services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IAgentSession>();
        var convo = await session.StartAsync(TestUserId, pageKey: "workflow", connectionId: null);
        await foreach (var _ in session.SendMessageAsync(convo.Id, TestUserId, "Hello"))
        {
            // drain
        }
    }

    private static async Task EnableInternetAccessAsync(WebApplicationFactoryHandle handle)
    {
        var client = handle.Customised.CreateClient();
        // Dev auto-login fires on GETs; warm the cookie.
        (await client.GetAsync("/api/admin/site-settings")).EnsureSuccessStatusCode();

        var trueValue = JsonDocument.Parse("true").RootElement;
        var resp = await client.PatchAsJsonAsync("/api/admin/site-settings", new
        {
            updates = new Dictionary<string, JsonElement>
            {
                [SiteSettingsKeys.ChatbotInternetAccessEnabled] = trueValue
            }
        });
        resp.EnsureSuccessStatusCode();
    }

    // Records the tool list of every StreamAsync call so the test can assert
    // what the provider was offered. Yields a single end_turn chunk so the
    // agent loop terminates cleanly after one iteration.
    private sealed class ToolCapturingProvider : IChatProvider
    {
        public List<IReadOnlyList<ChatTool>> RequestsSeen { get; } = new();

        public string Kind => "Capturing";
        public string ModelId => "test-model";

        public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RequestsSeen.Add(request.Tools.ToList());
            yield return new ChatStreamChunk.TextDelta("ok");
            yield return new ChatStreamChunk.MessageStop(ChatStopReason.EndTurn, new Usage(1, 1, null, null));
            await Task.Yield();
        }

        public Task<ChatProviderTestResult> TestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatProviderTestResult(true, 0, "captured", null));
    }

    private sealed class SingleProviderResolver : IChatProviderResolver
    {
        private readonly IChatProvider _provider;
        public SingleProviderResolver(IChatProvider provider) => _provider = provider;
        public Task<IChatProvider?> ResolveAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IChatProvider?>(_provider);
        public Task<IChatProvider?> ResolveDefaultForKindAsync(string kind, CancellationToken ct = default) => Task.FromResult<IChatProvider?>(_provider);
    }

    // Owns the original factory (which holds the Postgres database fixture)
    // and the WithWebHostBuilder customisation; both must be disposed.
    private sealed class WebApplicationFactoryHandle : IAsyncDisposable
    {
        public AutoNateWebApplicationFactory Raw { get; }
        public Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Customised { get; }

        public WebApplicationFactoryHandle(
            AutoNateWebApplicationFactory raw,
            Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> customised)
        {
            Raw = raw;
            Customised = customised;
        }

        public async ValueTask DisposeAsync()
        {
            await Customised.DisposeAsync();
            await Raw.DisposeAsync();
        }
    }
}
