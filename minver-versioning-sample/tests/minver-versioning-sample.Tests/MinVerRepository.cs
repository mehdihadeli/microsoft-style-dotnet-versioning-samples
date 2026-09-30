using System.Text;
using System.Text.RegularExpressions;

namespace minver_versioning_sample.Tests;

internal sealed class MinVerRepository : IDisposable
{
    private readonly string _directory;
    private readonly string _bash;
    private readonly string _tool;

    private MinVerRepository(string directory, string bash, string tool)
    {
        _directory = directory;
        _bash = bash;
        _tool = tool;
    }

    public static MinVerRepository Create()
    {
        var sampleRoot = FindSampleRoot();
        var directory = Path.Combine(Path.GetTempPath(), $"minver-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "scripts"));
        File.Copy(Path.Combine(sampleRoot, "version.env"), Path.Combine(directory, "version.env"));
        File.Copy(Path.Combine(sampleRoot, "release-version.sh"), Path.Combine(directory, "release-version.sh"));
        File.Copy(Path.Combine(sampleRoot, "scripts", "calculate-version.sh"), Path.Combine(directory, "scripts", "calculate-version.sh"));

        var toolDirectory = Path.Combine(directory, ".tools");
        var tool = Path.Combine(toolDirectory, OperatingSystem.IsWindows() ? "minver.exe" : "minver");
        var repository = new MinVerRepository(directory, FindBashExecutable(directory), tool);
        repository.Run("git", "init");
        repository.Run("git", "config", "user.email", "versioning-tests@example.com");
        repository.Run("git", "config", "user.name", "Versioning Tests");
        repository.Run("git", "checkout", "-b", "main");
        repository.Run("dotnet", "tool", "install", "--tool-path", toolDirectory, "minver-cli", "--version", "8.0.0");
        File.WriteAllText(Path.Combine(directory, ".gitignore"), ".tools/\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "changes.txt"), "seed\n", new UTF8Encoding(false));
        repository.Run("git", "add", ".");
        repository.Run("git", "commit", "-m", "chore: initialize", "--no-verify");
        return repository;
    }

    public string CalculateVersion() =>
        RunWithEnvironment(_bash, ["scripts/calculate-version.sh"], ("MINVER_CLI", _tool));

    public string CalculateVersionWithMinVer()
    {
        var values = File.ReadAllLines(Path.Combine(_directory, "version.env"))
            .Where(line => line.Contains('='))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1]);
        return Run(
            _tool,
            "-t", values["MINVER_TAG_PREFIX"],
            "-m", values["MINVER_MINIMUM_MAJOR_MINOR"],
            "-p", values["MINVER_DEFAULT_PRE_RELEASE_PHASE"]
        ).Split('+')[0];
    }

    public void PrepareVersionWithReleaseScript(string branch, string command, string version)
    {
        Run("git", "checkout", "-b", branch);
        RunReleaseVersionScript(command, version);
    }

    public void RunReleaseVersionScript(params string[] arguments) =>
        RunWithEnvironment(_bash, ["release-version.sh", .. arguments], ("MINVER_CLI", _tool));

    public void PrepareVersionDirectly(string branch, string phase, string version)
    {
        Run("git", "checkout", "-b", branch);
        var path = Path.Combine(_directory, "version.env");
        var content = File.ReadAllText(path);
        var majorMinor = string.Join('.', version.Split('.')[..2]);
        content = Regex.Replace(content, "(?m)^MINVER_MINIMUM_MAJOR_MINOR=.*$", $"MINVER_MINIMUM_MAJOR_MINOR={majorMinor}");
        content = Regex.Replace(content, "(?m)^MINVER_RELEASE_VERSION=.*$", $"MINVER_RELEASE_VERSION={version}");
        content = Regex.Replace(content, "(?m)^MINVER_RELEASE_PHASE=.*$", $"MINVER_RELEASE_PHASE={phase}");
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    public void Tag(string tag) => Run("git", "tag", "-a", tag, "-m", tag);

    public string[] TagsAtHead() =>
        Run("git", "tag", "--points-at", "HEAD").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    public void MergePullRequest(string branch, string message)
    {
        if (!IsOnBranch(branch))
            Run("git", "checkout", "-b", branch);
        File.AppendAllText(Path.Combine(_directory, "changes.txt"), $"{message}\n", new UTF8Encoding(false));
        Run("git", "add", "changes.txt", "version.env");
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "checkout", "main");
        Run("git", "merge", "--squash", branch);
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "branch", "-D", branch);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private bool IsOnBranch(string branch) => Run("git", "branch", "--show-current") == branch;
    private string Run(string file, params string[] arguments) => ProcessRunner.Run(_directory, file, arguments);
    private string RunWithEnvironment(string file, string[] arguments, (string Name, string Value) environment) =>
        ProcessRunner.Run(_directory, file, arguments, environment);

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
            if (File.Exists(Path.Combine(directory.FullName, "version.env"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find MinVer sample root.");
    }
}