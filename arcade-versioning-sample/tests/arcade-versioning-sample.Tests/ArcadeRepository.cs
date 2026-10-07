using System.Text;
using System.Text.RegularExpressions;

namespace arcade_versioning_sample.Tests;

internal sealed class ArcadeRepository : IDisposable
{
    // Arcade's official build number has the shape <yyyyMMdd>.<revision>, and the SDK turns it
    // into the SHORT_DATE.revision suffix on its own. Pinning it keeps the calculated versions
    // deterministic; the tests that exercise the real CI path leave it unset and match the date
    // stamp with a pattern instead.
    private const string PinnedBuildId = "20260101.7";

    private readonly string _directory;
    private readonly string _bash;

    // The committed release intent, mirrored here so the fixture can also ask Arcade directly
    // for the version the adapter is expected to report.
    private string _versionPrefix = "1.0.0";
    private string _releaseLabel = "preview";

    private ArcadeRepository(string directory, string bash) => (_directory, _bash) = (directory, bash);

    public static ArcadeRepository Create()
    {
        var sampleRoot = FindSampleRoot();
        var directory = Path.Combine(Path.GetTempPath(), $"arcade-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "eng"));
        Directory.CreateDirectory(Path.Combine(directory, "scripts"));
        // Arcade resolves global.json, eng/Versions.props, and the toolset wiring from the
        // repository root, so the fixture reproduces that layout and runs the real files.
        // Changing them changes what these tests calculate.
        foreach (var relativePath in new[]
        {
            "global.json",
            "NuGet.config",
            "Directory.Build.props",
            "Directory.Build.targets",
            "version.proj",
            Path.Combine("eng", "Versions.props"),
            Path.Combine("scripts", "calculate-version.sh"),
        })
        {
            File.Copy(Path.Combine(sampleRoot, relativePath), Path.Combine(directory, relativePath));
        }
        // Arcade writes every output under artifacts/, which git must not pick up.
        File.WriteAllText(Path.Combine(directory, ".gitignore"), "artifacts/\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "changes.txt"), "seed\n", new UTF8Encoding(false));

        var repository = new ArcadeRepository(directory, FindBashExecutable(directory));
        repository.Run("git", "init");
        repository.Run("git", "config", "user.email", "versioning-tests@example.com");
        repository.Run("git", "config", "user.name", "Versioning Tests");
        repository.Run("git", "checkout", "-b", "main");
        repository.Run("git", "add", ".");
        repository.Run("git", "commit", "-m", "chore: initialize", "--no-verify");
        return repository;
    }

    // What a developer or a workflow runs: the adapter with no CI environment, so the pinned
    // build id stands in for the CI build number and the version is stable enough to assert.
    public string CalculateVersion() =>
        ProcessRunner.Run(
            _directory,
            _bash,
            ["scripts/calculate-version.sh"],
            new Dictionary<string, string> { ["OFFICIAL_BUILD_ID"] = PinnedBuildId }
        );

    // Arcade itself, so the tests prove the adapter reports what the SDK calculated rather than
    // inventing a version. This supplies the same inputs the adapter derives from Git.
    public string CalculateVersionWithArcade(string officialBuildId = PinnedBuildId)
    {
        var inputs = DerivedInputs();
        var arguments = new List<string>
        {
            "msbuild",
            "version.proj",
            "-getProperty:Version",
            "-p:ContinuousIntegrationBuild=true",
            $"-p:OfficialBuildId={officialBuildId}",
            $"-p:PreReleaseVersionLabel={inputs.Label}",
            $"-p:PreReleaseVersionIteration={inputs.Iteration}",
        };
        if (inputs.FinalVersionKind is not null) arguments.Add($"-p:DotNetFinalVersionKind={inputs.FinalVersionKind}");
        if (inputs.ReleaseVersion is not null) arguments.Add($"-p:ReleaseVersion={inputs.ReleaseVersion}");
        return Run("dotnet", [.. arguments]);
    }

    // What the workflow does: the same adapter in its github-output mode. OfficialBuildId is
    // left to the adapter, so the date stamp is the real one and assertions match a pattern.
    public Dictionary<string, string> CalculateCiOutput(string gitRef, string runNumber)
    {
        var environment = new Dictionary<string, string>
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

    // Arcade has no release helper script. The release intent is committed, so starting a train
    // or moving a train into release-candidate hardening is a reviewed edit to eng/Versions.props.
    public void PrepareVersion(string branch, string versionPrefix, string releaseLabel)
    {
        _versionPrefix = versionPrefix;
        _releaseLabel = releaseLabel;
        var path = Path.Combine(_directory, "eng", "Versions.props");
        var content = File.ReadAllText(path);
        content = Regex.Replace(
            content,
            "(<VersionPrefix[^>]*>)[^<]*(</VersionPrefix>)",
            match => $"{match.Groups[1].Value}{versionPrefix}{match.Groups[2].Value}"
        );
        content = Regex.Replace(
            content,
            "(<PreReleaseVersionLabel[^>]*>)[^<]*(</PreReleaseVersionLabel>)",
            match => $"{match.Groups[1].Value}{releaseLabel}{match.Groups[2].Value}"
        );
        File.WriteAllText(path, content, new UTF8Encoding(false));
        Run("git", "checkout", "-b", branch);
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

    // GitHub's "Create a merge commit" option: the branch commits and a merge commit all land on
    // main. Arcade counts commits rather than following first parents, so the preview number
    // advances by every commit the pull request brought with it.
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

    // Mirrors the adapter's inputs: a release tag owns the version, the committed rc label
    // supplies the RC ordinal, and anything else advances the preview by commit height.
    private (string Label, string Iteration, string? FinalVersionKind, string? ReleaseVersion) DerivedInputs()
    {
        var releaseTag = ReleaseTagAtHead();
        if (releaseTag is not null)
        {
            var releaseVersion = releaseTag[1..];
            if (releaseTag.Contains("-rc."))
                return ("rc", releaseTag[(releaseTag.LastIndexOf('.') + 1)..], null, releaseVersion);
            return (_releaseLabel, string.Empty, "release", releaseVersion);
        }
        if (_releaseLabel == "rc") return ("rc", NextReleaseCandidateNumber().ToString(), null, null);
        return ("preview", Height().ToString(), null, null);
    }

    // A stable tag outranks an RC tag on the same commit, which is how a validated candidate is
    // promoted to stable without being rebuilt.
    private string? ReleaseTagAtHead()
    {
        var tags = TagsAtHead();
        var stable = tags.Where(tag => Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$")).ToList();
        if (stable.Count > 0) return stable.OrderBy(tag => tag, StringComparer.Ordinal).Last();
        var candidates = tags.Where(tag => Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+-rc\.\d+$")).ToList();
        return candidates.Count > 0 ? candidates.OrderBy(tag => tag, StringComparer.Ordinal).Last() : null;
    }

    private int NextReleaseCandidateNumber() =>
        Run("git", "tag", "--merged", "HEAD", "--list", $"v{_versionPrefix}-rc.*")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => int.Parse(tag[(tag.LastIndexOf('.') + 1)..]))
            .DefaultIfEmpty(0)
            .Max() + 1;

    // A preview advances per commit and restarts when eng/Versions.props, which holds the train,
    // is edited on a release-preparation branch.
    private int Height()
    {
        var trainCommit = Run("git", "log", "-1", "--format=%H", "--", "eng/Versions.props");
        var initialCommit = Run("git", "rev-list", "--max-parents=0", "HEAD");
        var height = int.Parse(Run("git", "rev-list", "--count", $"{trainCommit}..HEAD"));
        return trainCommit == initialCommit ? height : height + 1;
    }

    private bool IsOnBranch(string branch) => Run("git", "branch", "--show-current") == branch;
    private string Run(string file, params string[] arguments) => ProcessRunner.Run(_directory, file, arguments);

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
            var candidate = Path.Combine(directory.FullName, "eng", "Versions.props");
            if (File.Exists(candidate)) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find Arcade sample root.");
    }
}
