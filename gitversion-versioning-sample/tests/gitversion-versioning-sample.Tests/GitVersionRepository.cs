using System.Text;

namespace gitversion_versioning_sample.Tests;

internal sealed class GitVersionRepository : IDisposable
{
    // GitVersion needs a base version, so the fixture seeds the same preview tag the
    // README scenario tags on the initial commit.
    private const string BaselineTag = "v1.0.0-preview.0";
    private const string GitVersionVersion = "6.8.2";

    private readonly string _directory;
    private readonly string _bash;
    private readonly string _tool;

    private GitVersionRepository(string directory, string bash, string tool) =>
        (_directory, _bash, _tool) = (directory, bash, tool);

    public static GitVersionRepository Create()
    {
        var sampleRoot = FindSampleRoot();
        var directory = Path.Combine(Path.GetTempPath(), $"gitversion-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "scripts"));
        Directory.CreateDirectory(Path.Combine(directory, ".github", "scripts"));
        // The script holds the strategy and the config holds the version rules, so the
        // tests exercise the real files. Changing them changes what these tests calculate.
        File.Copy(
            Path.Combine(sampleRoot, "scripts", "calculate-version.sh"),
            Path.Combine(directory, "scripts", "calculate-version.sh")
        );
        File.Copy(
            Path.Combine(sampleRoot, "GitVersion.yml"),
            Path.Combine(directory, "GitVersion.yml")
        );
        File.Copy(
            Path.Combine(sampleRoot, ".github", "scripts", "require-squash-merge.sh"),
            Path.Combine(directory, ".github", "scripts", "require-squash-merge.sh")
        );
        File.WriteAllText(Path.Combine(directory, ".gitignore"), ".tools/\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "changes.txt"), "seed\n", new UTF8Encoding(false));

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
        repository.Run("git", "add", ".");
        repository.Run("git", "commit", "-m", "chore: initialize", "--no-verify");
        repository.Tag(BaselineTag);
        return repository;
    }

    // What a developer runs locally: the calculated version with no CI build suffix.
    public string CalculateVersion() =>
        ProcessRunner.Run(_directory, _bash, ["scripts/calculate-version.sh"], ToolEnvironment);

    // The raw tool, so the tests prove the script reports the calculated version rather
    // than inventing one.
    public string CalculateVersionWithGitVersion() => Run(_tool, "/showvariable", "SemVer");

    // GitVersion takes the target line from the commit messages rather than from an option
    // in the script, so there is no StartNewVersionTrain equivalent. A `feat:` after a
    // stable tag opens the next minor line and a breaking marker such as `feat!:` opens
    // the next major line, which is what Commit_messages_start_a_new_train covers.
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

    public void Tag(string tag) => Run("git", "tag", "-a", tag, "-m", tag);

    public string[] TagsAtHead() =>
        Run("git", "tag", "--points-at", "HEAD")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    public void MergePullRequest(string branch, string message)
    {
        if (!IsOnBranch(branch))
            Run("git", "checkout", "-b", branch);
        File.AppendAllText(Path.Combine(_directory, "changes.txt"), $"{message}\n", new UTF8Encoding(false));
        Run("git", "add", ".");
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "checkout", "main");
        Run("git", "merge", "--squash", branch);
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "branch", "-D", branch);
    }

    // GitHub's "Create a merge commit" option: the branch commits and a merge
    // commit all land on main. GitVersion counts every commit, so preview.N advances by
    // the branch commit count plus the merge commit itself. This is why the sample
    // requires squash merges and enforces them with require-squash-merge.sh.
    public void MergePullRequestWithMergeCommit(string branch, string message, int commitCount)
    {
        Run("git", "checkout", "-b", branch);
        var path = Path.Combine(_directory, $"{branch.Replace('/', '-')}.txt");
        for (var index = 1; index <= commitCount; index++)
        {
            File.AppendAllText(path, $"line {index}\n", new UTF8Encoding(false));
            Run("git", "add", ".");
            Run("git", "commit", "-m", $"{message} (part {index})", "--no-verify");
        }
        Run("git", "checkout", "main");
        Run("git", "merge", "--no-ff", "-m", $"Merge pull request from {branch}", branch);
    }

    // The guard the version job runs before it calculates a version. It exits non-zero on
    // a merge commit and steps aside for release tags.
    public string RunSquashMergeGuard(string gitRef) =>
        ProcessRunner.Run(
            _directory,
            _bash,
            [".github/scripts/require-squash-merge.sh"],
            new Dictionary<string, string> { ["GITHUB_REF"] = gitRef }
        );

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private bool IsOnBranch(string branch) => Run("git", "branch", "--show-current") == branch;
    private string Run(string file, params string[] arguments) =>
        ProcessRunner.Run(_directory, file, arguments);

    private Dictionary<string, string> ToolEnvironment => new() { ["GITVERSION_CLI"] = _tool };

    private static string FindBashExecutable(string directory)
    {
        if (!OperatingSystem.IsWindows()) return "bash";
        var git = ProcessRunner
            .Run(directory, "where.exe", "git")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
        return Path.Combine(Directory.GetParent(Path.GetDirectoryName(git)!)!.FullName, "bin", "bash.exe");
    }

    internal static string SampleRoot { get; } = FindSampleRoot();

    internal static string RepositoryRoot { get; } = Directory.GetParent(SampleRoot)!.FullName;

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

