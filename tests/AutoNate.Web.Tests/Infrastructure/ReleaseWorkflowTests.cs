using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Shape assertions on the release workflow.
/// </summary>
/// <remarks>
/// A release workflow is proven by running it, not by unit tests — but the
/// properties below are the ones a later edit could widen without anyone
/// noticing, and they are exactly the ones that matter: permissions, tag
/// policy, and whether a release can be cancelled mid-flight.
/// </remarks>
public sealed class ReleaseWorkflowTests
{
    private static string Workflow() =>
        File.ReadAllText(Path.Combine(RepoRoot.Path, ".github", "workflows", "release.yml"));

    [Fact]
    public void A_release_is_never_cancelled_in_flight()
    {
        // The opposite of ci.yml, deliberately. A superseded PR build is
        // waste; a half-published set of images is worse than a slow one.
        Assert.Contains("cancel-in-progress: false", Workflow(), StringComparison.Ordinal);
    }

    [Fact]
    public void Permissions_are_least_privilege_and_declared_per_job()
    {
        var workflow = Workflow();

        // The publish job needs exactly these three beyond read.
        Assert.Contains("packages: write", workflow, StringComparison.Ordinal);
        Assert.Contains("id-token: write", workflow, StringComparison.Ordinal);
        Assert.Contains("attestations: write", workflow, StringComparison.Ordinal);

        // The workflow default must stay read-only, so a job that declares
        // nothing cannot write anything.
        Assert.Contains("permissions:\n  contents: read", workflow, StringComparison.Ordinal);

        // `contents: write` is legitimate — the assets job attaches files to
        // the release — but only there. This assertion originally forbade it
        // outright and failed the moment that job was added, which was the
        // right failure: widening a permission should have to be deliberate.
        // So it is scoped instead of dropped.
        var publishJob = workflow[workflow.IndexOf("  publish:", StringComparison.Ordinal)
                                  ..workflow.IndexOf("  assets:", StringComparison.Ordinal)];
        Assert.DoesNotContain("contents: write", publishJob);

        var assetsJob = workflow[workflow.IndexOf("  assets:", StringComparison.Ordinal)..];
        Assert.DoesNotContain("packages: write", assetsJob);
        Assert.DoesNotContain("id-token: write", assetsJob);
    }

    [Fact]
    public void All_four_images_are_published()
    {
        var workflow = Workflow();

        foreach (var image in new[] { "autonate-web", "hocuspocus", "executor", "flowable" })
        {
            Assert.Contains($"image: {image}", workflow, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Both_architectures_are_built()
    {
        Assert.Contains("platforms: linux/amd64,linux/arm64", Workflow(), StringComparison.Ordinal);
    }

    [Fact]
    public void No_moving_tag_is_published()
    {
        // Decided in planning: exact version only. A 0.x project makes no
        // upgrade-compatibility promise, so a `latest` or floating major would
        // let a routine `docker compose pull` carry an unpinned deployment
        // across a breaking change.
        var workflow = Workflow();

        Assert.DoesNotContain("type=semver", workflow);
        Assert.DoesNotContain("value=latest", workflow);
        Assert.DoesNotContain("latest=true", workflow);
    }

    [Fact]
    public void The_tag_is_validated_against_the_product_version_before_anything_publishes()
    {
        var workflow = Workflow();

        Assert.Contains("Directory.Build.props", workflow, StringComparison.Ordinal);

        // The validate job must gate the publish job, or the check runs
        // alongside the thing it is supposed to prevent.
        Assert.Contains("needs: validate", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Image_references_are_lowercased()
    {
        // OCI repository names must be lowercase and this repository is
        // `nathanpond/Auton8`. docker/metadata-action lowercases silently, so
        // the push succeeds while anything using the raw github.repository
        // fails — which is what happened on the first release run: four images
        // published and four verifications failed with "repository name must
        // be lowercase". Nothing downstream of the push may use the raw value.
        var workflow = Workflow();

        Assert.Contains("tr '[:upper:]' '[:lower:]'", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("${{ github.repository }}/", workflow);
        Assert.DoesNotContain("${GITHUB_REPOSITORY}/", workflow);
    }

    [Fact]
    public void The_attestation_is_verified_in_the_same_run()
    {
        // An attestation that does not verify is worth less than none: it
        // invites trust it cannot support, and finding that out from a user is
        // the wrong way round.
        Assert.Contains("gh attestation verify", Workflow(), StringComparison.Ordinal);
    }
}
