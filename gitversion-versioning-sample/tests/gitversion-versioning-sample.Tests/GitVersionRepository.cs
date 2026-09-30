using System.Text;
using System.Text.RegularExpressions;

namespace gitversion_versioning_sample.Tests;

internal sealed class GitVersionRepository : IDisposable
{
    private readonly string _directory;
    private readonly string _bash;
    private readonly string _tool;

    private GitVersionRepository(string directory, string bash, string tool) =>
        (_directory, _bash, _tool) = (directory, bash, tool);

    public static GitVersionRepository Create()
    {
        var root = FindSampleRoot();
        var directory = Path.Combine(Path.GetTempPath(), $"gitversion-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "scripts"));
        foreach (var file in new[] { "GitVersion.yml", "release.env", "release-version.sh" })
            File.Copy(Path.Combine(root, file), Path.Combine(directory, file));
        File.Copy(Path.Combine(root, "scripts", "calculate-version.sh"), Path.Combine(directory, "scripts", "calculate-version.sh"));
        var toolDirectory = Path.Combine(directory, ".tools");
        var tool = Path.Combine(toolDirectory, OperatingSystem.IsWindows() ? "dotnet-gitversion.exe" : "dotnet-gitversion");
        var repository = new GitVersionRepository(directory, FindBashExecutable(directory), tool);
        repository.Run("git", "init");
        repository.Run("git", "config", "user.email", "versioning-tests@example.com");
        repository.Run("git", "config", "user.name", "Versioning Tests");
        repository.Run("git", "checkout", "-b", "main");
        repository.Run("dotnet", "tool", "install", "--tool-path", toolDirectory, "GitVersion.Tool", "--version", "6.8.2");
        File.WriteAllText(Path.Combine(directory, ".gitignore"), ".tools/\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "changes.txt"), "seed\n", new UTF8Encoding(false));
        repository.Run("git", "add", ".");
        repository.Run("git", "commit", "-m", "chore: initialize", "--no-verify");
        repository.Tag("v1.0.0-preview.0");
        return repository;
    }

    public string CalculateVersion() =>
        ProcessRunner.Run(_directory, _bash, ["scripts/calculate-version.sh"], ("GITVERSION_CLI", _tool));

    public string CalculateVersionWithGitVersion() => Run(_tool, "/showvariable", "SemVer");

    public void PrepareVersionWithReleaseScript(string branch, string command, string version)
    {
        Run("git", "checkout", "-b", branch);
        RunReleaseVersionScript(command, version);
    }

    public void RunReleaseVersionScript(params string[] arguments) =>
        Run(_bash, ["release-version.sh", .. arguments]);

    public void PrepareVersionWithGitVersion(string branch, string phase, string version)
    {
        Run("git", "checkout", "-b", branch);
        Replace(Path.Combine(_directory, "GitVersion.yml"), "(?m)^next-version: .+$", $"next-version: {version}");
        Replace(Path.Combine(_directory, "release.env"), "(?m)^GITVERSION_RELEASE_VERSION=.*$", $"GITVERSION_RELEASE_VERSION={version}");
        Replace(Path.Combine(_directory, "release.env"), "(?m)^GITVERSION_RELEASE_PHASE=.*$", $"GITVERSION_RELEASE_PHASE={phase}");
    }

    public void MergePullRequest(string branch, string message)
    {
        if (!IsOnBranch(branch)) Run("git", "checkout", "-b", branch);
        File.AppendAllText(Path.Combine(_directory, "changes.txt"), $"{message}\n", new UTF8Encoding(false));
        Run("git", "add", "changes.txt", "GitVersion.yml", "release.env");
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "checkout", "main");
        Run("git", "merge", "--squash", branch);
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "branch", "-D", branch);
    }

    public void Tag(string tag) => Run("git", "tag", "-a", tag, "-m", tag);
    public string[] TagsAtHead() => Run("git", "tag", "--points-at", "HEAD").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void Replace(string path, string pattern, string replacement) =>
        File.WriteAllText(path, Regex.Replace(File.ReadAllText(path), pattern, replacement), new UTF8Encoding(false));
    private bool IsOnBranch(string branch) => Run("git", "branch", "--show-current") == branch;
    private string Run(string file, params string[] arguments) => ProcessRunner.Run(_directory, file, arguments);

    private static string FindBashExecutable(string directory)
    {
        if (!OperatingSystem.IsWindows()) return "bash";
        var git = ProcessRunner.Run(directory, "where.exe", "git").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
        return Path.Combine(Directory.GetParent(Path.GetDirectoryName(git)!)!.FullName, "bin", "bash.exe");
    }

    private static string FindSampleRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GitVersion.yml"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find GitVersion sample root.");
    }
}