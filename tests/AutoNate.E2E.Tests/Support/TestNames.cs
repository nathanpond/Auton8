namespace AutoNate.E2E.Tests.Support;

/// <summary>
/// Helpers for generating unique names per test so concurrent or sequential
/// runs of the suite don't collide on UNIQUE columns (record-type short codes,
/// usernames, etc.). The ephemeral test DB is dropped + recreated each fixture
/// run, but tests within a single run share the DB — uniqueness is enforced
/// at the test level.
/// </summary>
internal static class TestNames
{
    /// <summary>
    /// Returns a short, lowercase, hex-only unique slug suitable for embedding
    /// in a short code (e.g. record-type ShortCode, username). 8 hex chars =
    /// 32 bits of randomness, plenty for a single test run.
    /// </summary>
    public static string ShortSlug() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Returns a longer name like "e2e-asset-3f8c1e29" — useful for
    /// human-readable entity names.
    /// </summary>
    public static string Prefixed(string prefix) => $"e2e-{prefix}-{ShortSlug()}";
}
