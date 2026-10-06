using Xunit;

namespace minver_versioning_sample.Tests;

public sealed class MinVerScenarioTests
{
    [Fact]
    public void Tag_driven_release_scenario()
    {
        using var repository = MinVerRepository.Create();
        AssertLocalVersion(repository, "1.0.0-preview");

        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        AssertLocalVersion(repository, "1.0.0-preview.1");
        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        AssertLocalVersion(repository, "1.0.0-preview.2");

        repository.Tag("v1.0.0-rc.1");
        AssertReleaseVersion(repository, "1.0.0-rc.1");
        Assert.Contains("v1.0.0-rc.1", repository.TagsAtHead());

        repository.MergePullRequest("fix/release-candidate", "fix: correct release candidate behavior");
        AssertLocalVersion(repository, "1.0.0-rc.1.1");

        // The stable tag goes on the validated commit. MinVer prefers the higher of
        // two tags on one commit, and a stable version outranks its own RC, so the
        // approved build is promoted without being rebuilt or renumbered.
        repository.Tag("v1.0.0-rc.2");
        AssertReleaseVersion(repository, "1.0.0-rc.2");
        repository.Tag("v1.0.0");
        AssertReleaseVersion(repository, "1.0.0");
        Assert.Contains("v1.0.0", repository.TagsAtHead());

        repository.StartNewVersionTrain("chore/prepare-1.1.0-preview", "1.1");
        repository.MergePullRequest("chore/prepare-1.1.0-preview", "chore: start 1.1.0 preview train");
        AssertLocalVersion(repository, "1.1.0-preview.1");
        repository.MergePullRequest("feature/add-authorization", "feat: add authorization");
        AssertLocalVersion(repository, "1.1.0-preview.2");
        repository.MergePullRequest("feature/add-reporting", "feat: add reporting");
        AssertLocalVersion(repository, "1.1.0-preview.3");
    }

    // MinVer does not read commit messages, unlike GitVersion. A feature after a
    // stable tag stays on the patch line until a tag or the minimum major/minor
    // option declares a new train, and a breaking marker changes nothing.
    [Fact]
    public void Commit_messages_do_not_start_a_new_train()
    {
        using var repository = MinVerRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v1.0.0");
        AssertReleaseVersion(repository, "1.0.0");

        repository.MergePullRequest("feature/add-auth", "feat!: add authentication");
        AssertLocalVersion(repository, "1.0.1-preview.1");
    }

    // The tag prefix option decides which tags are release tags. A tag without the
    // prefix is not a version tag at all, so MinVer ignores it and the build stays on
    // the preview train.
    [Fact]
    public void Tag_without_the_configured_prefix_is_ignored()
    {
        using var repository = MinVerRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        AssertLocalVersion(repository, "1.0.0-preview.1");

        repository.Tag("1.0.0");
        AssertLocalVersion(repository, "1.0.0-preview.1");
    }

    // The deleted version.env recorded a release intent and the CI wrapper rejected a
    // tag that did not match it. MinVer has no such intent: a well-formed tag with the
    // configured prefix is the version, and it is used exactly as written.
    [Fact]
    public void Any_tag_with_the_configured_prefix_is_honoured()
    {
        using var repository = MinVerRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");

        repository.Tag("v9.9.9");
        AssertReleaseVersion(repository, "9.9.9");
    }

    // MinVer follows the first parent when it walks history, so a merge commit
    // advances height by one no matter how many commits the pull request had.
    // This is the opposite of a plain commit-count calculator such as GitVersion.
    [Fact]
    public void Merge_commits_advance_height_once_per_pull_request()
    {
        using var repository = MinVerRepository.Create();
        AssertLocalVersion(repository, "1.0.0-preview");

        repository.MergePullRequestWithMergeCommit("feature/customer-export", "feat: add customer export", 5);
        AssertLocalVersion(repository, "1.0.0-preview.1");

        repository.MergePullRequestWithMergeCommit("feature/add-auth", "feat: add authentication", 5);
        AssertLocalVersion(repository, "1.0.0-preview.2");

        repository.Tag("v1.0.0-rc.1");
        AssertReleaseVersion(repository, "1.0.0-rc.1");

        repository.MergePullRequestWithMergeCommit("fix/release-candidate", "fix: correct release candidate behavior", 3);
        AssertLocalVersion(repository, "1.0.0-rc.1.1");
    }

    // The script reports the version MinVer calculated; it does not invent one. Both
    // sources are checked for preview versions, so a wrapper that mangles the value the
    // tool produced fails here.
    private static void AssertLocalVersion(MinVerRepository repository, string expectedVersion)
    {
        Assert.Equal(expectedVersion, repository.CalculateVersion());
        Assert.Equal(expectedVersion, repository.CalculateVersionWithMinVer());
    }

    // A release tag is the version on its own; the script passes it through unchanged.
    private static void AssertReleaseVersion(MinVerRepository repository, string expectedVersion) =>
        Assert.Equal(expectedVersion, repository.CalculateVersion());

    // Mirrors the other samples' CI build suffix: the wrapper appends the UTC date and
    // the workflow run number to preview and RC artifacts so every build is unique.
    // Stable versions stay clean, and the environment records the audience.
    [Fact]
    public void Ci_output_appends_date_and_revision_to_prereleases()
    {
        using var repository = MinVerRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");

        var preview = repository.CalculateCiOutput("refs/heads/main", "42");
        Assert.Matches(@"^1\.0\.0-preview\.1\.\d{5}\.42$", preview["version"]);
        Assert.Equal("1.0.0", preview["assembly_version"]);
        Assert.Contains("+", preview["informational_version"]);
        Assert.Equal("dev", preview["environment"]);

        repository.Tag("v1.0.0-rc.1");
        var candidate = repository.CalculateCiOutput("refs/tags/v1.0.0-rc.1", "43");
        Assert.Matches(@"^1\.0\.0-rc\.1\.\d{5}\.43$", candidate["version"]);
        Assert.Equal("staging", candidate["environment"]);

        repository.Tag("v1.0.0");
        var stable = repository.CalculateCiOutput("refs/tags/v1.0.0", "44");
        Assert.Equal("1.0.0", stable["version"]);
        Assert.Equal("production", stable["environment"]);
    }

    // An untagged commit after an RC tag keeps its native intermediate version, such as
    // 1.0.0-rc.1.1, and is an internal build rather than a staging release. Only the tag
    // promotes a build to the staging audience.
    [Fact]
    public void Untagged_release_candidate_fix_targets_the_dev_audience()
    {
        using var repository = MinVerRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v1.0.0-rc.1");
        repository.MergePullRequest("fix/release-candidate", "fix: correct release candidate behavior");

        var internalBuild = repository.CalculateCiOutput("refs/heads/main", "46");
        Assert.Matches(@"^1\.0\.0-rc\.1\.1\.\d{5}\.46$", internalBuild["version"]);
        Assert.Equal("dev", internalBuild["environment"]);
    }

    // Only rc and stable tags are release identities; any other tag ref is rejected
    // instead of silently producing a preview artifact for a tagged commit.
    [Fact]
    public void Unsupported_release_tag_is_rejected()
    {
        using var repository = MinVerRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v1.0.0-preview.5");

        Assert.Throws<InvalidOperationException>(() =>
            repository.CalculateCiOutput("refs/tags/v1.0.0-preview.5", "45")
        );
    }

    // The script owns the CLI options because minver-cli has no configuration file.
    // Both workflows must call the script so the strategy cannot drift between them.
    [Theory]
    [InlineData(".github/workflows/minver.yml")]
    [InlineData("minver-versioning-sample/.github/workflows/build-and-publish.yml")]
    public void Ci_workflow_calculates_through_the_script(string relativeWorkflowPath)
    {
        var workflow = File.ReadAllText(
            Path.Combine(MinVerRepository.RepositoryRoot, relativeWorkflowPath)
        );
        Assert.Contains("scripts/calculate-version.sh", workflow);
        Assert.DoesNotContain("minver\" -t", workflow);
        Assert.DoesNotContain("minver -t", workflow);
    }
}
