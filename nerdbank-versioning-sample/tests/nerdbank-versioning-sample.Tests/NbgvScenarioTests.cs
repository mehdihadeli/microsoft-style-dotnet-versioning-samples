using Xunit;

namespace nerdbank_versioning_sample.Tests;

public sealed class NbgvScenarioTests
{
    private static void AssertLocalVersion(NbgvRepository repository, string expectedVersion)
    {
        Assert.Equal(expectedVersion, repository.CalculateVersion());
        Assert.Equal(expectedVersion, repository.CalculateVersionWithNbgv());
    }

    [Fact]
    public void Release_helper_commands_follow_release_scenario()
    {
        using var repository = NbgvRepository.Create();
        AssertLocalVersion(repository, "1.0.0-preview.0");

        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        AssertLocalVersion(repository, "1.0.0-preview.1");
        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        AssertLocalVersion(repository, "1.0.0-preview.2");

        repository.PrepareVersionWithReleaseScript("chore/prepare-1.0.0-rc", "prepare-rc", "1.0.0");
        repository.MergePullRequest("chore/prepare-1.0.0-rc", "chore: prepare 1.0.0 RC train");
        repository.RunReleaseVersionScript("tag");
        AssertLocalVersion(repository, "1.0.0-rc.1");
        Assert.Contains("v1.0.0-rc.1", repository.TagsAtHead());

        repository.MergePullRequest(
            "fix/release-candidate",
            "fix: correct release candidate behavior"
        );
        AssertLocalVersion(repository, "1.0.0-rc.2");
        repository.RunReleaseVersionScript("tag");
        AssertLocalVersion(repository, "1.0.0-rc.2");
        Assert.Contains("v1.0.0-rc.2", repository.TagsAtHead());

        repository.PrepareVersionWithReleaseScript(
            "chore/prepare-1.0.0",
            "prepare-stable",
            "1.0.0"
        );
        repository.MergePullRequest("chore/prepare-1.0.0", "chore: prepare 1.0.0");
        repository.RunReleaseVersionScript("tag");
        AssertLocalVersion(repository, "1.0.0");
        Assert.Contains("v1.0.0", repository.TagsAtHead());

        repository.PrepareVersionWithReleaseScript(
            "chore/prepare-1.1.0-preview",
            "prepare-train",
            "1.1.0"
        );
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
    public void Direct_nbgv_commands_follow_release_scenario()
    {
        using var repository = NbgvRepository.Create();
        AssertLocalVersion(repository, "1.0.0-preview.0");

        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        AssertLocalVersion(repository, "1.0.0-preview.1");
        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        AssertLocalVersion(repository, "1.0.0-preview.2");

        repository.PrepareVersionWithNbgv("chore/prepare-1.0.0-rc", "1.0.0-rc.{height}");
        repository.MergePullRequest("chore/prepare-1.0.0-rc", "chore: prepare 1.0.0 RC train");
        repository.CreateNbgvTag();
        AssertLocalVersion(repository, "1.0.0-rc.1");
        Assert.Contains("v1.0.0-rc.1", repository.TagsAtHead());

        repository.MergePullRequest(
            "fix/release-candidate",
            "fix: correct release candidate behavior"
        );
        repository.CreateNbgvTag();
        AssertLocalVersion(repository, "1.0.0-rc.2");
        Assert.Contains("v1.0.0-rc.2", repository.TagsAtHead());

        repository.PrepareVersionWithNbgv("chore/prepare-1.0.0", "1.0.0", true);
        repository.MergePullRequest("chore/prepare-1.0.0", "chore: prepare 1.0.0");
        repository.CreateNbgvTag();
        AssertLocalVersion(repository, "1.0.0");
        Assert.Contains("v1.0.0", repository.TagsAtHead());

        repository.PrepareVersionWithNbgv(
            "chore/prepare-1.1.0-preview",
            "1.1.0-preview.{height}",
            true
        );
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
    public void Mismatched_manual_tag_is_rejected()
    {
        using var repository = NbgvRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        repository.Tag("v9.9.9");

        Assert.Throws<InvalidOperationException>(() => repository.CalculateVersion());
    }
}
