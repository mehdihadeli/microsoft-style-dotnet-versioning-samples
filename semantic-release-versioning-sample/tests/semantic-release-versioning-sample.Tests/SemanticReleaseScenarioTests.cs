using Xunit;

namespace semantic_release_versioning_sample.Tests;

[Collection("semantic-release")]
public sealed class SemanticReleaseScenarioTests(SemanticReleaseTestFixture fixture)
{
    [Fact]
    public void Conventional_commits_and_tags_cover_release_line()
    {
        using var repository = fixture.CreateRepository("0.0.0");

        repository.MergePullRequest(
            "feature/customer-export",
            "feat: add customer export\n\nBREAKING CHANGE: establish the 1.0 API contract"
        );
        Assert.Equal("1.0.0-preview.1", repository.GetCiVersion(1));

        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        Assert.Equal("1.0.0-preview.2", repository.GetCiVersion(2));

        repository.Tag("v1.0.0-rc.1");
        Assert.Equal("1.0.0-rc.1", repository.GetCiVersion(3));

        repository.Tag("v1.0.0-rc.2");
        Assert.Equal("1.0.0-rc.2", repository.GetCiVersion(4));

        repository.Tag("v1.0.0");
        Assert.Equal("1.0.0", repository.GetCiVersion(5));

        repository.MergePullRequest("feature/add-authurization", "feat: add authurization");
        Assert.Equal("1.0.1-preview.1", repository.GetCiVersion(1));
    }
}
