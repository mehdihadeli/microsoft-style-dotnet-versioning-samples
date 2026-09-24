using System.Diagnostics;
using System.Text;
using Xunit;

namespace arcade_versioning_sample.Tests;

public sealed class ArcadeScenarioTests
{
    [Fact]
    public void Squash_merged_pull_requests_use_one_run_each_then_tags_control_releases()
    {
        using var repository = GitSandbox.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        var firstCount = repository.MainCommitCount;
        Assert.Equal("1.0.0-preview.1", ArcadeVersion.Calculate("1.0.0", 1, null));

        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        Assert.Equal(firstCount + 1, repository.MainCommitCount);
        Assert.Equal("1.0.0-preview.2", ArcadeVersion.Calculate("1.0.0", 2, null));

        Assert.Equal("1.0.0-rc.1", ArcadeVersion.Calculate("1.0.0", 3, "v1.0.0-rc.1"));
        Assert.Equal("1.0.0-rc.2", ArcadeVersion.Calculate("1.0.0", 4, "v1.0.0-rc.2"));
        Assert.Equal("1.0.0", ArcadeVersion.Calculate("1.0.0", 5, "v1.0.0"));

        repository.MergePullRequest("feature/add-authurization", "feat: add authurization");
        Assert.Equal("1.0.1-preview.1", ArcadeVersion.Calculate("1.0.1", 1, null));
    }
}

internal static class ArcadeVersion
{
    public static string Calculate(string prefix, int runNumber, string? tag) =>
        tag is not null ? tag.TrimStart('v') : $"{prefix}-preview.{runNumber}";
}

internal sealed class GitSandbox : IDisposable
{
    private readonly string _directory;

    private GitSandbox(string directory) => _directory = directory;

    public int MainCommitCount =>
        int.Parse(Run("git", "rev-list", "--first-parent", "--count", "main"));

    public static GitSandbox Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"arcade-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var repository = new GitSandbox(directory);
        repository.Run("git", "init");
        repository.Run(
            "git",
            "remote",
            "add",
            "origin",
            "https://example.com/versioning-tests.git"
        );
        repository.Run("git", "config", "user.email", "versioning-tests@example.com");
        repository.Run("git", "config", "user.name", "Versioning Tests");
        repository.Run("git", "checkout", "-b", "main");
        File.WriteAllText(Path.Combine(directory, "changes.txt"), "seed\n", Encoding.UTF8);
        repository.Run("git", "add", ".");
        repository.Run("git", "commit", "-m", "chore: initialize", "--no-verify");
        return repository;
    }

    public void MergePullRequest(string branch, string message)
    {
        Run("git", "checkout", "-b", branch);
        File.AppendAllText(Path.Combine(_directory, "changes.txt"), $"{message}\n", Encoding.UTF8);
        Run("git", "add", "changes.txt");
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "checkout", "main");
        Run("git", "merge", "--squash", branch);
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "branch", "-D", branch);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch { }
    }

    private string Run(string fileName, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = _directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} failed.\n{output}\n{error}");
        return output.Trim();
    }
}
