using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Structural assertions on the `app` compose profile.
/// </summary>
/// <remarks>
/// CI hosts neither Flowable nor Dapr, so the end-to-end behaviour of this
/// profile is verified by hand (see #57's closing evidence) and cannot be
/// asserted here. What <i>can</i> be pinned is the wiring that took several
/// attempts to get right and would be easy to undo without noticing.
/// </remarks>
public sealed class ComposeAppProfileTests
{
    private static string Compose() =>
        File.ReadAllText(Path.Combine(RepoRoot.Path, "infra", "docker-compose.yml"));

    private static string Overlay() =>
        File.ReadAllText(Path.Combine(RepoRoot.Path, "infra", "docker-compose.app.yml"));

    [Fact]
    public void The_app_and_its_sidecar_are_behind_the_app_profile()
    {
        var compose = Compose();

        foreach (var service in new[] { "autonate-web:", "autonate-web-dapr:" })
        {
            var index = compose.IndexOf(service, StringComparison.Ordinal);
            Assert.True(index >= 0, $"{service} is missing from the compose file.");

            // The profile declaration must be inside this service's block, not
            // merely somewhere in the file.
            var block = compose[index..Math.Min(compose.Length, index + 900)];
            Assert.Contains("profiles:", block, StringComparison.Ordinal);
            Assert.Contains("- app", block, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_sidecar_waits_for_the_app_to_start_not_to_be_healthy()
    {
        // The deadlock this profile hit. The app refuses to start without a
        // reachable sidecar; if the sidecar waits for the app to be *healthy*,
        // neither ever starts. flowable-dapr can wait for health because
        // Flowable boots without its sidecar — this one cannot, and someone
        // "fixing an inconsistency" between the two would reintroduce it.
        var compose = Compose();
        var index = compose.IndexOf("autonate-web-dapr:", StringComparison.Ordinal);
        Assert.True(index >= 0);

        var block = compose[index..];

        // Scoped to the app's own dependency entry. A wider window catches the
        // sibling nats and redis entries, which correctly wait for health.
        Assert.Contains(
            """
                  autonate-web:
                    condition: service_started
            """,
            block,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            """
                  autonate-web:
                    condition: service_healthy
            """,
            block,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_entrypoint_waits_on_the_outbound_health_endpoint()
    {
        // /v1.0/healthz includes the *application's* health, so waiting on it
        // would deadlock — the app is what the entrypoint is about to start.
        // The outbound variant reports only that the sidecar can serve calls.
        var entrypoint = File.ReadAllText(
            Path.Combine(RepoRoot.Path, "src", "AutoNate.Web", "docker-entrypoint.sh"));

        Assert.Contains("/v1.0/healthz/outbound", entrypoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Callbacks_are_rewired_by_the_overlay_and_not_by_the_base_file()
    {
        // Both must move together. Rewiring only one leaves a silent failure:
        // nothing errors on the app side, because nothing ever reaches it.
        var overlay = Overlay();

        Assert.Contains("AUTONATE_FLOWABLE_EVENTS_CALLBACK_BASE_URL: http://autonate-web:8080",
            overlay, StringComparison.Ordinal);
        Assert.Contains("AUTONATE_WEB_URL: http://autonate-web:8080",
            overlay, StringComparison.Ordinal);

        // Without the overlay the base file must still address the host, or the
        // developer's `make app` loop breaks.
        var compose = Compose();
        Assert.Contains("host.docker.internal:5108", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void The_app_container_ships_no_bootstrap_credential()
    {
        // Nothing is seeded (project invariant 1). An unset bootstrap
        // username or password must stay unset rather than acquiring a
        // convenient default here.
        var compose = Compose();
        var index = compose.IndexOf("autonate-web:", StringComparison.Ordinal);
        var block = compose[index..Math.Min(compose.Length, index + 2600)];

        Assert.Contains("Bootstrap__AdminUsername: ${Bootstrap__AdminUsername:-}",
            block, StringComparison.Ordinal);
        Assert.Contains("Bootstrap__AdminPassword: ${Bootstrap__AdminPassword:-}",
            block, StringComparison.Ordinal);
    }

    [Fact]
    public void The_app_overrides_every_endpoint_that_defaults_to_localhost()
    {
        // Each of these was found by hitting it: the app has more than one
        // connection string, and the datastores initialiser carries its own.
        // A missing one does not warn — it fails to start.
        var compose = Compose();
        var index = compose.IndexOf("autonate-web:", StringComparison.Ordinal);
        var block = compose[index..Math.Min(compose.Length, index + 2600)];

        foreach (var key in new[]
                 {
                     "ConnectionStrings__Default",
                     "ConnectionStrings__Datastores",
                     "Nats__Url",
                     "Flowable__BaseUrl",
                 })
        {
            Assert.Contains(key, block, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR", block);
    }
}
