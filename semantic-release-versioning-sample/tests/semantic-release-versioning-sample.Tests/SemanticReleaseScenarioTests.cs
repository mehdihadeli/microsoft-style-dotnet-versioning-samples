using Xunit;

namespace semantic_release_versioning_sample.Tests;

[Collection("semantic-release")]
public sealed class SemanticReleaseScenarioTests(SemanticReleaseTestFixture fixture)
{
    [Fact]
    public void Release_helper_commands_follow_release_scenario()
    {
        using var repository = fixture.CreateRepository("0.0.0");
        Assert.Equal("1.0.0-preview.0", repository.GetCalculatorVersion());

        repository.MergePullRequest(
            "feature/customer-export",
            "feat: add customer export\n\nBREAKING CHANGE: establish the 1.0 API contract"
        );
        AssertPreviewVersion(repository, "1.0.0", "1.0.0-preview.1");

        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        AssertPreviewVersion(repository, "1.0.0", "1.0.0-preview.2");

        repository.PrepareVersionWithReleaseScript("chore/prepare-1.0.0-rc", "prepare-rc", "1.0.0");
        repository.MergePullRequest("chore/prepare-1.0.0-rc", "chore: prepare 1.0.0 RC train");
        AssertReleaseVersion(repository, "1.0.0-rc.1");
        repository.RunReleaseVersionScript("tag");
        AssertReleaseVersion(repository, "1.0.0-rc.1");
        Assert.Contains("v1.0.0-rc.1", repository.TagsAtHead());

        repository.MergePullRequest("fix/release-candidate", "fix: correct release candidate behavior");
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
        repository.MergePullRequest("chore/prepare-1.1.0-preview", "feat: start 1.1.0 preview train");
        AssertPreviewVersion(repository, "1.1.0", "1.1.0-preview.1");
        repository.MergePullRequest("feature/add-authorization", "feat: add authorization");
        AssertPreviewVersion(repository, "1.1.0", "1.1.0-preview.2");
        repository.MergePullRequest("feature/add-reporting", "feat: add reporting");
        AssertPreviewVersion(repository, "1.1.0", "1.1.0-preview.3");
    }

    [Fact]
    public void Direct_semantic_release_inputs_follow_release_scenario()
    {
        using var repository = fixture.CreateRepository("0.0.0");
        Assert.Equal("1.0.0-preview.0", repository.GetCalculatorVersion());

        repository.MergePullRequest("feature/customer-export", "feat: add customer export\n\nBREAKING CHANGE: establish the 1.0 API contract");
        AssertPreviewVersion(repository, "1.0.0", "1.0.0-preview.1");
        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        AssertPreviewVersion(repository, "1.0.0", "1.0.0-preview.2");

        repository.PrepareVersionDirectly("chore/prepare-1.0.0-rc", "rc", "1.0.0");
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

        repository.PrepareVersionDirectly("chore/prepare-1.0.0", "stable", "1.0.0");
        repository.MergePullRequest("chore/prepare-1.0.0", "chore: prepare 1.0.0");
        repository.Tag("v1.0.0");
        AssertReleaseVersion(repository, "1.0.0");
        Assert.Contains("v1.0.0", repository.TagsAtHead());

        repository.PrepareVersionDirectly("chore/prepare-1.1.0-preview", "preview", "1.1.0");
        repository.MergePullRequest("chore/prepare-1.1.0-preview", "feat: start 1.1.0 preview train");
        AssertPreviewVersion(repository, "1.1.0", "1.1.0-preview.1");
        repository.MergePullRequest("feature/add-authorization", "feat: add authorization");
        AssertPreviewVersion(repository, "1.1.0", "1.1.0-preview.2");
        repository.MergePullRequest("feature/add-reporting", "feat: add reporting");
        AssertPreviewVersion(repository, "1.1.0", "1.1.0-preview.3");
    }

    [Fact]
    public void Mismatched_manual_tag_is_rejected()
    {
        using var repository = fixture.CreateRepository("0.0.0");
        repository.MergePullRequest("feature/customer-export", "feat: add customer export\n\nBREAKING CHANGE: establish the 1.0 API contract");
        repository.Tag("v9.9.9");
        Assert.Throws<InvalidOperationException>(() => repository.GetCalculatorVersion());
    }

    private static void AssertPreviewVersion(SemanticReleaseRepository repository, string nativeVersion, string expected)
    {
        Assert.Equal(nativeVersion, repository.GetNextVersionWithSemanticRelease());
        Assert.Equal(expected, repository.GetCiVersion());
        Assert.Equal(expected, repository.GetCalculatorVersion());
    }

    private static void AssertReleaseVersion(SemanticReleaseRepository repository, string expected)
    {
        Assert.Equal(expected, repository.GetCiVersion());
        Assert.Equal(expected, repository.GetCalculatorVersion());
    }
}
