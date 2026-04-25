using AutoNate.Web.Services.Flowable;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AutoNate.Web.Tests;

internal sealed class AutoNateWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly PostgresTestDatabase _database;

    private AutoNateWebApplicationFactory(PostgresTestDatabase database)
    {
        _database = database;
        // Skip the startup Dapr probe — it would block the host from starting in tests.
        Environment.SetEnvironmentVariable("AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR", "true");
    }

    public static async Task<AutoNateWebApplicationFactory> CreateAsync()
    {
        var database = await PostgresTestDatabase.CreateAsync();
        return new AutoNateWebApplicationFactory(database);
    }

    public PostgresTestDatabase Database => _database;

    public StubFlowableClient FlowableStub => Services.GetRequiredService<StubFlowableClient>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The dev auto-login middleware only activates in the Development environment.
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _database.ConnectionString,
                // Auto-login as the seeded `admin` user so authenticated endpoints
                // are reachable on GET requests without simulating a manual login.
                ["DevelopmentAutoLogin:Enabled"] = "true",
                ["DevelopmentAutoLogin:Username"] = "admin",
                // Flowable is not exercised by these tests, but the options binding
                // requires a section to exist.
                ["Flowable:BaseAddress"] = "http://localhost/flowable",
            });
        });

        // Replace the real IFlowableClient with a stub so endpoint tests don't
        // hit a live Flowable server. Endpoint plumbing is what we're after here;
        // FlowableClient itself is tested separately.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IFlowableClient>();
            services.RemoveAll<FlowableClient>();
            services.AddSingleton<StubFlowableClient>();
            services.AddSingleton<IFlowableClient>(sp => sp.GetRequiredService<StubFlowableClient>());
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}
