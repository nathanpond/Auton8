namespace AutoNate.Web.Authorization;

public sealed class AuthorizationOptions
{
    public const string SectionName = "Authorization";

    // Fail closed. Every deployment that does not say otherwise enforces
    // grants; `AuthorizationOptionsValidator` additionally refuses to start
    // outside Development unless these two are explicitly on, so an operator
    // cannot silently ship an open system by omitting configuration (archived-59).
    public bool Enabled { get; set; } = true;

    // One of AuthorizationEnforcement.{Off,ReadOnly,Full}. Compared with
    // ordinal equality on the hot path, so the validator rejects anything
    // outside that set — "Full" or a typo would otherwise read as
    // "not full" and quietly allow every instance write.
    public string Enforcement { get; set; } = AuthorizationEnforcement.Full;

    // One-shot backfill switch, consumed by DatabaseSchemaInitializer's
    // SuperAdminBackfillSql. It grants the built-in SuperAdmin role to the
    // local_users that exist the first time it runs, then records
    // `superadmin_backfill_v1` in auth_seed_state and never runs again —
    // users created afterwards get nothing from it.
    //
    // It stays true by default because it is the only startup path that
    // grants SuperAdmin to anyone: a greenfield install with this set to
    // false boots with no super admin at all and, under Enforcement=full,
    // cannot be administered. Turn it off once the first admin is seeded,
    // and keep it off before pointing a deployment at a database that
    // already holds other people's user rows.
    public bool AssignSuperAdminToAllExistingUsers { get; set; } = true;

    // When true and Enforcement is Full, write-path AuthorizeAsync logs would-be
    // denials at WARN level but returns Allow. Used as a 24-hour safety window
    // before flipping the lockdown switch in production.
    public bool DryRun { get; set; } = false;
}

public static class AuthorizationEnforcement
{
    public const string Off = "off";
    public const string ReadOnly = "read-only";
    public const string Full = "full";

    public static readonly string[] All = [Off, ReadOnly, Full];

    // Ordinal on purpose: the evaluator compares with `!=`, so a value that
    // differs only by case is a different value and must not be treated as
    // recognised here either.
    public static bool IsKnown(string? value) =>
        value is not null && Array.IndexOf(All, value) >= 0;
}
