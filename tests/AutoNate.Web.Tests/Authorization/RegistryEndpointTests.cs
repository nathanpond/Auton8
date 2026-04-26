using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class RegistryEndpointTests
{
    [Fact]
    public async Task GetRegistry_ReturnsAllRegisteredKinds()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me"); // prime auto-login

        var registry = await client.GetFromJsonAsync<RegistryDto>("/api/admin/registry");
        Assert.NotNull(registry);

        var kinds = registry!.Kinds.Select(k => k.Kind).ToHashSet();
        Assert.Contains(EntityKinds.Record, kinds);
        Assert.Contains(EntityKinds.Role, kinds);
        Assert.Contains(EntityKinds.Group, kinds);
        Assert.Contains(EntityKinds.WorkflowTask, kinds);
        Assert.Contains(EntityKinds.WorkflowExecution, kinds);
        Assert.Contains(EntityKinds.RecordType, kinds);
        Assert.Contains(EntityKinds.WorkflowModel, kinds);
        Assert.Contains(EntityKinds.User, kinds);

        var record = registry.Kinds.Single(k => k.Kind == EntityKinds.Record);
        Assert.Contains("view", record.Actions);
        Assert.Contains("assignee", record.Tags);
    }

    private sealed record RegistryDto(IReadOnlyList<RegistryKindDto> Kinds);
    private sealed record RegistryKindDto(string Kind, string[] Actions, string[] Tags);
}
