using AutoNate.Web.Models;
using AutoNate.Web.Services.Agent.Skills;
using AutoNate.Web.Services.ExternalConnections;
using AutoNate.Web.Services.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The parts of #87 that are about what the system refuses to do.
/// </summary>
public sealed class IdentityProviderPolicyTests
{
    // ── The Development-only plain-http accommodation, both directions ──────
    //
    // #87 requires that an allowlisted host may be reached over plain http in
    // Development *and that the relaxation cannot be enabled in production*.
    // Both halves are tested, because only testing the permissive half would
    // leave the guarantee that matters unverified.

    private const string Host = "keycloak.local";

    private static ProviderBaseUrlPolicy PolicyFor(string environmentName)
    {
        var options = Options.Create(new ExternalConnectionUrlOptions
        {
            AllowedProviderHosts = new(StringComparer.OrdinalIgnoreCase)
            {
                [IdentityProviderConfigurationTester.OidcPolicyKind] = [Host],
                ["LlmProvider:OpenAI"] = [Host],
            },
        });

        return new ProviderBaseUrlPolicy(options, new StubEnvironment(environmentName));
    }

    [Fact]
    public void In_Development_an_allowlisted_identity_provider_host_may_use_plain_http()
    {
        var policy = PolicyFor("Development");
        var url = $"http://{Host}/realms/auton8/.well-known/openid-configuration";

        var resolved = policy.Resolve(IdentityProviderConfigurationTester.OidcPolicyKind, url, url);

        Assert.Equal(Uri.UriSchemeHttp, resolved.Scheme);
        Assert.Equal(Host, resolved.Host);
    }

    [Fact]
    public void Outside_Development_the_same_host_over_plain_http_is_refused()
    {
        // The half that matters. If this ever passes, an operator can be
        // talked into pointing production at an http IdP.
        var policy = PolicyFor("Production");
        var url = $"http://{Host}/realms/auton8/.well-known/openid-configuration";

        var ex = Assert.Throws<InvalidOperationException>(
            () => policy.Resolve(IdentityProviderConfigurationTester.OidcPolicyKind, url, url));

        Assert.Contains("https", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_relaxation_does_not_extend_to_other_provider_kinds_even_in_Development()
    {
        // Scoped deliberately: an LLM connection carries a live API key on
        // every chat turn, and must not travel in the clear because a
        // developer is running locally.
        var policy = PolicyFor("Development");
        var url = $"http://{Host}/v1";

        Assert.Throws<InvalidOperationException>(
            () => policy.Resolve("LlmProvider:OpenAI", url, url));
    }

    [Fact]
    public void A_host_that_is_not_allowlisted_is_refused_even_in_Development_over_https()
    {
        var policy = PolicyFor("Development");
        var url = "https://evil.example.com/.well-known/openid-configuration";

        var ex = Assert.Throws<InvalidOperationException>(
            () => policy.Resolve(IdentityProviderConfigurationTester.OidcPolicyKind, url, url));

        Assert.Contains("not an allowed endpoint", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── No mutating agent skill ─────────────────────────────────────────────

    [Fact]
    public async Task The_assistant_cannot_configure_identity_providers()
    {
        // #87: "The assistant does not gain the ability to configure identity
        // providers; a test asserts it, so the decision cannot be quietly
        // reversed."
        //
        // Asserted over the registered skills rather than over a list of file
        // names, so adding a skill that mutates providers fails here even if it
        // is called something innocuous.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        // Skills are scoped, so they need a scope rather than the root provider.
        using var scope = factory.Services.CreateScope();
        var skills = scope.ServiceProvider.GetServices<IAgentSkill>().ToList();
        Assert.NotEmpty(skills);

        var offenders = skills
            .Where(s => MentionsIdentityProviders(s.Name) || MentionsIdentityProviders(s.GetType().Name))
            .Select(s => s.GetType().Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "The assistant must not be able to configure identity providers. Found: "
            + string.Join(", ", offenders)
            + ". Identity configuration decides who can get into the system; that is an "
            + "administrator's decision made deliberately in the admin UI, not something an "
            + "agent should be able to reach through a prompt. If this is being added on "
            + "purpose, it needs a conversation and #87's acceptance criterion revisited.");

        // And no skill takes a dependency on the store, which is the other way
        // the capability could arrive.
        var storeDependents = skills
            .Where(s => s.GetType().GetConstructors()
                .Any(c => c.GetParameters().Any(p =>
                    p.ParameterType == typeof(IIdentityProviderStore))))
            .Select(s => s.GetType().Name)
            .ToList();

        Assert.True(
            storeDependents.Count == 0,
            "An agent skill takes IIdentityProviderStore as a dependency: "
            + string.Join(", ", storeDependents));
    }

    private static bool MentionsIdentityProviders(string value) =>
        value.Contains("identityprovider", StringComparison.OrdinalIgnoreCase)
        || value.Contains("identity_provider", StringComparison.OrdinalIgnoreCase);

    private sealed class StubEnvironment(string environmentName) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "AutoNate.Web.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
