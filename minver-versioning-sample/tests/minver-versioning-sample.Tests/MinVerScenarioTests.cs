using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace minver_versioning_sample.Tests;

public sealed class MinVerScenarioTests
{
    [Fact]
    public void Squash_merged_pull_requests_advance_once_then_tags_control_releases()
    {
        using var repository = MinVerRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        var previewOne = repository.CalculateEffectiveVersion();
        var mainCountOne = repository.MainCommitCount;
        Assert.Equal("1.0.0-preview.1", previewOne);

        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        var previewTwo = repository.CalculateEffectiveVersion();

        Assert.Equal(mainCountOne + 1, repository.MainCommitCount);
        Assert.Equal("1.0.0-preview.2", previewTwo);
        Assert.Equal(GetLastNumber(previewOne) + 1, GetLastNumber(previewTwo));

        repository.Tag("v1.0.0-rc.1");
        Assert.Equal("1.0.0-rc.1", repository.CalculateEffectiveVersion());
        repository.Tag("v1.0.0-rc.2");
        Assert.Equal("1.0.0-rc.2", repository.CalculateEffectiveVersion());
        repository.Tag("v1.0.0");
        Assert.Equal("1.0.0", repository.CalculateEffectiveVersion());

        repository.MergePullRequest("feature/add-authurization", "feat: add authurization");
        Assert.Equal("1.0.1-preview.1", repository.CalculateEffectiveVersion());
    }

    private static int GetLastNumber(string version)
    {
        var numbers = Regex.Matches(version.Split('+')[0], @"\d+");
        Assert.NotEmpty(numbers);
        return int.Parse(numbers[^1].Value);
    }
}

internal sealed class MinVerRepository : IDisposable
{
    private readonly string _directory;
    private readonly string _tool;

    private MinVerRepository(string directory)
    {
        _directory = directory;
        _tool = Path.Combine(
            directory,
            ".tools",
            OperatingSystem.IsWindows() ? "minver.exe" : "minver"
        );
    }

    public int MainCommitCount =>
        int.Parse(Run("git", "rev-list", "--first-parent", "--count", "main"));

    public static MinVerRepository Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"minver-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var repository = new MinVerRepository(directory);
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
        repository.Run(
            "dotnet",
            "tool",
            "install",
            "--tool-path",
            Path.Combine(directory, ".tools"),
            "minver-cli",
            "--version",
            "8.0.0"
        );
        File.WriteAllText(Path.Combine(directory, ".gitignore"), ".tools/\n", Encoding.UTF8);
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

    public void Tag(string tag) => Run("git", "tag", "-a", tag, "-m", tag);

    public string CalculateEffectiveVersion()
    {
        var tags = Run("git", "tag", "--points-at", "HEAD")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(value => Regex.IsMatch(value, @"^v\d+\.\d+\.\d+(?:-rc\.\d+)?$"))
            .OrderByDescending(GetReleaseTagRank)
            .ToArray();
        return tags.Length > 0 ? tags[0][1..] : Run(_tool, "-t", "v", "-m", "1.0", "-p", "preview");
    }

    private static (int ReleaseKind, int CandidateNumber) GetReleaseTagRank(string tag)
    {
        if (Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$"))
            return (2, 0);

        return (1, int.Parse(Regex.Match(tag, @"-rc\.(\d+)$").Groups[1].Value));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch { }
    }

    private string Run(string fileName, params string[] arguments) =>
        ProcessRunner.Run(_directory, fileName, arguments);
}

internal static class ProcessRunner
{
    public static string Run(string directory, string fileName, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = directory,
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
            throw new InvalidOperationException(
                $"{fileName} {string.Join(' ', arguments)} failed.\n{output}\n{error}"
            );
        return output.Trim();
    }
}
