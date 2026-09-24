using Xunit;

namespace semantic_release_versioning_sample.Tests;

public sealed class SemanticReleaseTestFixture : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"semantic-release-fixture-{Guid.NewGuid():N}"
    );
    private string _npx = string.Empty;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        var sampleRoot = SemanticReleaseRepository.FindSampleRoot();
        foreach (var fileName in new[] { "package.json", "package-lock.json" })
            File.Copy(Path.Combine(sampleRoot, fileName), Path.Combine(_directory, fileName));

        var npm = SemanticReleaseRepository.FindExecutable(
            OperatingSystem.IsWindows() ? "npm.cmd" : "npm"
        );
        _npx = SemanticReleaseRepository.FindExecutable(
            OperatingSystem.IsWindows() ? "npx.cmd" : "npx"
        );
        await SemanticReleaseRepository.RunProcessAsync(
            npm,
            _directory,
            "ci",
            "--ignore-scripts",
            "--no-audit",
            "--no-fund"
        );
    }

    internal SemanticReleaseRepository CreateRepository(string stableVersion) =>
        SemanticReleaseRepository.Create(_directory, _npx, stableVersion);

    public Task DisposeAsync()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch { }

        return Task.CompletedTask;
    }
}
