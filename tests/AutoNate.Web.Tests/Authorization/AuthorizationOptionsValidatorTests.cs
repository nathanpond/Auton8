using AutoNate.Web.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

// Regression coverage for #59: authorization used to default to
// Enabled=false / Enforcement=off with nothing validating it, so a deployment
// that simply omitted the keys ran with every permission check returning allow.
public sealed class AuthorizationOptionsValidatorTests
{
    private static AuthorizationOptionsValidator Production() => new(isDevelopment: false);

    private static AuthorizationOptionsValidator Development() => new(isDevelopment: true);

    [Fact]
    public void CodeDefaults_AreAcceptedOutsideDevelopment()
    {
        // The whole point of the fail-closed defaults: an operator who
        // configures nothing gets an enforcing system, not an open one.
        var result = Production().Validate(name: null, new AuthorizationOptions());

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void PreFixDefaults_AreRefusedOutsideDevelopment()
    {
        // Exactly the configuration this issue was filed about.
        var result = Production().Validate(name: null, new AuthorizationOptions
        {
            Enabled = false,
            Enforcement = AuthorizationEnforcement.Off,
        });

        Assert.True(result.Failed);
        Assert.Collection(
            result.Failures!,
            first => Assert.Contains("Authorization:Enabled must be true outside Development", first),
            second => Assert.Contains("Authorization:Enforcement must be \"full\"", second));
    }

    [Fact]
    public void Disabled_IsRefusedOutsideDevelopment()
    {
        var result = Production().Validate(name: null, new AuthorizationOptions { Enabled = false });

        Assert.True(result.Failed);
        Assert.Contains("Authorization:Enabled must be true outside Development", result.FailureMessage);
    }

    [Theory]
    [InlineData(AuthorizationEnforcement.Off)]
    [InlineData(AuthorizationEnforcement.ReadOnly)]
    public void NonFullEnforcement_IsRefusedOutsideDevelopment(string enforcement)
    {
        var result = Production().Validate(name: null, new AuthorizationOptions { Enforcement = enforcement });

        Assert.True(result.Failed);
        Assert.Contains("Authorization:Enforcement must be \"full\"", result.FailureMessage);
    }

    [Fact]
    public void FullyConfigured_IsAcceptedOutsideDevelopment()
    {
        var result = Production().Validate(name: null, new AuthorizationOptions
        {
            Enabled = true,
            Enforcement = AuthorizationEnforcement.Full,
        });

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    // DryRun and the SuperAdmin backfill are fail-open-ish but legitimate:
    // they warn at start-up (Program.cs) rather than blocking it, because the
    // backfill is the only thing that grants a greenfield install its first
    // admin and DryRun is the documented staged-rollout window.
    [Fact]
    public void DryRunAndSuperAdminBackfill_DoNotBlockStartup()
    {
        var result = Production().Validate(name: null, new AuthorizationOptions
        {
            DryRun = true,
            AssignSuperAdminToAllExistingUsers = true,
        });

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    // The evaluator compares Enforcement with ordinal equality, so a value
    // that is merely mis-cased or misspelt reads as "not full" and lets every
    // instance write through with nothing logged. Never intentional — refused
    // in Development too.
    [Theory]
    [InlineData("Full")]
    [InlineData("FULL")]
    [InlineData("ful")]
    [InlineData("readonly")]
    [InlineData("")]
    public void UnrecognisedEnforcement_IsRefusedInEveryEnvironment(string enforcement)
    {
        var options = new AuthorizationOptions { Enforcement = enforcement };

        var inProduction = Production().Validate(name: null, options);
        var inDevelopment = Development().Validate(name: null, options);

        Assert.True(inProduction.Failed);
        Assert.True(inDevelopment.Failed);
        Assert.Contains("Authorization:Enforcement must be one of", inDevelopment.FailureMessage);
    }

    [Theory]
    [InlineData(AuthorizationEnforcement.Off)]
    [InlineData(AuthorizationEnforcement.ReadOnly)]
    [InlineData(AuthorizationEnforcement.Full)]
    public void KnownEnforcementValues_AreAcceptedInDevelopment(string enforcement)
    {
        var result = Development().Validate(name: null, new AuthorizationOptions
        {
            Enabled = false,
            Enforcement = enforcement,
        });

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    // Registration is the half that actually protects production: a validator
    // nobody wired up would let every case above through.
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Validator_IsRegisteredOnTheHost()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        var validators = factory.Services.GetServices<IValidateOptions<AuthorizationOptions>>();

        Assert.Contains(validators, v => v is AuthorizationOptionsValidator);
    }
}
