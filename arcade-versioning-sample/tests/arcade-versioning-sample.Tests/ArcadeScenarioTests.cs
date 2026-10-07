using Xunit;

namespace arcade_versioning_sample.Tests;

public sealed class ArcadeScenarioTests
{
    [Fact]
    public void Tag_driven_release_scenario()
    {
        using var repository = ArcadeRepository.Create();
        AssertLocalVersion(repository, "1.0.0-preview.0.26051.7");

        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        AssertLocalVersion(repository, "1.0.0-preview.1.26051.7");
        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        AssertLocalVersion(repository, "1.0.0-preview.2.26051.7");

        // Arcade records release intent in eng/Versions.props, so hardening a candidate is a
        // reviewed edit and a merge rather than a helper script that rewrites the properties.
        repository.PrepareVersion("chore/prepare-1.0.0-rc", "1.0.0", "rc");
        repository.MergePullRequest("chore/prepare-1.0.0-rc", "chore: prepare 1.0.0 release candidate");
        AssertLocalVersion(repository, "1.0.0-rc.1.26051.7");

        repository.Tag("v1.0.0-rc.1");
        AssertReleaseVersion(repository, "1.0.0-rc.1");
        Assert.Contains("v1.0.0-rc.1", repository.TagsAtHead());

        repository.MergePullRequest("fix/release-candidate", "fix: correct release candidate behavior");
        AssertLocalVersion(repository, "1.0.0-rc.2.26051.7");

        // The stable tag goes on the validated commit. Arcade drops the suffix through
        // DotNetFinalVersionKind, so the approved build is promoted without being rebuilt.
        repository.Tag("v1.0.0-rc.2");
        AssertReleaseVersion(repository, "1.0.0-rc.2");
        repository.Tag("v1.0.0");
        AssertReleaseVersion(repository, "1.0.0");
        Assert.Contains("v1.0.0", repository.TagsAtHead());

        repository.PrepareVersion("chore/prepare-1.1.0-preview", "1.1.0", "preview");
        repository.MergePullRequest("chore/prepare-1.1.0-preview", "chore: start 1.1.0 preview train");
        AssertLocalVersion(repository, "1.1.0-preview.1.26051.7");
        repository.MergePullRequest("feature/add-authorization", "feat: add authorization");
        AssertLocalVersion(repository, "1.1.0-preview.2.26051.7");
        repository.MergePullRequest("feature/add-reporting", "feat: add reporting");
        AssertLocalVersion(repository, "1.1.0-preview.3.26051.7");
    }

    // Arcade reads no commit messages and derives no train from Git, unlike GitVersion. Only an
    // edit to eng/Versions.props changes the version prefix, so a breaking marker changes
    // nothing and the preview number keeps counting on the existing train.
    [Fact]
    public void Release_intent_comes_from_Versions_props_not_commit_messages()
    {
        using var repository = ArcadeRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v1.0.0");
        AssertReleaseVersion(repository, "1.0.0");

        repository.MergePullRequest("feature/add-auth", "feat!: add authentication");
        AssertLocalVersion(repository, "1.0.0-preview.2.26051.7");
    }

    // The adapter only recognises the v-prefixed release vocabulary. A tag without the prefix is
    // not a release identity, so the build stays on the preview train.
    [Fact]
    public void Tag_without_the_configured_prefix_is_ignored()
    {
        using var repository = ArcadeRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        AssertLocalVersion(repository, "1.0.0-preview.1.26051.7");

        repository.Tag("1.0.0");
        AssertLocalVersion(repository, "1.0.0-preview.1.26051.7");
    }

    // The committed VersionPrefix is the train, so a release tag that names a different one is
    // rejected instead of silently publishing a version nobody reviewed.
    [Fact]
    public void Mismatched_release_tag_is_rejected()
    {
        using var repository = ArcadeRepository.Create();
        repository.Tag("v9.9.9");

        Assert.Throws<InvalidOperationException>(() => repository.CalculateVersion());
    }

    // The adapter never formats a build suffix. Arcade derives SHORT_DATE and the revision from
    // OfficialBuildId, so changing that single input changes the stamped version.
    [Fact]
    public void Arcade_derives_the_build_suffix_from_the_official_build_id()
    {
        using var repository = ArcadeRepository.Create();

        Assert.Equal("1.0.0-preview.0.26051.7", repository.CalculateVersionWithArcade("20260101.7"));
        Assert.Equal("1.0.0-preview.0.26052.9", repository.CalculateVersionWithArcade("20260102.9"));
    }

    // A preview is not identified by a tag, so it carries Arcade's SHORT_DATE.revision stamp and
    // needs no regression test of its own. A tagged release is the version on its own and must
    // not be stamped, which is what this asserts.
    [Fact]
    public void Ci_output_stamps_previews_but_not_tagged_releases()
    {
        using var repository = ArcadeRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");

        var preview = repository.CalculateCiOutput("refs/heads/main", "42");
        Assert.Matches(@"^1\.0\.0-preview\.1\.\d{5}\.42$", preview["version"]);
        Assert.Equal("1.0.0", preview["assembly_version"]);
        Assert.Contains("+", preview["informational_version"]);
        Assert.Equal("dev", preview["environment"]);

        repository.PrepareVersion("chore/prepare-1.0.0-rc", "1.0.0", "rc");
        repository.MergePullRequest("chore/prepare-1.0.0-rc", "chore: prepare 1.0.0 release candidate");
        repository.Tag("v1.0.0-rc.1");
        var candidate = repository.CalculateCiOutput("refs/tags/v1.0.0-rc.1", "43");
        Assert.Equal("1.0.0-rc.1", candidate["version"]);
        Assert.Equal("1.0.0", candidate["assembly_version"]);
        Assert.Equal("staging", candidate["environment"]);

        repository.Tag("v1.0.0");
        var stable = repository.CalculateCiOutput("refs/tags/v1.0.0", "44");
        Assert.Equal("1.0.0", stable["version"]);
        Assert.Equal("production", stable["environment"]);
    }

    // An untagged commit after an RC tag keeps the committed rc intent and takes the next RC
    // ordinal, and it is an internal build rather than a staging release.
    [Fact]
    public void Untagged_commit_after_a_release_candidate_targets_the_dev_audience()
    {
        using var repository = ArcadeRepository.Create();
        repository.PrepareVersion("chore/prepare-1.0.0-rc", "1.0.0", "rc");
        repository.MergePullRequest("chore/prepare-1.0.0-rc", "chore: prepare 1.0.0 release candidate");
        repository.Tag("v1.0.0-rc.1");
        repository.MergePullRequest("fix/release-candidate", "fix: correct release candidate behavior");

        var internalBuild = repository.CalculateCiOutput("refs/heads/main", "46");
        Assert.Matches(@"^1\.0\.0-rc\.2\.\d{5}\.46$", internalBuild["version"]);
        Assert.Equal("dev", internalBuild["environment"]);
    }

    // Only v-prefixed RC and stable tags are release identities; any other tag ref is rejected
    // instead of silently producing a preview artifact for a tagged commit.
    [Fact]
    public void Unsupported_release_tag_is_rejected()
    {
        using var repository = ArcadeRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v1.0.0-preview.5");

        Assert.Throws<InvalidOperationException>(() =>
            repository.CalculateCiOutput("refs/tags/v1.0.0-preview.5", "45")
        );
    }

    // Arcade counts commits rather than following first parents, so a merge commit that brings a
    // branch's commits onto main advances the preview number by all of them. This is the opposite
    // of MinVer's first-parent height, and it is why the workflow requires squash merges.
    [Fact]
    public void Merge_commits_advance_the_preview_number_by_every_commit()
    {
        using var repository = ArcadeRepository.Create();
        AssertLocalVersion(repository, "1.0.0-preview.0.26051.7");

        repository.MergePullRequestWithMergeCommit("feature/customer-export", "feat: add customer export", 5);
        AssertLocalVersion(repository, "1.0.0-preview.6.26051.7");

        repository.MergePullRequestWithMergeCommit("feature/add-auth", "feat: add authentication", 5);
        AssertLocalVersion(repository, "1.0.0-preview.12.26051.7");
    }

    // The adapter owns the Arcade inputs because they have to come from Git. Both workflows must
    // call it so the strategy cannot drift, and neither may reimplement the calculation inline.
    [Theory]
    [InlineData(".github/workflows/arcade.yml")]
    [InlineData("arcade-versioning-sample/.github/workflows/build-and-publish.yml")]
    public void Ci_workflow_calculates_through_the_script(string relativeWorkflowPath)
    {
        var workflow = File.ReadAllText(
            Path.Combine(ArcadeRepository.RepositoryRoot, relativeWorkflowPath)
        );
        Assert.Contains("scripts/calculate-version.sh", workflow);
        Assert.DoesNotContain("GITHUB_REF_TYPE", workflow);
        Assert.DoesNotContain("GITHUB_RUN_NUMBER}", workflow);
    }

    // The adapter reports the version Arcade calculated; it does not invent one. Both sources are
    // checked, so an adapter that mangles the value the SDK produced fails here.
    private static void AssertLocalVersion(ArcadeRepository repository, string expectedVersion)
    {
        Assert.Equal(expectedVersion, repository.CalculateVersion());
        Assert.Equal(expectedVersion, repository.CalculateVersionWithArcade());
    }

    // A release tag is the version on its own and is used exactly as written, so it carries no
    // SHORT_DATE.revision stamp. Arcade still runs the official build.
    private static void AssertReleaseVersion(ArcadeRepository repository, string expectedVersion)
    {
        Assert.Equal(expectedVersion, repository.CalculateVersion());
        Assert.Equal(expectedVersion, repository.CalculateVersionWithArcade());
    }
}
