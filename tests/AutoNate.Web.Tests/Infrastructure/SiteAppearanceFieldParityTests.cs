using System.Text.RegularExpressions;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Tests.Infrastructure;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// The site-appearance field set is declared in four places; this fails when
/// they stop agreeing.
/// </summary>
/// <remarks>
/// #88 names this as the trap, and the epic records that these have drifted
/// before: the backend <c>SiteAppearanceDto</c>, the SPA's
/// <c>types/siteAppearance.ts</c>, its defaults in <c>lib/siteAppearance.ts</c>,
/// and the admin screen. A field added to one and forgotten in another does not
/// fail anything — it silently does not round-trip, which is the worst kind of
/// bug to find later because the symptom is "my setting did not save" long after
/// the change that caused it.
///
/// The admin screen is deliberately <b>not</b> required to expose every field.
/// It says so itself: several ColorAdmin-era fields keep their stored values but
/// paint nothing, so surfacing them would be offering settings that do nothing.
/// What must agree is the contract — the DTO, the type, and the defaults.
/// </remarks>
public sealed class SiteAppearanceFieldParityTests
{
    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine(RepoRoot.Path, Path.Combine(segments)));

    /// <summary>Property names on the backend DTO, camel-cased to match the wire format.</summary>
    private static IReadOnlyList<string> BackendFields() =>
        typeof(SiteAppearanceDto).GetProperties()
            .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string> TypeScriptTypeFields()
    {
        var source = Read("src", "AutoNate.Spa", "src", "types", "siteAppearance.ts");
        var body = Between(source, "export type SiteAppearance = {", "};");
        return Regex.Matches(body, @"^\s*(\w+)\s*:", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string> DefaultFields()
    {
        var source = Read("src", "AutoNate.Spa", "src", "lib", "siteAppearance.ts");
        // The defaults object is the first `SiteAppearance = {` literal.
        var body = Between(source, "SiteAppearance = {", "};");
        return Regex.Matches(body, @"^\s*(\w+)\s*:", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"Could not find '{start}'. This test reads source text, so it "
                             + "breaks when the declaration is reshaped — fix the test, do not delete it.");
        from += start.Length;
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to >= 0, $"Could not find the end of the block starting at '{start}'.");
        return source[from..to];
    }

    [Fact]
    public void The_backend_dto_and_the_typescript_type_declare_the_same_fields()
    {
        var backend = BackendFields();
        var typescript = TypeScriptTypeFields();

        var missingInTs = backend.Except(typescript).ToList();
        var missingInBackend = typescript.Except(backend).ToList();

        Assert.True(
            missingInTs.Count == 0 && missingInBackend.Count == 0,
            Explain(missingInTs, "types/siteAppearance.ts", missingInBackend, "SiteAppearanceDto"));
    }

    [Fact]
    public void Every_declared_field_has_a_default()
    {
        // A field with no default is the one that comes back undefined and
        // renders as a blank heading or a broken image.
        var declared = TypeScriptTypeFields();
        var defaults = DefaultFields();

        var withoutDefault = declared.Except(defaults).ToList();
        var defaultedButUndeclared = defaults.Except(declared).ToList();

        Assert.True(
            withoutDefault.Count == 0 && defaultedButUndeclared.Count == 0,
            Explain(withoutDefault, "the defaults in lib/siteAppearance.ts",
                    defaultedButUndeclared, "the SiteAppearance type"));
    }

    [Fact]
    public void The_login_fields_the_signed_out_page_renders_exist_everywhere()
    {
        // Named explicitly rather than left to the set comparison above,
        // because these are the ones a signed-out visitor sees and #88 exists
        // for. If the general parity test is ever loosened, these still hold.
        foreach (var field in new[] { "logoImageUrl", "logoText", "logoIcon", "loginTagline", "loginCoverImageUrl" })
        {
            Assert.Contains(field, BackendFields());
            Assert.Contains(field, TypeScriptTypeFields());
            Assert.Contains(field, DefaultFields());
        }
    }

    private static string Explain(
        IReadOnlyList<string> missingA, string whereA,
        IReadOnlyList<string> missingB, string whereB)
    {
        var lines = new List<string>
        {
            "The site-appearance field set has drifted. These four declarations must agree, "
            + "and a field present in one but not another does not fail anything at runtime — "
            + "it silently stops round-tripping, and the symptom arrives much later as "
            + "\"my setting did not save\".",
        };
        if (missingA.Count > 0) lines.Add($"  Missing from {whereA}: {string.Join(", ", missingA)}");
        if (missingB.Count > 0) lines.Add($"  Missing from {whereB}: {string.Join(", ", missingB)}");
        return string.Join("\n", lines);
    }
}
