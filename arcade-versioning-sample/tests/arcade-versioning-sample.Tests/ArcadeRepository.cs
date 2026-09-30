using System.Text;
using System.Text.RegularExpressions;

namespace arcade_versioning_sample.Tests;

internal sealed class ArcadeRepository : IDisposable
{
    private readonly string _directory;
    private readonly string _bash;

    private ArcadeRepository(string directory, string bash) => (_directory, _bash) = (directory, bash);

    public static ArcadeRepository Create()
    {
        var root = FindSampleRoot();
        var directory = Path.Combine(Path.GetTempPath(), $"arcade-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "scripts"));
        File.Copy(Path.Combine(root, "Directory.Build.props"), Path.Combine(directory, "Directory.Build.props"));
        File.Copy(Path.Combine(root, "release-version.sh"), Path.Combine(directory, "release-version.sh"));
        File.Copy(Path.Combine(root, "scripts", "calculate-version.sh"), Path.Combine(directory, "scripts", "calculate-version.sh"));
        File.WriteAllText(
            Path.Combine(directory, "version.proj"),
            "<Project><Import Project=\"Directory.Build.props\" /></Project>\n",
            new UTF8Encoding(false)
        );
        var repository = new ArcadeRepository(directory, FindBashExecutable(directory));
        repository.Run("git", "init");
        repository.Run("git", "config", "user.email", "versioning-tests@example.com");
        repository.Run("git", "config", "user.name", "Versioning Tests");
        repository.Run("git", "checkout", "-b", "main");
        File.WriteAllText(Path.Combine(directory, "changes.txt"), "seed\n", new UTF8Encoding(false));
        repository.Run("git", "add", ".");
        repository.Run("git", "commit", "-m", "chore: initialize", "--no-verify");
        return repository;
    }

    public string CalculateVersion() =>
        ProcessRunner.Run(_directory, _bash, ["scripts/calculate-version.sh"], ("GITHUB_RUN_NUMBER", string.Empty));

    public string CalculateVersionWithMsBuild()
    {
        var props = System.Xml.Linq.XDocument.Load(Path.Combine(_directory, "Directory.Build.props"));
        var prefix = props.Descendants("VersionPrefix").Single().Value;
        var tags = Run("git", "tag", "--merged", "HEAD", "--list", $"v{prefix}-rc.*");
        var nextRc = tags.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => int.Parse(tag[(tag.LastIndexOf('.') + 1)..])).DefaultIfEmpty(0).Max() + 1;
        var trainCommit = Run("git", "log", "-1", "--format=%H", "--", "Directory.Build.props");
        var initialCommit = Run("git", "rev-list", "--max-parents=0", "HEAD");
        var height = int.Parse(Run("git", "rev-list", "--count", $"{trainCommit}..HEAD"));
        if (trainCommit != initialCommit) height++;
        return Run("dotnet", "msbuild", "version.proj", "-getProperty:Version",
            $"-p:GITHUB_RUN_NUMBER={height}", $"-p:ReleaseCandidateNumber={nextRc}");
    }

    public void PrepareVersionWithReleaseScript(string branch, string command, string version)
    {
        Run("git", "checkout", "-b", branch);
        Run(_bash, "release-version.sh", command, version);
    }

    public void RunReleaseVersionScript(params string[] arguments) => Run(_bash, ["release-version.sh", .. arguments]);

    public void PrepareVersionWithMsBuild(string branch, string phase, string version)
    {
        Run("git", "checkout", "-b", branch);
        var path = Path.Combine(_directory, "Directory.Build.props");
        var content = File.ReadAllText(path);
        content = Regex.Replace(
            content,
            "(<VersionPrefix[^>]*>)[^<]*(</VersionPrefix>)",
            match => $"{match.Groups[1].Value}{version}{match.Groups[2].Value}"
        );
        content = Regex.Replace(
            content,
            "(<ReleasePhase[^>]*>)[^<]*(</ReleasePhase>)",
            match => $"{match.Groups[1].Value}{phase}{match.Groups[2].Value}"
        );
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    public void MergePullRequest(string branch, string message)
    {
        if (!IsOnBranch(branch)) Run("git", "checkout", "-b", branch);
        File.AppendAllText(Path.Combine(_directory, "changes.txt"), $"{message}\n", new UTF8Encoding(false));
        Run("git", "add", "changes.txt", "Directory.Build.props");
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
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find Arcade sample root.");
    }
}