using System.Text;

namespace minver_versioning_sample.Tests;

internal sealed class MinVerRepository : IDisposable
{
    // MinVer CLI writes its log to stderr and the calculated version to stdout.
    private const string MinVerVersion = "8.0.0";

    private readonly string _directory;
    private readonly string _bash;
    private readonly string _tool;
    private string _trainMajorMinor = "1.0";

    private MinVerRepository(string directory, string bash, string tool) =>
        (_directory, _bash, _tool) = (directory, bash, tool);

    public static MinVerRepository Create()
    {
        var sampleRoot = FindSampleRoot();
        var directory = Path.Combine(Path.GetTempPath(), $"minver-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "scripts"));
        // The script holds the strategy, so the tests exercise the real file. Changing
        // the options in the script changes what these tests calculate.
        File.Copy(
            Path.Combine(sampleRoot, "scripts", "calculate-version.sh"),
            Path.Combine(directory, "scripts", "calculate-version.sh")
        );
        File.WriteAllText(Path.Combine(directory, ".gitignore"), ".tools/\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "changes.txt"), "seed\n", new UTF8Encoding(false));

        var toolDirectory = Path.Combine(directory, ".tools");
        var tool = Path.Combine(toolDirectory, OperatingSystem.IsWindows() ? "minver.exe" : "minver");
        var repository = new MinVerRepository(directory, FindBashExecutable(directory), tool);
        repository.Run("git", "init");
        repository.Run("git", "config", "user.email", "versioning-tests@example.com");
        repository.Run("git", "config", "user.name", "Versioning Tests");
        repository.Run("git", "checkout", "-b", "main");
        repository.Run("dotnet", "tool", "install", "--tool-path", toolDirectory, "minver-cli", "--version", MinVerVersion);
        repository.Run("git", "add", ".");
        repository.Run("git", "commit", "-m", "chore: initialize", "--no-verify");
        return repository;
    }

    // What a developer runs locally: the calculated version with no CI build suffix.
    public string CalculateVersion() =>
        ProcessRunner.Run(_directory, _bash, ["scripts/calculate-version.sh"], ToolEnvironment);

    // The raw tool, so the tests prove the script reports the calculated version rather
    // than inventing one. MinVer writes the version to stdout and its log to stderr.
    public string CalculateVersionWithMinVer() =>
        Run(_tool, "-t", "v", "-m", _trainMajorMinor, "-p", "preview").Trim();

    // MinVer takes the target line from the minimum major and minor option, so starting
    // a new train is a reviewed change to the script that holds the strategy. The fixture
    // models that review by rewriting the option in its copy of the script.
    public void StartNewVersionTrain(string branch, string majorMinor)
    {
        var scriptPath = Path.Combine(_directory, "scripts", "calculate-version.sh");
        var script = File.ReadAllText(scriptPath);
        const string marker = "minver_options=(-t v -m ";
        if (!script.Contains($"{marker}{_trainMajorMinor}"))
        {
            throw new InvalidOperationException(
                $"Expected the script to declare {marker}{_trainMajorMinor} ...)."
            );
        }

        File.WriteAllText(
            scriptPath,
            script.Replace($"{marker}{_trainMajorMinor}", $"{marker}{majorMinor}"),
            new UTF8Encoding(false)
        );
        _trainMajorMinor = majorMinor;
        Run("git", "checkout", "-b", branch);
    }

    // What the workflow does: the same script in its github-output mode.
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
        Run("git", "tag", "--points-at", "HEAD").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

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
    // commit all land on main. MinVer follows the first parent of a merge, so
    // height must still advance by exactly one however many commits the branch had.
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

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private bool IsOnBranch(string branch) => Run("git", "branch", "--show-current") == branch;
    private string Run(string file, params string[] arguments) => ProcessRunner.Run(_directory, file, arguments);

    private Dictionary<string, string> ToolEnvironment => new() { ["MINVER_CLI"] = _tool };

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
            var candidate = Path.Combine(directory.FullName, "scripts", "calculate-version.sh");
            if (File.Exists(candidate)) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find MinVer sample root.");
    }
}