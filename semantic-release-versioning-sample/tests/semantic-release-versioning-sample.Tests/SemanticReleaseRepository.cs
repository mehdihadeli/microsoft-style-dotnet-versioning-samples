using System.Text;

namespace semantic_release_versioning_sample.Tests;

internal sealed class SemanticReleaseRepository : IDisposable
{
    // semantic-release resolves its plugins from node_modules below the working directory.
    // Installing once and linking that folder into every scratch repository keeps the
    // suite fast while still running the real release.config.cjs.
    private static readonly Lazy<string> NodeModules = new(InstallDependencies);

    private readonly string _directory;
    private readonly string _remoteDirectory;
    private readonly string _bash;

    private SemanticReleaseRepository(string directory, string remoteDirectory, string bash) =>
        (_directory, _remoteDirectory, _bash) = (directory, remoteDirectory, bash);

    public static SemanticReleaseRepository Create()
    {
        var sampleRoot = FindSampleRoot();
        var directory = Path.Combine(Path.GetTempPath(), $"semantic-release-tests-{Guid.NewGuid():N}");
        var remoteDirectory = Path.Combine(Path.GetTempPath(), $"semantic-release-remote-{Guid.NewGuid():N}.git");
        Directory.CreateDirectory(Path.Combine(directory, "scripts"));
        // The script holds the Git-side rules and release.config.cjs holds the commit
        // analysis strategy, so the tests exercise the real files. Changing either file
        // changes what these tests calculate.
        foreach (var fileName in new[] { "package.json", "package-lock.json", "release.config.cjs" })
            File.Copy(Path.Combine(sampleRoot, fileName), Path.Combine(directory, fileName));
        foreach (var fileName in new[] { "calculate-version.sh", "calculate-version.mjs" })
            File.Copy(Path.Combine(sampleRoot, "scripts", fileName), Path.Combine(directory, "scripts", fileName));
        File.WriteAllText(Path.Combine(directory, ".gitignore"), "node_modules/\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "changes.txt"), "seed\n", new UTF8Encoding(false));
        LinkNodeModules(directory);

        // semantic-release requires a repository URL and reads it from the git origin.
        ProcessRunner.Run(directory, "git", "init", "--bare", "--initial-branch=main", remoteDirectory);
        var repository = new SemanticReleaseRepository(directory, remoteDirectory, FindBashExecutable(directory));
        repository.Run("git", "init");
        repository.Run("git", "config", "user.email", "versioning-tests@example.com");
        repository.Run("git", "config", "user.name", "Versioning Tests");
        repository.Run("git", "checkout", "-b", "main");
        repository.Run("git", "remote", "add", "origin", remoteDirectory);
        repository.Run("git", "add", ".");
        repository.Run("git", "commit", "-m", "chore: initialize", "--no-verify");
        repository.Run("git", "push", "--set-upstream", "origin", "main");
        return repository;
    }

    // What a developer runs locally: the calculated version with no CI build suffix.
    public string CalculateVersion() =>
        ProcessRunner.Run(_directory, _bash, ["scripts/calculate-version.sh"]);

    // The raw commit analysis, so the tests prove the script reports the base semantic-release
    // calculated rather than one it invented. Empty means semantic-release declared no
    // release for these commits.
    public string CalculateVersionWithSemanticRelease() =>
        ProcessRunner.Run(_directory, FindNodeExecutable(), ["scripts/calculate-version.mjs"]);

    // What the workflow does: the same script in its github-output mode.
    public Dictionary<string, string> CalculateCiOutput(string gitRef, string runNumber) =>
        Parse(ProcessRunner.Run(
            _directory,
            _bash,
            ["scripts/calculate-version.sh", "github-output"],
            new Dictionary<string, string>
            {
                ["GITHUB_REF"] = gitRef,
                ["GITHUB_REF_NAME"] = gitRef.Replace("refs/heads/", "").Replace("refs/tags/", ""),
                ["GITHUB_RUN_NUMBER"] = runNumber,
            }
        ));

    public void Tag(string tag)
    {
        Run("git", "tag", "-a", tag, "-m", tag);
        Run("git", "push", "origin", tag);
    }

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
        Run("git", "push", "origin", "main");
    }

    // GitHub's "Create a merge commit" option: the branch commits and a merge commit all
    // land on main. The preview ordinal counts every commit since the train baseline, so
    // it advances by the branch commit count plus the merge commit itself.
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
        Run("git", "push", "origin", "main");
    }

    public void Dispose()
    {
        // The node_modules link is removed before the recursive delete so the shared
        // install can never be followed and wiped.
        var link = Path.Combine(_directory, "node_modules");
        if (Directory.Exists(link))
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    ProcessRunner.Run(_directory, "cmd.exe", ["/c", "rmdir", link]);
                else
                    Directory.Delete(link);
            }
            catch (Exception exception)
                when (exception is InvalidOperationException or IOException or UnauthorizedAccessException) { }
        }

        DeleteDirectory(_directory);
        DeleteDirectory(_remoteDirectory);
    }

    private bool IsOnBranch(string branch) => Run("git", "branch", "--show-current") == branch;
    private string Run(string file, params string[] arguments) => ProcessRunner.Run(_directory, file, arguments);

    private static Dictionary<string, string> Parse(string output) =>
        output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1]);

    private static string InstallDependencies()
    {
        var sampleRoot = FindSampleRoot();
        var directory = Path.Combine(Path.GetTempPath(), "semantic-release-dependencies");
        var nodeModules = Path.Combine(directory, "node_modules");
        if (!Directory.Exists(nodeModules))
        {
            Directory.CreateDirectory(directory);
            foreach (var fileName in new[] { "package.json", "package-lock.json" })
                File.Copy(Path.Combine(sampleRoot, fileName), Path.Combine(directory, fileName), overwrite: true);
            ProcessRunner.Run(
                directory,
                FindExecutable(OperatingSystem.IsWindows() ? "npm.cmd" : "npm"),
                ["ci", "--ignore-scripts", "--no-audit", "--no-fund"]
            );
        }
        return nodeModules;
    }

    private static void LinkNodeModules(string directory)
    {
        var nodeModules = NodeModules.Value;
        var link = Path.Combine(directory, "node_modules");
        if (OperatingSystem.IsWindows())
            ProcessRunner.Run(directory, "cmd.exe", ["/c", "mklink", "/J", link, nodeModules]);
        else
            Directory.CreateSymbolicLink(link, nodeModules);
    }

    private static void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    internal static string SampleRoot { get; } = FindSampleRoot();

    internal static string RepositoryRoot { get; } = Directory.GetParent(SampleRoot)!.FullName;

    private static string FindNodeExecutable() => FindExecutable(OperatingSystem.IsWindows() ? "node.exe" : "node");

    private static string FindSampleRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "release.config.cjs")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find semantic-release sample root.");
    }

    private static string FindExecutable(string fileName)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Could not find '{fileName}' on PATH.");
    }

    private static string FindBashExecutable(string directory)
    {
        if (!OperatingSystem.IsWindows()) return "bash";
        var git = ProcessRunner
            .Run(directory, "where.exe", "git")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
        return Path.Combine(Directory.GetParent(Path.GetDirectoryName(git)!)!.FullName, "bin", "bash.exe");
    }
}
