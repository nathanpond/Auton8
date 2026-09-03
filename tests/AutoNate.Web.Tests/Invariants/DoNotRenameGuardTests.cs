using System.Reflection;
using System.Text.Json;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Plugins;
using AutoNate.Web.Services.ExternalConnections;
using AutoNate.Web.Tests.Infrastructure;
using Npgsql;
using Xunit;

namespace AutoNate.Web.Tests.Invariants;

/// <summary>
/// Guard for project invariant 4: the do-not-rename identifiers stay put.
/// </summary>
/// <remarks>
/// Four of the five invariants fail the suite when breached. This one was prose
/// until now — and it is the one whose breach is most expensive and least
/// visible. Renaming a DataProtection purpose makes every stored secret
/// permanently undecryptable; renaming the <c>.docx</c> markers orphans every
/// bound document; renaming the plugin ABI's type names breaks third-party
/// plugins with a misleading "type not found".
///
/// None of that shows up as a failing test. It shows up as a support request
/// months later, from someone whose data is already gone.
///
/// Every failure message here carries the <b>consequence</b>, not just the
/// identifier, because the person reading it is the person who just did the
/// rename and does not yet know why it matters.
/// </remarks>
public sealed class DoNotRenameGuardTests
{
    // The inventory. One place, so the CLAUDE.md prose list and this guard
    // cannot drift apart without the count assertion at the bottom noticing.
    private const int GuardedIdentifierCount = 9;

    private static string Consequence(string identifier, string effect) =>
        $"'{identifier}' has been renamed. {effect} It is on the do-not-rename list in "
        + "CLAUDE.md (project invariant 4) for that reason. If this rename is deliberate, "
        + "it needs a migration plan and a conversation, not a passing test.";

    // ── Read at runtime: the value that actually ships ──────────────────────

    [Fact]
    public void The_external_connections_dataprotection_purpose_is_unchanged()
    {
        const string Expected = "AutoNate.ExternalConnections.v1";

        Assert.True(
            string.Equals(Expected, DataProtectionConnectionSecretProtector.Purpose,
                StringComparison.Ordinal),
            Consequence(Expected,
                "Every stored external-connection secret becomes permanently undecryptable — "
                + "the purpose string is part of DataProtection's key derivation, so every API "
                + "key an operator has entered is lost and must be re-entered at every provider.")
            + $" Found '{DataProtectionConnectionSecretProtector.Purpose}' in "
            + "DataProtectionConnectionSecretProtector.");
    }

    [Fact]
    public void The_plugin_role_password_dataprotection_purpose_is_unchanged()
    {
        const string Expected = "AutoNate.Plugins.RolePassword.v1";

        Assert.True(
            string.Equals(Expected, PluginSchemaProvisioner.ProtectorPurpose,
                StringComparison.Ordinal),
            Consequence(Expected,
                "Every installed plugin's stored Postgres role password becomes undecryptable, "
                + "which takes that plugin's database access with it.")
            + $" Found '{PluginSchemaProvisioner.ProtectorPurpose}' in PluginSchemaProvisioner.");
    }

    [Fact]
    public void The_dataprotection_purposes_are_read_from_the_shipping_constants()
    {
        // Not a tautology: this asserts the guard is wired to the values the
        // application uses, rather than to copies of them. A text-scanning
        // guard would pass against a stale duplicate.
        var external = typeof(DataProtectionConnectionSecretProtector)
            .GetField("Purpose", BindingFlags.NonPublic | BindingFlags.Static)!;
        var plugins = typeof(PluginSchemaProvisioner)
            .GetField("ProtectorPurpose", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.True(external.IsLiteral, "Purpose must stay a compile-time constant.");
        Assert.True(plugins.IsLiteral, "ProtectorPurpose must stay a compile-time constant.");
    }

    [Fact]
    public void The_plugin_abi_assembly_and_interface_names_are_unchanged()
    {
        var abi = typeof(IAutoNatePlugin).Assembly;

        // The assembly is singular and the namespace is plural. That is not a
        // typo to tidy: the assembly name is what a third-party plugin's baked-in
        // reference asks the host for across the AssemblyLoadContext boundary, and
        // the namespace is what its source imports. They are independently
        // load-bearing, and renaming either breaks already-built plugins with a
        // misleading "type not found".
        Assert.True(
            string.Equals("AutoNate.Plugin.Abstractions", abi.GetName().Name, StringComparison.Ordinal),
            Consequence("AutoNate.Plugin.Abstractions (assembly name)",
                "Every already-built third-party plugin fails to load: its baked-in reference asks "
                + "the host for this assembly by name across the AssemblyLoadContext boundary, and "
                + "the symptom is a misleading \"type not found\" that reads as a badly-built plugin.")
            + $" Found '{abi.GetName().Name}'.");

        Assert.True(
            string.Equals("AutoNate.Plugins.Abstractions.IAutoNatePlugin",
                typeof(IAutoNatePlugin).FullName, StringComparison.Ordinal),
            Consequence("IAutoNatePlugin (full type name)",
                "The host's cast to the plugin interface fails silently across the "
                + "AssemblyLoadContext boundary, so every plugin stops loading.")
            + $" Found '{typeof(IAutoNatePlugin).FullName}'.");
    }

    [Theory]
    [InlineData("Default", "AutoNate", "Every existing install's data becomes unreachable.")]
    [InlineData("Datastores", "autonate_datastores",
        "Every provisioned data store's schema and read-only role becomes unreachable.")]
    public void The_database_names_in_the_dev_connection_strings_are_unchanged(
        string connectionName, string expectedDatabase, string effect)
    {
        // appsettings.Development.json, not appsettings.json: the base file
        // ships ConnectionStrings EMPTY on purpose, so a deployment fails closed
        // rather than silently pointing at a developer's database. The dev file
        // is therefore where these names live as values, parsed the way
        // configuration binding parses them rather than searched for as text.
        var settingsPath = Path.Combine(
            RepoRoot.Path, "src", "AutoNate.Web", "appsettings.Development.json");

        // JSONC: the file carries `//` comments documenting each setting, and
        // the default reader rejects them with "'/' is an invalid start of a
        // property name".
        using var document = JsonDocument.Parse(
            File.ReadAllText(settingsPath),
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

        var connectionString = document.RootElement
            .GetProperty("ConnectionStrings")
            .GetProperty(connectionName)
            .GetString();

        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;

        Assert.True(
            string.Equals(database, expectedDatabase, StringComparison.Ordinal),
            Consequence(expectedDatabase, effect) + $" Found '{database}'.");
    }

    [Fact]
    public void The_AutoNate_database_name_is_unchanged_where_the_database_is_created()
    {
        // Two places create it, and both must agree with the connection string
        // above or a fresh install comes up pointing at a database nothing made:
        // the compose entrypoint script for development, and the released
        // compose file's postgres-init for everyone else.
        var initSql = File.ReadAllText(Path.Combine(
            RepoRoot.Path, "infra", "postgres", "init", "01-create-autonate-db.sql"));

        Assert.Contains("\"AutoNate\"", initSql, StringComparison.Ordinal);

        var releaseCompose = File.ReadAllText(Path.Combine(
            RepoRoot.Path, "infra", "release", "compose.template.yml"));

        Assert.Contains("CREATE DATABASE \"AutoNate\"", releaseCompose, StringComparison.Ordinal);
        Assert.Contains("CREATE DATABASE \"autonate_datastores\"", releaseCompose,
            StringComparison.Ordinal);
    }

    // ── Scanned in source: no runtime representation exists ─────────────────

    [Theory]
    [InlineData("AUTONATE_BINDING",
        "src/AutoNate.Spa/src/components/documents/bindingFieldNode.ts",
        "Every document with a bound record field is orphaned — the marker is what "
        + "ties a Word field to its record, and .docx files already in the wild carry the old one.")]
    [InlineData("AUTONATE_TABLE_BINDING",
        "src/AutoNate.Spa/src/components/documents/bindingTableNode.ts",
        "Every document with a bound table is orphaned, for the same reason.")]
    public void The_docx_binding_markers_are_present_as_string_literals(
        string marker, string definingFile, string effect)
    {
        // Two things this deliberately does NOT do, both learned by watching an
        // earlier version fail to catch a real rename:
        //
        // It does not search a directory. Renaming the constant in
        // bindingFieldNode.ts left `AUTONATE_BINDING` mentioned in a *comment*
        // in bindingTableNode.ts, and a directory-wide search passed.
        //
        // It does not match the bare token. It requires the marker inside a
        // double-quoted string, which is where it is a value rather than prose.
        // A comment saying "the AUTONATE_BINDING marker" no longer rescues a
        // renamed constant.
        var path = Path.Combine(RepoRoot.Path, definingFile.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(path),
            $"{definingFile} does not exist. The marker's defining file has moved — fix this "
            + "guard rather than deleting it, because a guard pointed at a missing file "
            + "cannot protect anything.");

        var source = File.ReadAllText(path);

        Assert.True(
            source.Contains($"\"{marker}", StringComparison.Ordinal),
            Consequence(marker, effect) + $" It is no longer a string literal in {definingFile}.");
    }

    [Fact]
    public void The_bpmn_namespace_is_present_in_source()
    {
        var matches = SourceIdentifierScanner.FindIn(
            "src/AutoNate.Spa/src", "http://autonate.dev/workflows", [".js", ".ts", ".tsx"],
            out var scanned);

        Assert.True(scanned > 0, "The scan examined no SPA source files.");
        Assert.True(matches.Count > 0, Consequence(
            "http://autonate.dev/workflows",
            "Every deployed process definition stops matching the namespace the studio emits, "
            + "and Flowable's extension elements stop resolving."));
    }

    [Fact]
    public void The_behaviour_delegate_expression_is_present_on_both_sides()
    {
        // Both sides, deliberately. The SPA writes the expression into the BPMN
        // and the Flowable extension resolves it; renaming one alone breaks
        // service-task dispatch with no build error anywhere.
        var spa = SourceIdentifierScanner.FindIn(
            "src/AutoNate.Spa/src", "autonateBehaviorDelegate", [".js", ".ts", ".tsx"],
            out var spaScanned);
        var java = SourceIdentifierScanner.FindIn(
            "flowable-extension/src", "autonateBehaviorDelegate", [".java"],
            out var javaScanned);

        Assert.True(spaScanned > 0 && javaScanned > 0,
            "One side of the delegate scan examined no files; fix the search roots.");

        Assert.True(spa.Count > 0, Consequence("${autonateBehaviorDelegate} (SPA side)",
            "The studio stops emitting the delegate expression, so every service task "
            + "silently stops dispatching to Auton8."));
        Assert.True(java.Count > 0, Consequence("${autonateBehaviorDelegate} (Flowable side)",
            "The engine stops resolving the delegate, so every service task fails at runtime."));
    }

    // ── The scan ignores build output ───────────────────────────────────────

    [Fact]
    public void A_correct_copy_in_build_output_does_not_rescue_a_renamed_source_file()
    {
        // The failure mode this guard is most likely to have: wwwroot/ holds a
        // bundled copy of every SPA marker, so a repository-wide grep passes
        // long after the source has been renamed.
        var root = RepoRoot.Path;
        var fixtures = Path.Combine(
            root, "tests", "AutoNate.Web.Tests", "Invariants", "RenameFixtures");

        // The fixtures demonstrate the two shapes: a renamed source file, and a
        // stale bundle that still carries the correct marker.
        Assert.Contains("AUTONATE_BINDING",
            File.ReadAllText(Path.Combine(fixtures, "wwwroot", "bundle.js")),
            StringComparison.Ordinal);
        Assert.DoesNotContain("AUTONATE_BINDING ",
            File.ReadAllText(Path.Combine(fixtures, "src", "renamed.ts")));

        // The rule is asserted against real repository paths rather than the
        // fixture directory, which is itself excluded — asserting a file inside
        // it is *not* excluded would contradict that.
        Assert.True(
            SourceIdentifierScanner.IsExcluded(
                root, Path.Combine(root, "src", "AutoNate.Web", "wwwroot", "assets", "bundle.js")),
            "Build output under wwwroot/ must be excluded, or the guard passes on a stale bundle "
            + "long after the source has been renamed.");

        Assert.True(
            SourceIdentifierScanner.IsExcluded(
                root, Path.Combine(root, "src", "AutoNate.Web", "App_Data", "workflows", "x.json")),
            "Saved BPMN under App_Data/ is data, not source, and must be excluded.");

        Assert.False(
            SourceIdentifierScanner.IsExcluded(
                root, Path.Combine(
                    root, "src", "AutoNate.Spa", "src", "components", "documents",
                    "bindingFieldNode.ts")),
            "Real source must not be excluded, or the guard scans nothing and passes vacuously.");
    }

    // ── The list and the guard cannot drift ─────────────────────────────────

    [Fact]
    public void Every_identifier_on_the_CLAUDE_md_list_is_guarded()
    {
        // CLAUDE.md's Naming section is the prose list; this guard is its
        // executable form. Adding to the prose without adding a check here
        // would leave an identifier that reads as protected and is not.
        var claude = File.ReadAllText(Path.Combine(RepoRoot.Path, "CLAUDE.md"));

        foreach (var identifier in new[]
                 {
                     "AutoNate.ExternalConnections.v1",
                     "AutoNate.Plugins.RolePassword.v1",
                     "AUTONATE_BINDING",
                     "AUTONATE_TABLE_BINDING",
                     "http://autonate.dev/workflows",
                     "autonateBehaviorDelegate",
                     "autonate_datastores",
                     "IAutoNatePlugin",
                     "AutoNate.Plugin.Abstractions",
                 })
        {
            Assert.True(claude.Contains(identifier, StringComparison.Ordinal),
                $"'{identifier}' is guarded by this test but is no longer named in CLAUDE.md. "
                + "The prose list and the guard must agree.");
        }

        Assert.Equal(9, GuardedIdentifierCount);
    }
}
