using AutoNate.E2E.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace AutoNate.E2E.Tests;

// Temporary: trace exactly what /api/record-types returns for a freshly-
// granted limited user. Delete after Phase 10 is stable.
public sealed class _GrantDiagnostic : E2ETestBase
{
    private readonly ITestOutputHelper _out;
    public _GrantDiagnostic(AutoNateE2EFixture fixture, ITestOutputHelper output) : base(fixture)
        => _out = output;

    [Fact]
    public async Task TraceRecordTypeFilter()
    {
        // Admin: seed a record type and create a limited user, then grant
        // recordtype:view to the limited user.
        await using var adminSession = await NewSignedInAsAdminAsync();
        var adminSeeder = new ApiSeeder(adminSession.Page.APIRequest);

        var seededType = await adminSeeder.CreateRecordTypeAsync(
            TestNames.ShortCode(), TestNames.Prefixed("diag"));
        _out.WriteLine($"seeded type: id={seededType.Id} shortCode={seededType.ShortCode}");

        var username = $"e2e_diag_{TestNames.ShortSlug()}";
        var user = await adminSeeder.CreateUserAsync(username, "P@ssword123!");
        _out.WriteLine($"created user: id={user.UserId} username={user.Username}");

        await adminSeeder.GrantAsync(
            principalKind: "user",
            principalId: user.UserId,
            action: "view",
            selectorString: "/recordtype/*");
        _out.WriteLine("grant: user/view /recordtype/* allow");

        // Admin's own query as a baseline — admin has SuperAdmin so this
        // should always include the seeded type.
        var adminRecordTypes = await adminSession.Page.APIRequest.GetAsync("/api/record-types/?includeArchived=false");
        _out.WriteLine($"admin GET status={adminRecordTypes.Status}");
        _out.WriteLine($"  body (first 500): {Trim(await adminRecordTypes.TextAsync(), 500)}");

        // Limited user sign-in + same query.
        await using var limitedContext = await Fixture.NewContextAsync();
        var limitedPage = await limitedContext.NewPageAsync();
        await AutoNateE2EFixture.SignInAsync(limitedPage, username, "P@ssword123!");

        var limitedMe = await limitedPage.APIRequest.GetAsync("/api/auth/me");
        _out.WriteLine($"limited /api/auth/me: {Trim(await limitedMe.TextAsync(), 300)}");

        var limitedRecordTypes = await limitedPage.APIRequest.GetAsync("/api/record-types/?includeArchived=false");
        _out.WriteLine($"limited GET /api/record-types/ status={limitedRecordTypes.Status}");
        _out.WriteLine($"  body (first 800): {Trim(await limitedRecordTypes.TextAsync(), 800)}");
    }

    private static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
