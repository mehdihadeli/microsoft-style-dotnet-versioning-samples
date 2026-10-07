using Xunit;

namespace semantic_release_versioning_sample.Tests;

public sealed class SemanticReleaseScenarioTests
{
    // The README scenario end to end: previews come from the commit analyzer on main, and
    // a tag on a main commit supplies the release candidate or stable identity with no
    // release branch and no committed release intent.
    [Fact]
    public void Tag_driven_release_scenario()
    {
        using var repository = SemanticReleaseRepository.Create();
        AssertBaselineVersion(repository);

        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        AssertPreviewVersion(repository, "1.0.0-preview.1");
        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        AssertPreviewVersion(repository, "1.0.0-preview.2");

        repository.Tag("v1.0.0-rc.1");
        AssertReleaseVersion(repository, "1.0.0-rc.1");
        Assert.Contains("v1.0.0-rc.1", repository.TagsAtHead());

        // semantic-release has no release-candidate channel on a single release branch, so
        // a fix after the candidate returns to the preview train and keeps counting
        // commits instead of staying on the rc line. Only the next tag makes a candidate.
        repository.MergePullRequest("fix/release-candidate", "fix: correct release candidate behavior");
        AssertPreviewVersion(repository, "1.0.0-preview.3");

        // The stable tag goes on the validated commit. The wrapper reads the tag at HEAD
        // directly, so the approved build is promoted without being rebuilt or renumbered.
        repository.Tag("v1.0.0-rc.2");
        AssertReleaseVersion(repository, "1.0.0-rc.2");
        repository.Tag("v1.0.0");
        AssertReleaseVersion(repository, "1.0.0");
        Assert.Contains("v1.0.0", repository.TagsAtHead());

        // The next train comes from the commit analyzer: a `feat:` after the stable tag
        // opens 1.1.0 with no release intent to edit and commit first.
        repository.MergePullRequest("feature/add-authorization", "feat: add authorization");
        AssertPreviewVersion(repository, "1.1.0-preview.1");
        repository.MergePullRequest("feature/add-reporting", "feat: add reporting");
        AssertPreviewVersion(repository, "1.1.0-preview.2");
        repository.MergePullRequest("feature/add-batch", "feat: add batch export");
        AssertPreviewVersion(repository, "1.1.0-preview.3");
    }

    // semantic-release reads commit messages, like GitVersion and unlike MinVer. A `feat:`
    // after a stable tag opens the next minor train, and a breaking marker opens the next
    // major line.
    [Fact]
    public void Commit_messages_start_a_new_train()
    {
        using var repository = SemanticReleaseRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v1.0.0");
        AssertReleaseVersion(repository, "1.0.0");

        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        AssertPreviewVersion(repository, "1.1.0-preview.1");

        repository.MergePullRequest(
            "feature/replace-api",
            "feat: replace the legacy API\n\nBREAKING CHANGE: the legacy API is removed"
        );
        AssertPreviewVersion(repository, "2.0.0-preview.2");
    }

    // commit-analyzer's default preset only reads the BREAKING CHANGE footer. A `feat!:`
    // header is not a breaking marker under that preset, and not a feature either, so the
    // commit declares no release at all and the shipped line is kept. This is the
    // opposite of GitVersion and MinVer, where the breaking marker is honoured wherever
    // it appears.
    [Fact]
    public void Bang_header_is_not_a_breaking_marker()
    {
        using var repository = SemanticReleaseRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v1.0.0");
        AssertReleaseVersion(repository, "1.0.0");

        repository.MergePullRequest("feature/replace-api", "feat!: replace the legacy API");
        Assert.Equal(string.Empty, repository.CalculateVersionWithSemanticRelease());
        Assert.Equal("1.0.0-preview.1", repository.CalculateVersion());
    }

    // semantic-release only reacts to release commits, which is the opposite of GitVersion
    // and MinVer, where every accepted pull request advances the version. A `chore:` after
    // a stable tag declares no release, so the wrapper keeps the shipped line and only the
    // preview ordinal moves.
    [Fact]
    public void Non_release_commits_declare_no_release()
    {
        using var repository = SemanticReleaseRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v1.0.0");
        AssertReleaseVersion(repository, "1.0.0");

        repository.MergePullRequest("chore/tidy", "chore: tidy the readme");
        Assert.Equal(string.Empty, repository.CalculateVersionWithSemanticRelease());
        Assert.Equal("1.0.0-preview.1", repository.CalculateVersion());
    }

    // The tagFormat option decides which tags are release tags. A tag without the `v`
    // prefix does not match it, so semantic-release ignores the tag and the build stays on
    // the preview train.
    [Fact]
    public void Tag_without_the_configured_prefix_is_ignored()
    {
        using var repository = SemanticReleaseRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        AssertPreviewVersion(repository, "1.0.0-preview.1");

        repository.Tag("1.0.0");
        AssertPreviewVersion(repository, "1.0.0-preview.1");
    }

    // The deleted release.env recorded a release intent and the CI wrapper rejected a tag
    // that did not match it. semantic-release has no such intent: a well-formed tag with
    // the configured prefix is the version, and it is used exactly as written.
    [Fact]
    public void Any_tag_with_the_configured_prefix_is_honoured()
    {
        using var repository = SemanticReleaseRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");

        repository.Tag("v9.9.9");
        AssertReleaseVersion(repository, "9.9.9");
    }

    // The preview ordinal counts every commit since the train baseline, so a merge commit
    // advances it by the branch commit count plus the merge commit itself. MinVer follows
    // the first parent and advances by one per pull request instead. This is why the
    // sample requires squash merges.
    [Fact]
    public void Merge_commits_advance_the_preview_number_by_every_commit()
    {
        using var repository = SemanticReleaseRepository.Create();
        AssertBaselineVersion(repository);

        repository.MergePullRequestWithMergeCommit("feature/customer-export", "feat: add customer export", 5);
        AssertPreviewVersion(repository, "1.0.0-preview.6");

        repository.MergePullRequestWithMergeCommit("feature/add-auth", "feat: add authentication", 5);
        AssertPreviewVersion(repository, "1.0.0-preview.12");

        // A release candidate is not a train, so the counter does not reset at the tag.
        repository.Tag("v1.0.0-rc.1");
        AssertReleaseVersion(repository, "1.0.0-rc.1");

        repository.MergePullRequestWithMergeCommit("fix/release-candidate", "fix: correct release candidate behavior", 3);
        AssertPreviewVersion(repository, "1.0.0-preview.16");
    }

    // Previews carry the UTC date and the workflow run number so every build is unique,
    // which mirrors the other samples' CI build suffix. A release that comes from a tag is
    // published exactly as tagged, with no suffix, and the environment records the
    // audience.
    [Fact]
    public void Ci_output_stamps_previews_but_not_tagged_releases()
    {
        using var repository = SemanticReleaseRepository.Create();

        // The initialization build has no release behind it and belongs to no audience.
        var baseline = repository.CalculateCiOutput("refs/heads/main", "40");
        Assert.Equal("1.0.0-preview.0", baseline["version"]);
        Assert.Equal("none", baseline["environment"]);

        repository.MergePullRequest("feature/customer-export", "feat: add customer export");

        var preview = repository.CalculateCiOutput("refs/heads/main", "42");
        Assert.Matches(@"^1\.0\.0-preview\.1\.\d{5}\.42$", preview["version"]);
        Assert.Equal("1.0.0", preview["assembly_version"]);
        Assert.Contains("+", preview["informational_version"]);
        Assert.Equal("dev", preview["environment"]);

        repository.Tag("v1.0.0-rc.1");
        var candidate = repository.CalculateCiOutput("refs/tags/v1.0.0-rc.1", "43");
        Assert.Equal("1.0.0-rc.1", candidate["version"]);
        Assert.Equal("staging", candidate["environment"]);

        repository.Tag("v1.0.0");
        var stable = repository.CalculateCiOutput("refs/tags/v1.0.0", "44");
        Assert.Equal("1.0.0", stable["version"]);
        Assert.Equal("production", stable["environment"]);
    }

    // An untagged commit after a release candidate returns to the preview train and is an
    // internal build rather than a staging release. Only the tag promotes a build to the
    // staging audience. The ordinal is the commit count, because no release-candidate tag
    // counts as a train baseline.
    [Fact]
    public void Untagged_release_candidate_fix_targets_the_dev_audience()
    {
        using var repository = SemanticReleaseRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v1.0.0-rc.1");
        repository.MergePullRequest("fix/release-candidate", "fix: correct release candidate behavior");

        var internalBuild = repository.CalculateCiOutput("refs/heads/main", "46");
        Assert.Matches(@"^1\.0\.0-preview\.2\.\d{5}\.46$", internalBuild["version"]);
        Assert.Equal("dev", internalBuild["environment"]);
    }

    // Only rc and stable tags are release identities; any other tag ref is rejected
    // instead of silently producing a preview artifact for a tagged commit.
    [Fact]
    public void Unsupported_release_tag_is_rejected()
    {
        using var repository = SemanticReleaseRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v1.0.0-preview.5");

        Assert.Throws<InvalidOperationException>(() =>
            repository.CalculateCiOutput("refs/tags/v1.0.0-preview.5", "45")
        );
    }

    // The script owns the strategy and the five workflow outputs, so the repository
    // workflow and the standalone sample workflow must both delegate to it instead of
    // calculating a version inline.
    [Theory]
    [InlineData(".github/workflows/semantic-release.yml")]
    [InlineData("semantic-release-versioning-sample/.github/workflows/build-and-publish.yml")]
    public void Ci_workflow_calculates_through_the_script(string relativeWorkflowPath)
    {
        var workflow = File.ReadAllText(
            Path.Combine(SemanticReleaseRepository.RepositoryRoot, relativeWorkflowPath)
        );

        Assert.Contains("scripts/calculate-version.sh", workflow);
        Assert.DoesNotContain("GITHUB_REF_TYPE", workflow);
        foreach (var outputName in new[] { "assembly_version", "informational_version", "environment", "commit" })
            Assert.Contains($"{outputName}:", workflow);
    }

    // Before any release commit semantic-release declares no release, so the wrapper keeps
    // semantic-release's first release of 1.0.0 and the preview ordinal is zero.
    private static void AssertBaselineVersion(SemanticReleaseRepository repository)
    {
        Assert.Equal(string.Empty, repository.CalculateVersionWithSemanticRelease());
        Assert.Equal("1.0.0-preview.0", repository.CalculateVersion());
    }

    // The script reports the version semantic-release calculated plus the Git-derived
    // preview ordinal. The base half must match the analyzer exactly, so a wrapper that
    // invents a version fails here.
    private static void AssertPreviewVersion(SemanticReleaseRepository repository, string expectedVersion)
    {
        Assert.Equal(expectedVersion, repository.CalculateVersion());
        var separator = expectedVersion.IndexOf("-preview.", StringComparison.Ordinal);
        Assert.Equal(expectedVersion[..separator], repository.CalculateVersionWithSemanticRelease());
    }

    // A release tag is the version on its own; the script passes it through unchanged.
    private static void AssertReleaseVersion(SemanticReleaseRepository repository, string expectedVersion) =>
        Assert.Equal(expectedVersion, repository.CalculateVersion());
}
