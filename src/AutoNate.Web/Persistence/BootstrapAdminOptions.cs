namespace AutoNate.Web.Persistence;

// The first administrator on an empty install.
//
// Before this existed there was no way to bootstrap a fresh deployment: the
// only account came from a hardcoded INSERT in
// infra/postgres/init/02-create-autonate-app-schema.sql that shipped
// `admin`/`admin` with its password hash *and* salt committed to the
// repository. That was ungated by environment, and
// Authorization:AssignSuperAdminToAllExistingUsers grants SuperAdmin to every
// existing row on first boot — so every deployment that ran the init script
// came up with a super-admin whose password was public.
//
// Removing that seed on its own would have made a clean database unloginable:
// there is no registration page, no setup wizard, and POST /api/users requires
// authentication. So the seed and the bootstrap are one change.
//
// Nothing is created unless BOTH Username and Password are supplied, and only
// while `local_users` is empty. There is deliberately no default password: an
// operator who configures nothing gets a loud startup message rather than a
// guessable account.
public sealed class BootstrapAdminOptions
{
    public const string SectionName = "Bootstrap";

    public string? AdminUsername { get; set; }

    public string? AdminPassword { get; set; }

    public string? AdminEmail { get; set; }

    // Only set deliberately — the test fixtures pin it so their seeded grants
    // keep referring to a known principal. Left unset in production, where a
    // fresh identifier is the right answer.
    public Guid? AdminUserId { get; set; }

    // Whether the created account also receives the SuperAdmin role.
    //
    // True is the only sensible production default — an unprivileged first
    // administrator can sign in and then be denied everything, which is the
    // lockout this bootstrap exists to prevent. It is separable because the
    // backend test factory needs the account to exist *without* privilege:
    // ~20 enforcement suites use this exact user as the principal they grant
    // one narrow permission to, and a SuperAdmin principal passes every
    // authorization assertion vacuously.
    public bool GrantSuperAdmin { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AdminUsername) && !string.IsNullOrWhiteSpace(AdminPassword);
}
