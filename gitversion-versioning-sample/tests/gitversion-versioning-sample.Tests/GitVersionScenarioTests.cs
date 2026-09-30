using Xunit;

namespace gitversion_versioning_sample.Tests;

public sealed class GitVersionScenarioTests
{
    [Fact]
    public void Release_helper_commands_follow_release_scenario()
    {
        using var repository = GitVersionRepository.Create();
        AssertLocalVersion(repository, "1.0.0-preview.0");

        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        AssertLocalVersion(repository, "1.0.0-preview.1");
        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        AssertLocalVersion(repository, "1.0.0-preview.2");

        repository.PrepareVersionWithReleaseScript("chore/prepare-1.0.0-rc", "prepare-rc", "1.0.0");
        repository.MergePullRequest("chore/prepare-1.0.0-rc", "chore: prepare 1.0.0 RC train");
        AssertReleaseVersion(repository, "1.0.0-rc.1");
        repository.RunReleaseVersionScript("tag");
        AssertReleaseVersion(repository, "1.0.0-rc.1");
        Assert.Contains("v1.0.0-rc.1", repository.TagsAtHead());

        repository.MergePullRequest(
            "fix/release-candidate",
            "fix: correct release candidate behavior"
        );
        AssertReleaseVersion(repository, "1.0.0-rc.2");
        repository.RunReleaseVersionScript("tag");
        AssertReleaseVersion(repository, "1.0.0-rc.2");
        Assert.Contains("v1.0.0-rc.2", repository.TagsAtHead());

        repository.PrepareVersionWithReleaseScript("chore/prepare-1.0.0", "prepare-stable", "1.0.0");
        repository.MergePullRequest("chore/prepare-1.0.0", "chore: prepare 1.0.0");
        repository.RunReleaseVersionScript("tag");
        AssertReleaseVersion(repository, "1.0.0");
        Assert.Contains("v1.0.0", repository.TagsAtHead());

        repository.PrepareVersionWithReleaseScript("chore/prepare-1.1.0-preview", "prepare-train", "1.1.0");
        repository.MergePullRequest(
            "chore/prepare-1.1.0-preview",
            "chore: start 1.1.0 preview train"
        );
        AssertLocalVersion(repository, "1.1.0-preview.1");
        repository.MergePullRequest("feature/add-authorization", "feat: add authorization");
        AssertLocalVersion(repository, "1.1.0-preview.2");
        repository.MergePullRequest("feature/add-reporting", "feat: add reporting");
        AssertLocalVersion(repository, "1.1.0-preview.3");
    }

    [Fact]
    public void Direct_gitversion_inputs_follow_release_scenario()
    {
        using var repository = GitVersionRepository.Create();
        AssertLocalVersion(repository, "1.0.0-preview.0");
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        AssertLocalVersion(repository, "1.0.0-preview.1");
        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        AssertLocalVersion(repository, "1.0.0-preview.2");

        repository.PrepareVersionWithGitVersion("chore/prepare-1.0.0-rc", "rc", "1.0.0");
        repository.MergePullRequest("chore/prepare-1.0.0-rc", "chore: prepare 1.0.0 RC train");
        AssertReleaseVersion(repository, "1.0.0-rc.1");
        repository.Tag("v1.0.0-rc.1");
        AssertReleaseVersion(repository, "1.0.0-rc.1");
        Assert.Contains("v1.0.0-rc.1", repository.TagsAtHead());
        repository.MergePullRequest("fix/release-candidate", "fix: correct release candidate behavior");
        AssertReleaseVersion(repository, "1.0.0-rc.2");
        repository.Tag("v1.0.0-rc.2");
        AssertReleaseVersion(repository, "1.0.0-rc.2");
        Assert.Contains("v1.0.0-rc.2", repository.TagsAtHead());
        repository.PrepareVersionWithGitVersion("chore/prepare-1.0.0", "stable", "1.0.0");
        repository.MergePullRequest("chore/prepare-1.0.0", "chore: prepare 1.0.0");
        repository.Tag("v1.0.0");
        AssertReleaseVersion(repository, "1.0.0");
        Assert.Contains("v1.0.0", repository.TagsAtHead());
        repository.PrepareVersionWithGitVersion("chore/prepare-1.1.0-preview", "preview", "1.1.0");
        repository.MergePullRequest("chore/prepare-1.1.0-preview", "chore: start 1.1.0 preview train");
        AssertLocalVersion(repository, "1.1.0-preview.1");
        repository.MergePullRequest("feature/add-authorization", "feat: add authorization");
        AssertLocalVersion(repository, "1.1.0-preview.2");
        repository.MergePullRequest("feature/add-reporting", "feat: add reporting");
        AssertLocalVersion(repository, "1.1.0-preview.3");
    }

    [Fact]
    public void Mismatched_manual_tag_is_rejected()
    {
        using var repository = GitVersionRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v9.9.9");
        Assert.Throws<InvalidOperationException>(() => repository.CalculateVersion());
    }

    private static void AssertLocalVersion(GitVersionRepository repository, string expectedVersion)
    {
        Assert.Equal(expectedVersion, repository.CalculateVersion());
        Assert.Equal(expectedVersion, repository.CalculateVersionWithGitVersion());
    }

    private static void AssertReleaseVersion(GitVersionRepository repository, string expectedVersion) =>
        Assert.Equal(expectedVersion, repository.CalculateVersion());
}
