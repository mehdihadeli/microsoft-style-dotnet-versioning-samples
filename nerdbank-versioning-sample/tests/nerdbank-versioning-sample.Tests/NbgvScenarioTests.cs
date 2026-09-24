using System.Diagnostics;
using System.Text;
using Xunit;

namespace nerdbank_versioning_sample.Tests;

public sealed class NbgvScenarioTests
{
    [Fact]
    public void Squash_merged_pull_requests_get_unique_versions_then_tags_control_releases()
    {
        using var repository = NbgvRepository.Create();
        repository.MergePullRequest("feature/customer-export", "feat: add customer export");
        var previewOne = repository.CalculateEffectiveVersion();
        repository.MergePullRequest("feature/add-auth", "feat: add authentication");
        var previewTwo = repository.CalculateEffectiveVersion();
        Assert.Equal("1.0.0-preview.1", previewOne);
        Assert.Equal("1.0.0-preview.2", previewTwo);
        Assert.NotEqual(previewOne, previewTwo);
        Assert.True(
            string.CompareOrdinal(previewOne, previewTwo) < 0,
            $"Expected second NBGV preview '{previewTwo}' to be newer than '{previewOne}'."
        );

        repository.Tag("v1.0.0-rc.1");
        Assert.Equal("1.0.0-rc.1", repository.CalculateEffectiveVersion());

        repository.Tag("v1.0.0-rc.2");
        Assert.Equal("1.0.0-rc.2", repository.CalculateEffectiveVersion());

        repository.Tag("v1.0.0");
        Assert.Equal("1.0.0", repository.CalculateEffectiveVersion());

        repository.MergePullRequest("feature/add-authurization", "feat: add authurization");
        var previewThree = repository.CalculateEffectiveVersion();
        Assert.Equal("1.0.1-preview.1", previewThree);
    }
}

internal sealed class NbgvRepository : IDisposable
{
    private readonly string _directory;
    private readonly string _bash;
    private readonly string _tool;
    private readonly string _versionScript;

    private NbgvRepository(string directory, string versionScript)
    {
        _directory = directory;
        _bash = FindBashExecutable(directory);
        _versionScript = versionScript;
        _tool = Path.Combine(
            directory,
            ".tools",
            OperatingSystem.IsWindows() ? "nbgv.exe" : "nbgv"
        );
    }

    public int MainCommitCount =>
        int.Parse(Run("git", "rev-list", "--first-parent", "--count", "main"));

    public static NbgvRepository Create()
    {
        var root = FindSampleRoot();
        var directory = Path.Combine(Path.GetTempPath(), $"nbgv-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.Copy(Path.Combine(root, "version.json"), Path.Combine(directory, "version.json"));
        var repository = new NbgvRepository(
            directory,
            Path.Combine(root, "scripts", "calculate-version.sh")
        );
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
            "nbgv",
            "--version",
            "3.10.94"
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

    public string CalculateEffectiveVersion() =>
        Run(
            _bash,
            _versionScript.Replace('\\', '/'),
            _directory.Replace('\\', '/'),
            _tool.Replace('\\', '/')
        );

    private static string FindBashExecutable(string directory)
    {
        if (!OperatingSystem.IsWindows())
            return "bash";

        var gitExecutable = NbgvProcessRunner
            .Run(directory, "where.exe", "git")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
        var gitRoot = Directory.GetParent(Path.GetDirectoryName(gitExecutable)!)!.FullName;
        var gitBash = Path.Combine(gitRoot, "bin", "bash.exe");
        if (!File.Exists(gitBash))
            throw new FileNotFoundException("Could not find Git Bash.", gitBash);
        return gitBash;
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
        NbgvProcessRunner.Run(_directory, fileName, arguments);

    private static string FindSampleRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "version.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find NBGV sample root.");
    }
}

internal static class NbgvProcessRunner
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
