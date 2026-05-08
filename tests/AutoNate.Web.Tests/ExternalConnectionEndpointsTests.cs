using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Services.ExternalConnections;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class ExternalConnectionEndpointsTests
{
    [Fact]
    public async Task Create_then_get_then_list_round_trips_through_the_admin_endpoint()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);
        factory.RecordedAuditEvents.Clear();

        var createBody = new
        {
            kind = "LlmProvider:Anthropic",
            name = "Production Anthropic",
            description = "Default Claude key",
            isEnabled = true,
            metadata = new { baseUrl = "https://api.anthropic.com", model = "claude-sonnet-4-6" },
            secret = "sk-ant-api03-PRODUCTION-KEY-abcd"
        };

        var createResp = await client.PostAsJsonAsync("/api/external-connections", createBody);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("Production Anthropic", created.GetProperty("name").GetString());
        // Plaintext must NOT be present in the response payload.
        Assert.False(created.TryGetProperty("secret", out _), "Response must not echo the plaintext secret.");
        Assert.False(created.TryGetProperty("secretCiphertext", out _));
        var fingerprint = created.GetProperty("secretFingerprint").GetString();
        Assert.False(string.IsNullOrEmpty(fingerprint));
        Assert.DoesNotContain("PRODUCTION-KEY-abcd", fingerprint);

        // Audit shape: created event with no plaintext leakage.
        var createdEvent = Assert.Single(
            factory.RecordedAuditEvents.Events,
            e => e.EventType == ExternalConnectionEventTypes.Created);
        Assert.Equal(ExternalConnectionEventTopic.TopicName, createdEvent.Topic);
        Assert.Equal(ExternalConnectionEventTopic.ResourceKind, createdEvent.ResourceKind);
        var rawJson = JsonSerializer.Serialize(createdEvent);
        Assert.DoesNotContain("PRODUCTION-KEY-abcd", rawJson);

        // GET by id.
        var getResp = await client.GetAsync($"/api/external-connections/{id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        // LIST shows the row.
        var listResp = await client.GetAsync("/api/external-connections");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.True(list.EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == id));
    }

    [Fact]
    public async Task Update_with_omitted_secret_keeps_existing_secret()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var createResp = await client.PostAsJsonAsync("/api/external-connections", new
        {
            kind = "LlmProvider:OpenAI",
            name = "Initial",
            isEnabled = true,
            metadata = new { },
            secret = "sk-openai-orig"
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        var originalFingerprint = created.GetProperty("secretFingerprint").GetString();

        // Update name only — secret omitted (null) means keep.
        var updateResp = await client.PutAsJsonAsync($"/api/external-connections/{id}", new
        {
            name = "Renamed"
        });
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed", updated.GetProperty("name").GetString());
        Assert.Equal(originalFingerprint, updated.GetProperty("secretFingerprint").GetString());
    }

    [Fact]
    public async Task Update_with_new_secret_rotates_fingerprint()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var createResp = await client.PostAsJsonAsync("/api/external-connections", new
        {
            kind = "LlmProvider:OpenAI",
            name = "Rotation",
            isEnabled = true,
            metadata = new { },
            secret = "sk-openai-original"
        });
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        var originalFingerprint = created.GetProperty("secretFingerprint").GetString();

        var updateResp = await client.PutAsJsonAsync($"/api/external-connections/{id}", new
        {
            secret = "sk-openai-rotated"
        });
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(originalFingerprint, updated.GetProperty("secretFingerprint").GetString());
    }

    [Fact]
    public async Task SetDefault_clears_other_defaults_for_the_same_kind()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        async Task<Guid> CreateAsync(string name)
        {
            var resp = await client.PostAsJsonAsync("/api/external-connections", new
            {
                kind = "LlmProvider:Anthropic",
                name,
                isEnabled = true,
                metadata = new { },
                secret = "sk-" + name
            });
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("id").GetGuid();
        }

        var firstId = await CreateAsync("First");
        var secondId = await CreateAsync("Second");

        var setFirst = await client.PostAsync($"/api/external-connections/{firstId}/set-default", content: null);
        Assert.Equal(HttpStatusCode.OK, setFirst.StatusCode);

        var setSecond = await client.PostAsync($"/api/external-connections/{secondId}/set-default", content: null);
        Assert.Equal(HttpStatusCode.OK, setSecond.StatusCode);

        var listResp = await client.GetAsync("/api/external-connections?kind=LlmProvider:Anthropic");
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var defaults = list.EnumerateArray().Where(e => e.GetProperty("isDefault").GetBoolean()).ToList();
        var theDefault = Assert.Single(defaults);
        Assert.Equal(secondId, theDefault.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Test_endpoint_returns_ok_with_latency_when_secret_decrypts_cleanly()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);
        factory.RecordedAuditEvents.Clear();

        var createResp = await client.PostAsJsonAsync("/api/external-connections", new
        {
            kind = "LlmProvider:Anthropic",
            name = "Tested",
            isEnabled = true,
            metadata = new { },
            secret = "sk-tested"
        });
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var testResp = await client.PostAsync($"/api/external-connections/{id}/test", content: null);
        Assert.Equal(HttpStatusCode.OK, testResp.StatusCode);
        var result = await testResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.True(result.GetProperty("latencyMs").GetInt64() >= 0);

        Assert.Single(
            factory.RecordedAuditEvents.Events,
            e => e.EventType == ExternalConnectionEventTypes.Tested);
    }

    [Fact]
    public async Task Delete_removes_the_row_and_publishes_a_deleted_event()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var createResp = await client.PostAsJsonAsync("/api/external-connections", new
        {
            kind = "LlmProvider:OpenAI",
            name = "Doomed",
            isEnabled = true,
            metadata = new { },
            secret = "sk-doomed"
        });
        var id = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        factory.RecordedAuditEvents.Clear();

        var deleteResp = await client.DeleteAsync($"/api/external-connections/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var afterDelete = await client.GetAsync($"/api/external-connections/{id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);

        Assert.Single(
            factory.RecordedAuditEvents.Events,
            e => e.EventType == ExternalConnectionEventTypes.Deleted);
    }

    // Dev auto-login fires on GETs; we have to trigger the cookie before any
    // POST. Mirrors the helper in FormEndpointsTests.
    private static async Task PrimeAuthAsync(HttpClient client)
    {
        (await client.GetAsync("/api/external-connections")).EnsureSuccessStatusCode();
    }
}
