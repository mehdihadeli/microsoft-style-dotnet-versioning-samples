using System.Text;
using System.Text.Json.Nodes;

namespace nerdbank_versioning_sample.Tests;

internal sealed class NbgvRepository : IDisposable
{
    private readonly string _directory;
    private readonly string _bash;
    private readonly string _tool;
    private readonly string _calculator;
    private readonly string _releaseHelper;

    private NbgvRepository(
        string directory,
        string bash,
        string tool,
        string calculator,
        string releaseHelper
    )
    {
        _directory = directory;
        _bash = bash;
        _tool = tool;
        _calculator = calculator;
        _releaseHelper = releaseHelper;
    }

    public static NbgvRepository Create()
    {
        var sampleRoot = FindSampleRoot();
        var directory = Path.Combine(Path.GetTempPath(), $"nbgv-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.Copy(
            Path.Combine(sampleRoot, "version.json"),
            Path.Combine(directory, "version.json")
        );
        File.Copy(
            Path.Combine(sampleRoot, "scripts", "calculate-version.sh"),
            Path.Combine(directory, "calculate-version.sh")
        );
        File.Copy(
            Path.Combine(sampleRoot, "release-version.sh"),
            Path.Combine(directory, "release-version.sh")
        );
        InitializePreviewVersion(directory);

        var bash = FindBashExecutable(directory);
        var toolDirectory = Path.Combine(directory, ".tools");
        var tool = Path.Combine(toolDirectory, OperatingSystem.IsWindows() ? "nbgv.exe" : "nbgv");
        var repository = new NbgvRepository(
            directory,
            bash,
            tool,
            "calculate-version.sh",
            "release-version.sh"
        );
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
            "nbgv",
            "--version",
            "3.10.94"
        );
        File.WriteAllText(Path.Combine(directory, ".gitignore"), ".tools/\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(directory, "changes.txt"), "seed\n", Encoding.UTF8);
        repository.Run("git", "add", ".");
        repository.Run("git", "commit", "-m", "chore: initialize", "--no-verify");
        return repository;

        static void InitializePreviewVersion(string path)
        {
            var versionPath = Path.Combine(path, "version.json");
            var version = JsonNode.Parse(File.ReadAllText(versionPath))!.AsObject();
            version["version"] = "1.0.0-preview.{height}";
            version["versionHeightOffset"] = -1;
            version["versionHeightOffsetAppliesTo"] = "1.0.0-preview.{height}";
            File.WriteAllText(versionPath, version.ToJsonString(new() { WriteIndented = true }));
        }
    }

    public string CalculateVersion() =>
        Run(
            _bash,
            _calculator.Replace('\\', '/'),
            _directory.Replace('\\', '/'),
            _tool.Replace('\\', '/')
        );

    public string CalculateVersionWithNbgv()
    {
        var version = Run(_tool, "get-version", "-v", "SemVer2");
        var metadataIndex = version.IndexOf(".g", StringComparison.Ordinal);
        return metadataIndex >= 0 ? version[..metadataIndex] : version;
    }

    public void PrepareVersionWithReleaseScript(string branch, string command, string version)
    {
        Run("git", "checkout", "-b", branch);
        RunReleaseVersionScript(command, version);
    }

    public void RunReleaseVersionScript(params string[] arguments) =>
        RunWithEnvironment(_bash, [_releaseHelper, .. arguments], ("NBGV_PATH", _tool));

    public void PrepareVersionWithNbgv(
        string branch,
        string version,
        bool removeVersionHeightOffset = false
    )
    {
        Run("git", "checkout", "-b", branch);
        Run(_tool, "set-version", version);
        if (removeVersionHeightOffset)
            RemoveVersionHeightOffset();
    }

    public void CreateNbgvTag() => Run(_tool, "tag");

    public void MergePullRequest(string branch, string message)
    {
        if (!IsOnBranch(branch))
            Run("git", "checkout", "-b", branch);
        File.AppendAllText(Path.Combine(_directory, "changes.txt"), $"{message}\n", Encoding.UTF8);
        Run("git", "add", "changes.txt", "version.json");
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "checkout", "main");
        Run("git", "merge", "--squash", branch);
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "branch", "-D", branch);
    }

    public void Tag(string tag) => Run("git", "tag", "-a", tag, "-m", tag);

    public string[] TagsAtHead() =>
        Run("git", "tag", "--points-at", "HEAD")
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );

    private void RemoveVersionHeightOffset()
    {
        var versionPath = Path.Combine(_directory, "version.json");
        var version = JsonNode.Parse(File.ReadAllText(versionPath))!.AsObject();
        version.Remove("versionHeightOffset");
        version.Remove("versionHeightOffsetAppliesTo");
        File.WriteAllText(versionPath, version.ToJsonString(new() { WriteIndented = true }));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { }
    }

    private bool IsOnBranch(string branch) => Run("git", "branch", "--show-current") == branch;

    private string Run(string file, params string[] arguments) =>
        ProcessRunner.Run(_directory, file, arguments);

    private void RunWithEnvironment(
        string file,
        string[] arguments,
        (string Name, string Value) environment
    ) => ProcessRunner.Run(_directory, file, arguments, environment);

    private static string FindBashExecutable(string directory)
    {
        if (!OperatingSystem.IsWindows())
            return "bash";
        var gitExecutable = ProcessRunner
            .Run(directory, "where.exe", ["git"])
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
        var gitRoot = Directory.GetParent(Path.GetDirectoryName(gitExecutable)!)!.FullName;
        return Path.Combine(gitRoot, "bin", "bash.exe");
    }

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
