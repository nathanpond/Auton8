using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class UserEndpointsTests
{
    [Fact]
    public async Task GetUsers_ReturnsSeededAdmin()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var users = await client.GetFromJsonAsync<IReadOnlyList<LocalUser>>("/api/users");

        Assert.NotNull(users);
        var admin = Assert.Single(users);
        Assert.Equal("admin", admin.Username);
    }

    [Fact]
    public async Task PostUsers_CreatesUser_AndItAppearsInList()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        // Trigger dev auto-login by hitting a GET first so the auth cookie is captured
        // by the test HttpClient before we send the POST.
        (await client.GetAsync("/api/users")).EnsureSuccessStatusCode();

        var createResponse = await client.PostAsJsonAsync(
            "/api/users",
            new UserEndpoints.CreateUserRequest(
                Username: "newuser",
                FirstName: "New",
                LastName: "User",
                Password: "password123",
                Email: "newuser@example.com"));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var listed = await client.GetFromJsonAsync<IReadOnlyList<LocalUser>>("/api/users");
        Assert.NotNull(listed);
        Assert.Contains(listed, u => u.Username == "newuser");
    }

    [Fact]
    public async Task DeleteUser_NotFound_Returns404()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        // Prime the auth cookie before the DELETE.
        (await client.GetAsync("/api/users")).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync("/api/users/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
