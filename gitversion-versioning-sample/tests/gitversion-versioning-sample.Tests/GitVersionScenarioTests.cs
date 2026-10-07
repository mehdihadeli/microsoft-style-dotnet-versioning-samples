using Xunit;

namespace gitversion_versioning_sample.Tests;

public sealed class GitVersionScenarioTests
{
    // The README scenario end to end: previews come from GitVersion on main, and a tag on
    // a main commit supplies the RC or stable identity with no release branch.
    [Fact]
    public void Tag_driven_release_scenario()
    {
        using var repository = GitVersionRepository.Create();
        AssertLocalVersion(repository, "1.0.0-preview.0");

        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        AssertLocalVersion(repository, "1.0.0-preview.1");
        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        AssertLocalVersion(repository, "1.0.0-preview.2");

        repository.Tag("v1.0.0-rc.1");
        AssertReleaseVersion(repository, "1.0.0-rc.1");
        Assert.Contains("v1.0.0-rc.1", repository.TagsAtHead());

        // GitVersion never lets an RC tag's label win on a main branch, so a fix after
        // the RC returns to the preview train and keeps counting commits instead of
        // staying on the RC line. Only the next tag makes a release candidate.
        repository.MergePullRequest(
            "fix/release-candidate",
            "fix: correct release candidate behavior"
        );
        AssertLocalVersion(repository, "1.0.0-preview.3");

        // The stable tag goes on the validated commit. The wrapper reads the tag at HEAD
        // directly, so the approved build is promoted without being rebuilt or renumbered.
        repository.Tag("v1.0.0-rc.2");
        AssertReleaseVersion(repository, "1.0.0-rc.2");
        repository.Tag("v1.0.0");
        AssertReleaseVersion(repository, "1.0.0");
        Assert.Contains("v1.0.0", repository.TagsAtHead());

        // GitVersion derives the next train from commit messages, unlike MinVer, which
        // needs the minimum major/minor option edited in the script. No script change is
        // needed here: the `feat:` commits open 1.1.0 on their own.
        repository.MergePullRequest("feature/add-authorization", "feat: add authorization");
        AssertLocalVersion(repository, "1.1.0-preview.1");
        repository.MergePullRequest("feature/add-reporting", "feat: add reporting");
        AssertLocalVersion(repository, "1.1.0-preview.2");
        repository.MergePullRequest("feature/add-more", "feat: add more");
        AssertLocalVersion(repository, "1.1.0-preview.3");
    }

    // GitVersion reads commit messages, the opposite of MinVer. A `feat:` after a stable
    // tag opens the next minor train rather than staying on the patch line, and a
    // breaking-change marker opens the next major line.
    [Fact]
    public void Commit_messages_start_a_new_train()
    {
        using var repository = GitVersionRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v1.0.0");
        AssertReleaseVersion(repository, "1.0.0");

        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        AssertLocalVersion(repository, "1.1.0-preview.1");

        repository.MergePullRequest("feature/add-breaking", "feat!: break authentication");
        AssertLocalVersion(repository, "2.0.0-preview.2");
    }

    // The tag-prefix option is `[vV]?`, so the prefix is optional. This is the opposite
    // of MinVer's `-t v`, where a tag without the prefix is not a version tag at all and
    // is ignored. Here the bare tag is a base version and becomes the version. The
    // release-tag filter in the script matches lowercase `v` only, so CI rejects that ref
    // instead of publishing an unclassified artifact.
    [Fact]
    public void Tag_without_the_configured_prefix_is_honoured()
    {
        using var repository = GitVersionRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        AssertLocalVersion(repository, "1.0.0-preview.1");

        repository.Tag("1.0.0");
        AssertLocalVersion(repository, "1.0.0");
        Assert.Throws<InvalidOperationException>(() =>
            repository.CalculateCiOutput("refs/tags/1.0.0", "47")
        );
    }

    // The deleted version.env recorded a release intent and the CI wrapper rejected a
    // tag that did not match it. GitVersion has no such intent: a well-formed tag is the
    // version, and it is used exactly as written.
    [Fact]
    public void Any_tag_with_the_configured_prefix_is_honoured()
    {
        using var repository = GitVersionRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");

        repository.Tag("v9.9.9");
        AssertLocalVersion(repository, "9.9.9");
    }

    // GitVersion counts every commit, so a merge commit advances preview.N by the branch
    // commit count plus the merge commit itself. MinVer follows the first parent and
    // advances height by one per pull request instead. This is why the sample requires
    // squash merges and enforces the rule.
    [Fact]
    public void Merge_commits_advance_the_preview_number_by_every_commit()
    {
        using var repository = GitVersionRepository.Create();
        AssertLocalVersion(repository, "1.0.0-preview.0");

        repository.MergePullRequestWithMergeCommit(
            "feature/customer-export",
            "feat: add customer export",
            5
        );
        AssertLocalVersion(repository, "1.0.0-preview.6");

        repository.MergePullRequestWithMergeCommit(
            "feature/add-auth",
            "feat: add authentication",
            5
        );
        AssertLocalVersion(repository, "1.0.0-preview.12");

        repository.Tag("v1.0.0-rc.1");
        AssertReleaseVersion(repository, "1.0.0-rc.1");

        repository.MergePullRequestWithMergeCommit(
            "fix/release-candidate",
            "fix: correct release candidate behavior",
            3
        );
        AssertLocalVersion(repository, "1.0.0-preview.16");
    }

    // The script reports the version GitVersion calculated; it does not invent one. Both
    // sources are checked for preview versions, so a wrapper that mangles the value the
    // tool produced fails here.
    private static void AssertLocalVersion(GitVersionRepository repository, string expectedVersion)
    {
        Assert.Equal(expectedVersion, repository.CalculateVersion());
        Assert.Equal(expectedVersion, repository.CalculateVersionWithGitVersion());
    }

    // A release tag is the version on its own; the script passes it through unchanged.
    private static void AssertReleaseVersion(GitVersionRepository repository, string expectedVersion) =>
        Assert.Equal(expectedVersion, repository.CalculateVersion());

    // Mirrors the other samples' CI build suffix: the wrapper appends the UTC date and
    // the workflow run number to preview and RC artifacts so every build is unique.
    // Stable versions stay clean, and the environment records the audience.
    [Fact]
    public void Ci_output_appends_date_and_revision_to_prereleases()
    {
        using var repository = GitVersionRepository.Create();

        // The baseline preview is the initialization build. The wrapper leaves it
        // unsuffixed and records that it belongs to no audience, so a fresh repository
        // never publishes on main.
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
        Assert.Matches(@"^1\.0\.0-rc\.1\.\d{5}\.43$", candidate["version"]);
        Assert.Equal("staging", candidate["environment"]);

        repository.Tag("v1.0.0");
        var stable = repository.CalculateCiOutput("refs/tags/v1.0.0", "44");
        Assert.Equal("1.0.0", stable["version"]);
        Assert.Equal("production", stable["environment"]);
    }

    // An untagged commit after an RC tag returns to the preview train and is an internal
    // build rather than a staging release. Only the tag promotes a build to the staging
    // audience. The counter does not reset at the RC tag on main: GitVersion keeps
    // counting from the preview baseline, so this is the second commit, not the first.
    [Fact]
    public void Untagged_release_candidate_fix_targets_the_dev_audience()
    {
        using var repository = GitVersionRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v1.0.0-rc.1");
        repository.MergePullRequest(
            "fix/release-candidate",
            "fix: correct release candidate behavior"
        );

        var internalBuild = repository.CalculateCiOutput("refs/heads/main", "46");
        Assert.Matches(@"^1\.0\.0-preview\.2\.\d{5}\.46$", internalBuild["version"]);
        Assert.Equal("dev", internalBuild["environment"]);
    }

    // Only rc and stable tags are release identities; any other tag ref is rejected
    // instead of silently producing a preview artifact for a tagged commit.
    [Fact]
    public void Unsupported_release_tag_is_rejected()
    {
        using var repository = GitVersionRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v1.0.0-preview.5");

        Assert.Throws<InvalidOperationException>(() =>
            repository.CalculateCiOutput("refs/tags/v1.0.0-preview.5", "45")
        );
    }

    // GitVersion numbers previews by commit count, so the guard is what keeps one
    // accepted pull request equal to one commit on main. MinVer tolerates merge commits
    // and has no equivalent guard.
    [Fact]
    public void Merge_commits_are_rejected_when_squash_merges_are_required()
    {
        using var repository = GitVersionRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");

        Assert.Contains(
            "one accepted pull request advances",
            repository.RunSquashMergeGuard("refs/heads/main")
        );

        repository.MergePullRequestWithMergeCommit(
            "feature/add-auth",
            "feat: add authentication",
            1
        );
        Assert.Throws<InvalidOperationException>(() =>
            repository.RunSquashMergeGuard("refs/heads/main")
        );

        // A release tag carries its own identity, so the guard steps aside for it.
        Assert.Contains("Tag build", repository.RunSquashMergeGuard("refs/tags/v1.0.0"));
    }

    // The script owns the CLI invocation and the five workflow outputs, so both the
    // repository workflow and the standalone sample workflow must delegate to it.
    [Theory]
    [InlineData(".github/workflows/gitversion.yml")]
    [InlineData("gitversion-versioning-sample/.github/workflows/build-and-publish.yml")]
    public void Ci_workflow_calculates_through_the_script(string relativeWorkflowPath)
    {
        var workflow = File.ReadAllText(
            Path.Combine(GitVersionRepository.RepositoryRoot, relativeWorkflowPath)
        );

        Assert.Contains("scripts/calculate-version.sh", workflow);
        Assert.DoesNotContain("dotnet-gitversion\" /showvariable", workflow);
        Assert.DoesNotContain("sed -n 's/^version=//p'", workflow);
        foreach (
            var outputName in new[]
            {
                "version",
                "assembly_version",
                "informational_version",
                "environment",
                "commit",
            }
        )
            Assert.Contains($"steps.version.outputs.{outputName}", workflow);
    }
}
