using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Models.Forms;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Forms;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

[Trait("Category", "Integration")]
public sealed class FormEnforcementTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfig() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    private static async Task<Guid> SeedFormAsync(AutoNateWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IFormStore>();
        var form = await store.CreateAsync(
            new CreateFormRequest("Test Form", "TST", "() => null", false),
            AdminUserId,
            CancellationToken.None);
        return form.Id;
    }

    private static async Task GrantAsync(
        AutoNateWebApplicationFactory factory, string action, string selector)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, AdminUserId.ToString(),
            action, selector, "allow", 0), AdminUserId);
    }

    [Fact]
    public async Task ListForms_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync("/api/forms/");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetForm_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var formId = await SeedFormAsync(factory);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/forms/{formId}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GetForm_WithFormViewGrant_Returns200()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var formId = await SeedFormAsync(factory);
        await GrantAsync(factory, Actions.View, "/form/*");
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.GetAsync($"/api/forms/{formId}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteForm_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var formId = await SeedFormAsync(factory);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.DeleteAsync($"/api/forms/{formId}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task PublishForm_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var formId = await SeedFormAsync(factory);
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsync($"/api/forms/{formId}/publish", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task CreateForm_NoGrant_Returns403()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(EnforceConfig());
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsJsonAsync(
            "/api/forms/",
            new CreateFormRequest("New", "NEW", "() => null", false));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
