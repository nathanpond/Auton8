using AutoNate.Web.Services.Identity;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The replay guard on its own, where time can be moved.
/// </summary>
/// <remarks>
/// <see cref="SamlSignInServiceTests"/> proves a replayed assertion is refused
/// end to end. These cover what that cannot reach without waiting in real time:
/// that a record outlives the assertion, that it does not outlive it forever,
/// and that two simultaneous presentations cannot both win.
/// </remarks>
public sealed class SamlReplayGuardTests
{
    /// <summary>A clock the test moves by hand.</summary>
    private sealed class TestClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static TestClock Clock() => new(new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_first_presentation_is_accepted()
    {
        var clock = Clock();
        var guard = new SamlReplayGuard(clock);

        Assert.True(guard.TryAdd("assertion-1", clock.GetUtcNow().AddMinutes(30).UtcDateTime));
    }

    [Fact]
    public void A_second_presentation_of_the_same_assertion_is_refused()
    {
        var clock = Clock();
        var guard = new SamlReplayGuard(clock);
        var expiry = clock.GetUtcNow().AddMinutes(30).UtcDateTime;

        Assert.True(guard.TryAdd("assertion-1", expiry));
        Assert.False(guard.TryAdd("assertion-1", expiry));
        Assert.True(guard.TryFind("assertion-1"));
    }

    [Fact]
    public void Different_assertions_do_not_shadow_each_other()
    {
        var clock = Clock();
        var guard = new SamlReplayGuard(clock);
        var expiry = clock.GetUtcNow().AddMinutes(30).UtcDateTime;

        Assert.True(guard.TryAdd("assertion-1", expiry));
        Assert.True(guard.TryAdd("assertion-2", expiry));
    }

    [Fact]
    public void A_record_lasts_as_long_as_the_assertion_it_guards()
    {
        var clock = Clock();
        var guard = new SamlReplayGuard(clock);

        guard.TryAdd("assertion-1", clock.GetUtcNow().AddMinutes(30).UtcDateTime);

        clock.Advance(TimeSpan.FromMinutes(29));

        // The window is still open, so the assertion could still be presented —
        // which is exactly when the guard has to remember it.
        Assert.False(guard.TryAdd("assertion-1", clock.GetUtcNow().AddMinutes(1).UtcDateTime));
    }

    [Fact]
    public void A_record_is_forgotten_once_the_assertion_could_no_longer_be_used()
    {
        var clock = Clock();
        var guard = new SamlReplayGuard(clock);

        guard.TryAdd("assertion-1", clock.GetUtcNow().AddMinutes(30).UtcDateTime);
        clock.Advance(TimeSpan.FromHours(2));

        // This is what keeps the store bounded without a cleanup job. Re-adding
        // is harmless: an assertion whose window closed two hours ago is refused
        // by the lifetime check long before the guard is consulted.
        Assert.False(guard.TryFind("assertion-1"));
        Assert.True(guard.TryAdd("assertion-1", clock.GetUtcNow().AddMinutes(30).UtcDateTime));
    }

    [Fact]
    public void An_already_expired_assertion_is_still_remembered_briefly()
    {
        var clock = Clock();
        var guard = new SamlReplayGuard(clock);

        // Expiry already in the past. Recording it for zero time would leave a
        // gap between the lifetime check and this guard that a replay could be
        // squeezed through, so a floor applies.
        Assert.True(guard.TryAdd("assertion-1", clock.GetUtcNow().AddMinutes(-1).UtcDateTime));
        Assert.True(guard.TryFind("assertion-1"));
        Assert.False(guard.TryAdd("assertion-1", clock.GetUtcNow().AddMinutes(-1).UtcDateTime));
    }

    [Fact]
    public void An_unspecified_kind_expiry_is_read_as_utc()
    {
        var clock = Clock();
        var guard = new SamlReplayGuard(clock);

        // SAML timestamps are UTC by definition but arrive with Kind
        // Unspecified. Read as local time on a machine east of Greenwich they
        // would land in the past, and the record would expire immediately.
        var unspecified = DateTime.SpecifyKind(
            clock.GetUtcNow().AddMinutes(30).UtcDateTime, DateTimeKind.Unspecified);

        Assert.True(guard.TryAdd("assertion-1", unspecified));
        clock.Advance(TimeSpan.FromMinutes(29));
        Assert.True(guard.TryFind("assertion-1"));
    }

    [Fact]
    public void Concurrent_presentations_of_one_assertion_produce_exactly_one_winner()
    {
        var clock = Clock();
        var guard = new SamlReplayGuard(clock);
        var expiry = clock.GetUtcNow().AddMinutes(30).UtcDateTime;

        // The race a replay guard exists to lose. Sending the same form twice
        // in parallel costs an attacker nothing, so a check-then-act that both
        // callers pass is the whole attack.
        var accepted = 0;
        Parallel.For(0, 64, _ =>
        {
            if (guard.TryAdd("assertion-1", expiry)) Interlocked.Increment(ref accepted);
        });

        Assert.Equal(1, accepted);
    }
}
