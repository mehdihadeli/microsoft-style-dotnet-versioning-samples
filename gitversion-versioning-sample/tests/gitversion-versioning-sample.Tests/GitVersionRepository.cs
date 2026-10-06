using System.Text;

namespace gitversion_versioning_sample.Tests;

internal sealed class GitVersionRepository : IDisposable
{
    private const string BaselineTag = "v1.0.0-preview.0";
    private const string GitVersionVersion = "6.8.2";

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
        File.Copy(Path.Combine(root, "GitVersion.yml"), Path.Combine(directory, "GitVersion.yml"));
        File.Copy(
            Path.Combine(root, "scripts", "calculate-version.sh"),
            Path.Combine(directory, "scripts", "calculate-version.sh")
        );
        var toolDirectory = Path.Combine(directory, ".tools");
        var tool = Path.Combine(
            toolDirectory,
            OperatingSystem.IsWindows() ? "dotnet-gitversion.exe" : "dotnet-gitversion"
        );
        var repository = new GitVersionRepository(directory, FindBashExecutable(directory), tool);
        repository.Run("git", "init");
        repository.Run("git", "config", "user.email", "versioning-tests@example.com");
        repository.Run("git", "config", "user.name", "Versioning Tests");
        repository.Run("git", "checkout", "-b", "main");
        repository.Run(
            "dotnet",
            "tool",
            "install",
            "--tool-path",
            toolDirectory,
            "GitVersion.Tool",
            "--version",
            GitVersionVersion
        );
        File.WriteAllText(Path.Combine(directory, ".gitignore"), ".tools/\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "changes.txt"), "seed\n", new UTF8Encoding(false));
        repository.Run("git", "add", ".");
        repository.Run("git", "commit", "-m", "chore: initialize", "--no-verify");
        repository.Tag(BaselineTag);
        return repository;
    }

    public string CalculateVersion() =>
        ProcessRunner.Run(_directory, _bash, ["scripts/calculate-version.sh"], ToolEnvironment);

    public string CalculateVersionWithGitVersion() => Run(_tool, "/showvariable", "SemVer");

    public Dictionary<string, string> CalculateCiOutput(string gitRef, string runNumber)
    {
        var environment = new Dictionary<string, string>(ToolEnvironment)
        {
            ["GITHUB_REF"] = gitRef,
            ["GITHUB_REF_NAME"] = gitRef.Replace("refs/heads/", "").Replace("refs/tags/", ""),
            ["GITHUB_RUN_NUMBER"] = runNumber,
        };
        return ProcessRunner
            .Run(_directory, _bash, ["scripts/calculate-version.sh", "github-output"], environment)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1]);
    }

    public void MergePullRequest(string branch, string message)
    {
        Run("git", "checkout", "-b", branch);
        Commit(message);
        Run("git", "checkout", "main");
        Run("git", "merge", "--squash", branch);
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "branch", "-D", branch);
    }

    public void Commit(string message)
    {
        File.AppendAllText(
            Path.Combine(_directory, "changes.txt"),
            $"{message}\n",
            new UTF8Encoding(false)
        );
        Run("git", "add", ".");
        Run("git", "commit", "-m", message, "--no-verify");
    }

    public void Tag(string tag) => Run("git", "tag", "-a", tag, "-m", tag);

    public string[] TagsAtHead() =>
        Run("git", "tag", "--points-at", "HEAD")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private Dictionary<string, string> ToolEnvironment => new() { ["GITVERSION_CLI"] = _tool };

    private string Run(string file, params string[] arguments) =>
        ProcessRunner.Run(_directory, file, arguments);

    private static string FindBashExecutable(string directory)
    {
        if (!OperatingSystem.IsWindows())
            return "bash";
        var git = ProcessRunner
            .Run(directory, "where.exe", "git")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
        return Path.Combine(Directory.GetParent(Path.GetDirectoryName(git)!)!.FullName, "bin", "bash.exe");
    }

    private static string FindSampleRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GitVersion.yml")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find GitVersion sample root.");
    }
}
