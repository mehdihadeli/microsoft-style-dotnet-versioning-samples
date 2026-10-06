using Xunit;

namespace gitversion_versioning_sample.Tests;

public sealed class GitVersionScenarioTests
{
    // Mirrors the README command scenario: previews come from GitVersion on main,
    // and a tag on a main commit supplies the RC or stable identity.
    [Fact]
    public void Preview_versions_follow_main_commits()
    {
        using var repository = GitVersionRepository.Create();
        AssertLocalVersion(repository, "1.0.0-preview.0");

        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        AssertLocalVersion(repository, "1.0.0-preview.1");
        Assert.Empty(repository.TagsAtHead());

        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        AssertLocalVersion(repository, "1.0.0-preview.2");
        Assert.Empty(repository.TagsAtHead());
    }

    // Mirrors the README RC and stable steps: tags on main, no release branch.
    [Fact]
    public void Release_tags_on_main_produce_release_versions()
    {
        using var repository = GitVersionRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.MergePullRequest("feature/add-auth", "feat: add authentication");

        repository.Tag("v1.0.0-rc.1");
        AssertReleaseVersion(repository, "1.0.0-rc.1");
        Assert.Contains("v1.0.0-rc.1", repository.TagsAtHead());

        repository.MergePullRequest(
            "fix/release-candidate",
            "fix: correct release candidate behavior"
        );
        Assert.Equal("1.0.0-preview.3", repository.CalculateVersionWithGitVersion());
        repository.Tag("v1.0.0-rc.2");
        AssertReleaseVersion(repository, "1.0.0-rc.2");
        Assert.Contains("v1.0.0-rc.2", repository.TagsAtHead());

        repository.Tag("v1.0.0");
        AssertReleaseVersion(repository, "1.0.0");
        Assert.Contains("v1.0.0", repository.TagsAtHead());
    }

    // Mirrors the README next-train step: a feat after the stable tag opens 1.1.0.
    [Fact]
    public void Conventional_commits_start_the_next_minor_train()
    {
        using var repository = GitVersionRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        repository.Tag("v1.0.0");
        AssertReleaseVersion(repository, "1.0.0");

        repository.MergePullRequest("feature/add-authorization", "feat: add authorization");
        AssertLocalVersion(repository, "1.1.0-preview.1");

        repository.MergePullRequest("feature/add-reporting", "feat: add reporting");
        AssertLocalVersion(repository, "1.1.0-preview.2");

        repository.MergePullRequest("feature/add-more", "feat: add more");
        AssertLocalVersion(repository, "1.1.0-preview.3");
    }

    // Mirrors the nerdbank sample's CI build suffix: uniquify each prerelease build.
    [Fact]
    public void Ci_output_appends_date_and_revision_to_prereleases()
    {
        using var repository = GitVersionRepository.Create();
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

    private static void AssertLocalVersion(GitVersionRepository repository, string expectedVersion)
    {
        Assert.Equal(expectedVersion, repository.CalculateVersion());
        Assert.Equal(expectedVersion, repository.CalculateVersionWithGitVersion());
    }

    private static void AssertReleaseVersion(GitVersionRepository repository, string expectedVersion) =>
        Assert.Equal(expectedVersion, repository.CalculateVersion());
}
