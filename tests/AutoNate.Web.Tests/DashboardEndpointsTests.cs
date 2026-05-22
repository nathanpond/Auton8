using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Services.Dashboards;
using AutoNate.Web.Services.Events;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class DashboardEndpointsTests
{
    [Fact]
    public async Task ListDashboards_OnEmpty_ReturnsEmpty()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        // Dev auto-login fires on GET to populate the cookie.
        var dashboards = await client.GetFromJsonAsync<List<DashboardDto>>("/api/dashboards/");
        Assert.NotNull(dashboards);
        Assert.Empty(dashboards!);
    }

    [Fact]
    public async Task CreateDashboard_RoundTrips_AndShowsInList()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var created = await CreateDashboardAsync(client, "Sales");
        Assert.Equal("Sales", created.Name);
        Assert.Equal("private", created.Visibility);
        Assert.Equal("user", created.Scope);

        var list = await client.GetFromJsonAsync<List<DashboardDto>>("/api/dashboards/");
        Assert.NotNull(list);
        Assert.Contains(list!, d => d.Id == created.Id);
    }

    [Fact]
    public async Task GetDashboard_ReturnsDashboardWithWidgets()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var created = await CreateDashboardAsync(client, "Ops");

        var fetched = await client.GetFromJsonAsync<DashboardWithWidgetsDto>(
            $"/api/dashboards/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Dashboard.Id);
        Assert.Empty(fetched.Widgets);
    }

    [Fact]
    public async Task AddRemoveWidget_PersistsAndShowsInGet()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var dashboard = await CreateDashboardAsync(client, "Widgets");

        var addResp = await client.PostAsJsonAsync(
            $"/api/dashboards/{dashboard.Id}/widgets",
            new CreateWidgetRequestDto(
                WidgetType: "data-table",
                Title: "My table",
                Config: new Dictionary<string, object?>
                {
                    ["recordTypeId"] = Guid.NewGuid().ToString(),
                    ["columns"] = new[] { "key", "name" },
                    ["pageSize"] = 25,
                    ["includeArchived"] = false
                },
                GridX: 0, GridY: 0, GridW: 6, GridH: 4));
        Assert.Equal(HttpStatusCode.Created, addResp.StatusCode);
        var widget = await addResp.Content.ReadFromJsonAsync<DashboardWidgetDto>();
        Assert.NotNull(widget);
        Assert.Equal("data-table", widget!.WidgetType);

        var fetched = await client.GetFromJsonAsync<DashboardWithWidgetsDto>(
            $"/api/dashboards/{dashboard.Id}");
        Assert.NotNull(fetched);
        Assert.Single(fetched!.Widgets);

        var del = await client.DeleteAsync($"/api/dashboards/{dashboard.Id}/widgets/{widget.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var afterDelete = await client.GetFromJsonAsync<DashboardWithWidgetsDto>(
            $"/api/dashboards/{dashboard.Id}");
        Assert.Empty(afterDelete!.Widgets);
    }

    [Fact]
    public async Task ReplaceLayout_UpdatesPositions()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var dashboard = await CreateDashboardAsync(client, "Layout");

        var widget = await AddWidgetAsync(client, dashboard.Id, gridX: 0, gridY: 0, gridW: 4, gridH: 3);

        var resp = await client.PostAsJsonAsync(
            $"/api/dashboards/{dashboard.Id}/layout",
            new ReplaceLayoutRequestDto(new[]
            {
                new LayoutPositionDto(widget.Id, GridX: 4, GridY: 2, GridW: 6, GridH: 4)
            }));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetProperty("updated").GetInt32());

        var fetched = await client.GetFromJsonAsync<DashboardWithWidgetsDto>(
            $"/api/dashboards/{dashboard.Id}");
        var moved = fetched!.Widgets.Single();
        Assert.Equal(4, moved.GridX);
        Assert.Equal(2, moved.GridY);
        Assert.Equal(6, moved.GridW);
        Assert.Equal(4, moved.GridH);
    }

    [Fact]
    public async Task RenameDashboard_UpdatesName()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var created = await CreateDashboardAsync(client, "Initial");

        var resp = await client.PatchAsJsonAsync(
            $"/api/dashboards/{created.Id}",
            new UpdateDashboardRequestDto(Name: "Renamed", Description: null, Settings: null));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var updated = await resp.Content.ReadFromJsonAsync<DashboardDto>();
        Assert.Equal("Renamed", updated!.Name);
    }

    [Fact]
    public async Task DeleteDashboard_RemovesIt()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var created = await CreateDashboardAsync(client, "Doomed");
        var del = await client.DeleteAsync($"/api/dashboards/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var resp = await client.GetAsync($"/api/dashboards/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task EventCatalog_RegistersAllDashboardEventTypes()
    {
        // Every event type DashboardEventTypes declares must appear in
        // EventCatalog. The catalog is the contract surface — missing
        // entries fail the audit-events skill's expectations.
        var declared = typeof(DashboardEventTypes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

        var cataloged = EventCatalog.AllEntries
            .Where(e => e.Topic == DashboardEventTopic.TopicName)
            .Select(e => e.EventType)
            .ToHashSet();

        Assert.NotEmpty(declared);
        Assert.True(declared.SetEquals(cataloged),
            $"Mismatch between DashboardEventTypes and EventCatalog. " +
            $"Declared only: {string.Join(", ", declared.Except(cataloged))}. " +
            $"Cataloged only: {string.Join(", ", cataloged.Except(declared))}.");
    }

    private static async Task PrimeAuthAsync(HttpClient client)
    {
        (await client.GetAsync("/api/dashboards/")).EnsureSuccessStatusCode();
    }

    private static async Task<DashboardDto> CreateDashboardAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/dashboards/",
            new CreateDashboardRequestDto(name, null, null));
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<DashboardDto>();
        Assert.NotNull(created);
        return created!;
    }

    private static async Task<DashboardWidgetDto> AddWidgetAsync(
        HttpClient client, Guid dashboardId, int gridX, int gridY, int gridW, int gridH)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/dashboards/{dashboardId}/widgets",
            new CreateWidgetRequestDto(
                WidgetType: "data-table",
                Title: null,
                Config: new Dictionary<string, object?>(),
                GridX: gridX, GridY: gridY, GridW: gridW, GridH: gridH));
        resp.EnsureSuccessStatusCode();
        var widget = await resp.Content.ReadFromJsonAsync<DashboardWidgetDto>();
        Assert.NotNull(widget);
        return widget!;
    }

    private sealed record CreateDashboardRequestDto(string Name, string? Description, string? FromMountPath);
    private sealed record UpdateDashboardRequestDto(string? Name, string? Description, object? Settings);
    private sealed record CreateWidgetRequestDto(
        string WidgetType, string? Title, IDictionary<string, object?> Config,
        int GridX, int GridY, int GridW, int GridH);
    private sealed record LayoutPositionDto(Guid WidgetId, int GridX, int GridY, int GridW, int GridH);
    private sealed record ReplaceLayoutRequestDto(IEnumerable<LayoutPositionDto> Positions);

    private sealed record DashboardDto(
        Guid Id, Guid OwnerUserId, string Name, string? Description,
        string Visibility, string Scope, string Source, string? TemplateKey,
        JsonElement Settings,
        bool IsArchived, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid CreatedBy, Guid UpdatedBy);

    private sealed record DashboardWidgetDto(
        Guid Id, Guid DashboardId, string WidgetType, string? Title,
        JsonElement Config,
        int GridX, int GridY, int GridW, int GridH, int SortOrder,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

    private sealed record DashboardWithWidgetsDto(
        DashboardDto Dashboard,
        List<DashboardWidgetDto> Widgets);
}
