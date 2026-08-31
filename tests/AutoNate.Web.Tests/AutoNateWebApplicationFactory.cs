using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Records;
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
    private readonly IReadOnlyDictionary<string, string?> _extraConfig;

    private AutoNateWebApplicationFactory(
        PostgresTestDatabase database,
        IReadOnlyDictionary<string, string?>? extraConfig,
        string? webRoot)
    {
        _webRoot = webRoot;
        _database = database;
        _extraConfig = extraConfig ?? new Dictionary<string, string?>();
        // Skip the startup Dapr probe — it would block the host from starting in tests.
        Environment.SetEnvironmentVariable("AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR", "true");
    }

    public static async Task<AutoNateWebApplicationFactory> CreateAsync(
        IReadOnlyDictionary<string, string?>? extraConfig = null,
        string? webRoot = null)
    {
        var database = await PostgresTestDatabase.CreateAsync();
        return new AutoNateWebApplicationFactory(database, extraConfig, webRoot);
    }

    // When set, the host boots with a real WebRootPath so Program.cs wires the
    // static-file / SPA-fallback pipeline (it is skipped when wwwroot/ is absent,
    // which is the normal Debug state). Used by SpaRootFallbackTests.
    private readonly string? _webRoot;

    public PostgresTestDatabase Database => _database;

    public StubFlowableClient FlowableStub => Services.GetRequiredService<StubFlowableClient>();

    public RecordingAuditEventPublisher RecordedAuditEvents =>
        (RecordingAuditEventPublisher)Services.GetRequiredService<IAuditEventPublisher>();

    public RecordingRecordEventPublisher RecordedRecordEvents =>
        (RecordingRecordEventPublisher)Services.GetRequiredService<IRecordEventPublisher>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The dev auto-login middleware only activates in the Development environment.
        builder.UseEnvironment(Environments.Development);
        if (_webRoot is not null)
        {
            builder.UseWebRoot(_webRoot);
        }

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _database.ConnectionString,
                // Auto-login as the seeded `admin` user so authenticated endpoints
                // are reachable on GET requests without simulating a manual login.
                ["DevelopmentAutoLogin:Enabled"] = "true",
                ["DevelopmentAutoLogin:Username"] = "admin",
                // Flowable is not exercised by these tests, but the options binding
                // requires a section to exist.
                ["Flowable:BaseUrl"] = "http://localhost/flowable",
                // Default tests to authorization-off so appsettings.Development.json
                // (which a dev may have flipped on) doesn't change their semantics.
                // Tests that need enforcement opt in via extraConfig.
                ["Authorization:Enabled"] = "false",
                ["Authorization:Enforcement"] = "off",
                ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false",
                // Self-healing platform: silence detector hosted services
                // during tests. Tests that exercise detectors call
                // RunOnceAsync directly rather than relying on the loop, so
                // ticking in the background just adds noise and racing.
                ["SystemIssues:DetectorsEnabled"] = "false",
                // The remediation dispatcher is harmless without registered
                // remediators (Phase 1 has none), but turning it off here
                // keeps the host quieter and matches the detector switch.
                ["SystemIssues:RemediationEnabled"] = "false",
                // Projection framework: same rationale as the detectors.
                // Tests that exercise projections call ApplyAsync directly;
                // the retention janitor exposes RunOnceAsync. Letting the
                // loops run across many parallel test factories would burn
                // CPU and risk host crashes under xUnit parallelism.
                ["Projections:WorkerEnabled"] = "false",
                ["FlowableCache:RetentionEnabled"] = "false",
            };

            foreach (var (key, value) in _extraConfig)
            {
                settings[key] = value;
            }

            config.AddInMemoryCollection(settings);
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

            // Replace the live Dapr publisher with a recording one so endpoint
            // tests can assert on every audit event published. Phase 1 of the
            // audit-events plan introduced this fixture.
            services.RemoveAll<IAuditEventPublisher>();
            services.AddSingleton<IAuditEventPublisher>(_ => new RecordingAuditEventPublisher());
            services.RemoveAll<IRecordEventPublisher>();
            services.AddSingleton<IRecordEventPublisher>(_ => new RecordingRecordEventPublisher());
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}
