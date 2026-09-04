using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.SiteSettings;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Identity;

/// <summary>Which ways in are available, and why local is available.</summary>
public sealed record SignInMethods(
    bool Local,
    bool Oidc,
    bool Saml,
    bool LocalForcedByOverride,
    bool LocalKeptForReachability = false)
{
    /// <summary>Whether a federated method is available at all.</summary>
    public bool AnyFederated => Oidc || Saml;
}

/// <summary>The outcome of asking to change the configuration.</summary>
public sealed record SignInMethodChangeResult(bool Accepted, string? Refusal);

public interface ISignInMethodPolicy
{
    /// <summary>What is available right now, override included.</summary>
    Task<SignInMethods> GetAsync(CancellationToken ct);

    /// <summary>What is stored, ignoring the override — what the admin screen edits.</summary>
    Task<SignInMethods> GetStoredAsync(CancellationToken ct);

    /// <summary>
    /// Applies a desired configuration, or refuses it with a reason.
    /// </summary>
    Task<SignInMethodChangeResult> UpdateAsync(
        bool local, bool oidc, bool saml, Guid actorId, CancellationToken ct);

    /// <summary>True while the break-glass environment variable is set.</summary>
    bool OverrideActive { get; }
}

/// <summary>
/// Decides which sign-in methods are available, and refuses configurations
/// nobody could get back into.
/// </summary>
/// <remarks>
/// Turning local sign-in off is what an organisation standardised on SSO wants,
/// and it is also the first configuration in this product that can lock everyone
/// out: a misconfigured provider plus local disabled is an install nobody can
/// enter. Epic #38 forbids exactly that, so two guards ship with the switch.
///
/// <b>Prove federation first.</b> Local cannot be switched off until a federated
/// provider is enabled <em>and has recorded a successful sign-in</em>.
/// "Configured" is not "working" — the gap between them is precisely where an
/// install locks itself out, and an administrator toggling both in one sitting
/// has no way to know they are in it.
///
/// <b>Break glass.</b> <see cref="OverrideVariable"/> forces local sign-in on
/// regardless of what is stored, so an operator with host access can always get
/// back in. It is read from the environment rather than from configuration
/// binding on purpose: it must not be settable by anything that also lives in
/// the database this override exists to overrule.
///
/// One more thing this class is careful about: it is the *only* answer to "is
/// this method available?". The login page and every enforcement point ask it,
/// because a hidden button is not a disabled method — an endpoint that still
/// accepts a direct POST is still a way in.
/// </remarks>
public sealed class SignInMethodPolicy : ISignInMethodPolicy
{
    /// <summary>
    /// Forces local sign-in on whatever the database says.
    /// </summary>
    /// <remarks>
    /// Named after <c>AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR</c>, the closest
    /// existing escape hatch, and documented in docs/DEPLOYMENT.md with it.
    /// </remarks>
    public const string OverrideVariable = "AUTONATE_FORCE_LOCAL_SIGNIN";

    internal const string LocalKey = "signin.localEnabled";
    internal const string OidcKey = "signin.oidcEnabled";
    internal const string SamlKey = "signin.samlEnabled";

    private readonly ISiteSettingsStore _settings;
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly bool _override;

    public SignInMethodPolicy(
        ISiteSettingsStore settings,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IConfiguration configuration)
    {
        _settings = settings;
        _dbFactory = dbFactory;
        _override = IsOverrideSet(configuration);
    }

    public bool OverrideActive => _override;

    /// <summary>
    /// Reads the break-glass flag.
    /// </summary>
    /// <remarks>
    /// Anything but a clear negative counts as on. An operator setting this in
    /// an incident has typed something meaning "yes" — <c>1</c>, <c>true</c>,
    /// <c>yes</c> — and a strict parse that rejected their spelling would leave
    /// them locked out while believing they had fixed it. The asymmetry is
    /// deliberate: the failure mode of reading it too eagerly is a login form
    /// that should have been hidden, and the failure mode of reading it too
    /// strictly is an install nobody can enter.
    /// </remarks>
    internal static bool IsOverrideSet(IConfiguration configuration)
    {
        var raw = configuration[OverrideVariable] ?? Environment.GetEnvironmentVariable(OverrideVariable);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var value = raw.Trim();
        return !value.Equals("0", StringComparison.Ordinal)
            && !value.Equals("false", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("no", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("off", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SignInMethods> GetAsync(CancellationToken ct)
    {
        var stored = await GetStoredAsync(ct);

        if (_override)
        {
            return stored with { Local = true, LocalForcedByOverride = !stored.Local };
        }

        if (stored.Local) return stored;

        // Local is stored off. The write-time guard should have made that
        // impossible without a proven federated provider — but stored state can
        // arrive by routes the guard never saw: a settings restore without the
        // matching users, a provider deleted or switched off afterwards, a
        // direct edit. Re-checking here makes "there is always a way in" true by
        // construction rather than only at the moment somebody pressed save.
        //
        // This is also what keeps the first-administrator bootstrap honest: a
        // fresh database whose settings say local is off would otherwise create
        // an administrator who cannot sign in, which is exactly the lockout the
        // guards exist to prevent, arrived at from the other direction.
        if (await AnyProvenFederationAsync(stored, ct)) return stored;

        return stored with { Local = true, LocalKeptForReachability = true };
    }

    public async Task<SignInMethods> GetStoredAsync(CancellationToken ct)
    {
        var all = await _settings.GetAllAsync(ct);
        return new SignInMethods(
            Local: Read(all, LocalKey),
            Oidc: Read(all, OidcKey),
            Saml: Read(all, SamlKey),
            LocalForcedByOverride: false);
    }

    public async Task<SignInMethodChangeResult> UpdateAsync(
        bool local, bool oidc, bool saml, Guid actorId, CancellationToken ct)
    {
        var refusal = await ValidateAsync(local, oidc, saml, ct);
        if (refusal is not null) return new SignInMethodChangeResult(false, refusal);

        await _settings.ApplyUpdatesAsync(
            new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
            {
                [LocalKey] = Json(local),
                [OidcKey] = Json(oidc),
                [SamlKey] = Json(saml),
            },
            actorId,
            ct);

        return new SignInMethodChangeResult(true, null);
    }

    /// <summary>
    /// Refuses a configuration nobody could sign in to, naming what is missing.
    /// </summary>
    /// <remarks>
    /// Validated as a whole rather than one toggle at a time. "All three off"
    /// and "local off with nothing working to replace it" are the same mistake
    /// wearing different clothes, and checking a single field at a time cannot
    /// see either of them.
    /// </remarks>
    private async Task<string?> ValidateAsync(bool local, bool oidc, bool saml, CancellationToken ct)
    {
        if (!local && !oidc && !saml)
        {
            return "That would disable every way of signing in, leaving an install nobody can enter. "
                + "Leave at least one method enabled.";
        }

        if (local) return null;

        // Local is being switched off, so something else has to actually work.
        var usable = await UsableProvidersAsync(oidc, saml, ct);

        if (usable.Count == 0)
        {
            return "Local sign-in cannot be disabled while no federated provider is enabled — "
                + "there would be no way in. Enable an OIDC or SAML provider first, and sign in "
                + "through it once.";
        }

        var proven = usable.Where(p => p.LastSuccess is not null).ToList();
        if (proven.Count == 0)
        {
            var names = string.Join(", ", usable.Select(p => p.DisplayName).Order(StringComparer.Ordinal));
            return $"No federated provider has completed a sign-in yet ({names} "
                + $"{(usable.Count == 1 ? "is" : "are")} enabled but unproven). Sign in through one "
                + "at least once before disabling local sign-in — a provider that is configured is "
                + "not yet a provider that works, and the difference is an install nobody can enter.";
        }

        return null;
    }

    /// <summary>
    /// Enabled providers whose protocol is also enabled, with whether each has worked.
    /// </summary>
    private async Task<List<(string DisplayName, DateTime? LastSuccess)>> UsableProvidersAsync(
        bool oidc, bool saml, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var candidates = await db.IdentityProviders.AsNoTracking()
            .Where(p => p.IsEnabled)
            .Select(p => new { p.DisplayName, p.Kind, p.LastSuccessfulSignInAtUtc })
            .ToListAsync(ct);

        // Filtered against the *desired* protocol flags, not the stored ones. A
        // request that disables OIDC cannot count an OIDC provider as its
        // justification — a lockout arrived at by counting the wrong thing.
        return candidates
            .Where(p => p.Kind == IdentityProviderKinds.Oidc ? oidc : saml)
            .Select(p => (p.DisplayName, p.LastSuccessfulSignInAtUtc))
            .ToList();
    }

    private async Task<bool> AnyProvenFederationAsync(SignInMethods stored, CancellationToken ct)
    {
        var usable = await UsableProvidersAsync(stored.Oidc, stored.Saml, ct);
        return usable.Any(p => p.LastSuccess is not null);
    }

    private static bool Read(IReadOnlyDictionary<string, System.Text.Json.JsonElement> all, string key)
    {
        // Absent means enabled. An upgrade must not silently switch a method
        // off, and every install that predates this story had all three.
        if (!all.TryGetValue(key, out var value)) return true;
        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.True => true,
            _ => true,
        };
    }

    private static System.Text.Json.JsonElement Json(bool value) =>
        System.Text.Json.JsonDocument.Parse(value ? "true" : "false").RootElement.Clone();
}
