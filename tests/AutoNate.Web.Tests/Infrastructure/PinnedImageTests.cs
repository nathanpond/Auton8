using System.Text;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Every image this repository builds from or runs is pinned by content digest.
/// </summary>
/// <remarks>
/// A tag is a mutable pointer. `flowable/flowable-rest:latest` was the clearest
/// case — `flowable-extension/pom.xml` compiles against Flowable 8.0.0 and the
/// tag happened to resolve to 8.0.0, so they agreed by coincidence rather than
/// by construction. A tag that moved to a different major would have put a
/// compiled extension against an engine it was never built for, and the symptom
/// would have been a JVM linkage error at runtime rather than a failing build.
///
/// Even an exact-looking tag like `1.17.5` can be re-pushed. Digests cannot.
/// </remarks>
public sealed class PinnedImageTests
{
    private static string FixturePath(string name) =>
        Path.Combine(
            RepoRoot.Path, "tests", "AutoNate.Web.Tests", "Infrastructure", "ImageFixtures", name);

    [Fact]
    public void Discovery_finds_the_repositorys_dockerfiles()
    {
        var files = ImageReferenceScanner.DiscoverDockerfiles();

        // Vacuous-pass guard, same as the compose scanner's.
        Assert.NotEmpty(files);
        Assert.Contains(files, f => f.EndsWith("infra/flowable/Dockerfile", StringComparison.Ordinal));
        Assert.Contains(files, f => f.EndsWith("services/executor/Dockerfile", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_image_reference_in_the_repository_is_pinned_by_digest()
    {
        var violations = new List<string>();

        foreach (var dockerfile in ImageReferenceScanner.DiscoverDockerfiles())
        {
            var stages = ImageReferenceScanner.StageNames(dockerfile);
            foreach (var reference in ImageReferenceScanner.ScanDockerfile(dockerfile))
            {
                // `FROM build` refers to an earlier stage, not a registry image.
                if (stages.Contains(reference.Reference) || reference.IsExempt || reference.IsPinned)
                {
                    continue;
                }

                violations.Add($"{reference.File}:{reference.Line} FROM {reference.Reference}");
            }
        }

        foreach (var compose in ComposeFileScanner.DiscoverComposeFiles())
        {
            foreach (var reference in ImageReferenceScanner.ScanCompose(compose))
            {
                if (reference.IsExempt || reference.IsPinned)
                {
                    continue;
                }

                violations.Add($"{reference.File}:{reference.Line} image: {reference.Reference}");
            }
        }

        Assert.True(violations.Count == 0, BuildMessage(violations));
    }

    private static string BuildMessage(IReadOnlyList<string> violations)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{violations.Count} image reference(s) are not pinned by digest. A tag is a");
        sb.AppendLine("mutable pointer: the registry can move it, so the same commit stops building");
        sb.AppendLine("the same thing. Resolve one with:");
        sb.AppendLine();
        sb.AppendLine("  docker buildx imagetools inspect <image> | awk '/^Digest:/{print $2; exit}'");
        sb.AppendLine();
        sb.AppendLine("then write `name@sha256:...  # original:tag`, keeping the tag as a comment.");
        sb.AppendLine();
        foreach (var v in violations)
        {
            sb.AppendLine("  " + v);
        }

        return sb.ToString();
    }

    [Fact]
    public void No_FROM_line_carries_a_trailing_comment()
    {
        // Docker does not treat `#` as a comment mid-line. `FROM x AS y # tag`
        // parses as five tokens and fails the whole build with "FROM requires
        // either one or three arguments".
        //
        // This guard exists because pinning by digest (#52) shipped exactly
        // that mistake across three Dockerfiles: the tag was kept as a trailing
        // comment for readability, and none of them could be built afterwards.
        // It was invisible to `make infra-up`, which reported every service
        // healthy — because the images were already built and cached, so
        // nothing rebuilt. Keep the tag on its own line above the FROM.
        var violations = new List<string>();

        foreach (var dockerfile in ImageReferenceScanner.DiscoverDockerfiles())
        {
            var relative = Path.GetRelativePath(RepoRoot.Path, dockerfile)
                .Replace(Path.DirectorySeparatorChar, '/');
            var lines = File.ReadAllLines(dockerfile);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!line.TrimStart().StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (line.Contains('#', StringComparison.Ordinal))
                {
                    violations.Add($"{relative}:{i + 1} {line.Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "A FROM line carries a trailing `#` comment. Docker will refuse to build the file:\n  "
            + string.Join("\n  ", violations));
    }

    // ── Failure path ────────────────────────────────────────────────────────

    [Fact]
    public void A_bare_version_tag_is_a_violation()
    {
        var reference = Assert.Single(
            ImageReferenceScanner.ScanDockerfile(FixturePath("Dockerfile.floating")));

        Assert.Equal("node:24-alpine", reference.Reference);
        Assert.False(reference.IsPinned);
    }

    [Fact]
    public void A_latest_tag_is_a_violation()
    {
        var reference = Assert.Single(
            ImageReferenceScanner.ScanDockerfile(FixturePath("Dockerfile.latest")));

        Assert.Equal("flowable/flowable-rest:latest", reference.Reference);
        Assert.False(reference.IsPinned);
    }

    [Fact]
    public void A_floating_compose_image_is_a_violation()
    {
        var reference = Assert.Single(
            ImageReferenceScanner.ScanCompose(FixturePath("compose-floating.yml")));

        Assert.Equal("postgres:16-alpine", reference.Reference);
        Assert.False(reference.IsPinned);
    }

    [Fact]
    public void A_digest_pinned_reference_passes_and_scratch_is_exempt()
    {
        var references = ImageReferenceScanner.ScanDockerfile(FixturePath("Dockerfile.pinned"));

        Assert.Equal(2, references.Count);
        Assert.True(references[0].IsPinned);
        Assert.True(references[1].IsExempt);
    }

    [Fact]
    public void CI_builds_the_application_image_on_every_pull_request()
    {
        // Without this job the app image is built only when a release is cut,
        // so a broken Dockerfile surfaces at the worst possible moment. That is
        // not hypothetical — see No_FROM_line_carries_a_trailing_comment above,
        // where all three Dockerfiles were unbuildable and `make infra-up` still
        // reported nine healthy services because nothing rebuilt.
        var ci = File.ReadAllText(
            Path.Combine(RepoRoot.Path, ".github", "workflows", "ci.yml"));

        Assert.Contains("src/AutoNate.Web/Dockerfile", ci, StringComparison.Ordinal);

        // And it must actually run the thing, not just build it: an image that
        // builds and cannot serve is the failure this is meant to catch.
        Assert.Contains("/api/health/live", ci, StringComparison.Ordinal);
    }

    // ── Lock files ──────────────────────────────────────────────────────────

    [Fact]
    public void Every_project_has_a_committed_lock_file()
    {
        var root = RepoRoot.Path;
        var projects = new[] { "src", "tests", "plugins" }
            .Select(d => Path.Combine(root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.csproj", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(projects);

        var missing = projects
            .Where(p => !File.Exists(Path.Combine(Path.GetDirectoryName(p)!, "packages.lock.json")))
            .Select(p => Path.GetRelativePath(root, p))
            .ToList();

        Assert.True(missing.Count == 0,
            "These projects have no packages.lock.json, so their dependency graph is not locked "
            + "and CI's locked-mode restore does not cover them. Run `make lockfiles`:\n  "
            + string.Join("\n  ", missing));
    }
}
