using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace semantic_release_versioning_sample.Tests;

internal sealed class SemanticReleaseRepository : IDisposable
{
    private readonly string _directory;
    private readonly string _remoteDirectory;
    private readonly string _npx;
    private readonly string _bash;

    private SemanticReleaseRepository(string directory, string remoteDirectory, string npx, string bash)
    {
        _directory = directory;
        _remoteDirectory = remoteDirectory;
        _npx = npx;
        _bash = bash;
    }

    public static SemanticReleaseRepository Create(string dependencyDirectory, string npx, string stableVersion)
    {
        var sampleRoot = FindSampleRoot();
        var directory = Path.Combine(Path.GetTempPath(), $"semantic-release-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "scripts"));
        foreach (var fileName in new[] { "package.json", "package-lock.json", "release.env", "release-version.sh" })
            File.Copy(Path.Combine(sampleRoot, fileName), Path.Combine(directory, fileName));
        File.Copy(Path.Combine(sampleRoot, "scripts", "calculate-version.mjs"), Path.Combine(directory, "scripts", "calculate-version.mjs"));
        File.Copy(Path.Combine(sampleRoot, "scripts", "calculate-version.sh"), Path.Combine(directory, "scripts", "calculate-version.sh"));
        File.WriteAllText(Path.Combine(directory, "release.config.cjs"),
            "module.exports = { branches: ['main'], tagFormat: 'v${version}', plugins: ['@semantic-release/commit-analyzer', '@semantic-release/release-notes-generator'] };");
        LinkNodeModules(directory, dependencyDirectory);

        var remoteDirectory = Path.Combine(Path.GetTempPath(), $"semantic-release-remote-{Guid.NewGuid():N}.git");
        var repository = new SemanticReleaseRepository(directory, remoteDirectory, npx, FindBashExecutable(directory));
        repository.Run("git", "init");
        repository.Run("git", "config", "user.email", "versioning-tests@example.com");
        repository.Run("git", "config", "user.name", "Versioning Tests");
        repository.Run("git", "checkout", "-b", "main");
        repository.Run("git", "init", "--bare", remoteDirectory);
        repository.Run("git", "remote", "add", "origin", remoteDirectory);
        File.WriteAllText(Path.Combine(directory, "changes.txt"), "seed\n", new UTF8Encoding(false));
        repository.Run("git", "add", ".");
        repository.Run("git", "commit", "-m", "chore: initialize", "--no-verify");
        repository.Run("git", "tag", $"v{stableVersion}");
        repository.Run("git", "push", "--set-upstream", "origin", "main", "--tags");
        return repository;
    }

    public void MergePullRequest(string branch, string message)
    {
        if (Run("git", "branch", "--show-current") != branch)
            Run("git", "checkout", "-b", branch);
        File.AppendAllText(Path.Combine(_directory, "changes.txt"), $"{message}\n", new UTF8Encoding(false));
        Run("git", "add", "changes.txt", "release.env");
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "checkout", "main");
        Run("git", "merge", "--squash", branch);
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "branch", "-D", branch);
        Run("git", "push", "origin", "main");
    }

    public void PrepareVersionWithReleaseScript(string branch, string command, string version)
    {
        Run("git", "checkout", "-b", branch);
        Run(_bash, "release-version.sh", command, version);
    }

    public void RunReleaseVersionScript(params string[] arguments)
    {
        Run(_bash, ["release-version.sh", .. arguments]);
        foreach (var tag in TagsAtHead())
            Run("git", "push", "origin", tag);
    }

    public void PrepareVersionDirectly(string branch, string phase, string version)
    {
        Run("git", "checkout", "-b", branch);
        var path = Path.Combine(_directory, "release.env");
        var content = File.ReadAllText(path);
        content = Regex.Replace(content, "(?m)^SEMANTIC_RELEASE_VERSION=.*$", $"SEMANTIC_RELEASE_VERSION={version}");
        content = Regex.Replace(content, "(?m)^SEMANTIC_RELEASE_PHASE=.*$", $"SEMANTIC_RELEASE_PHASE={phase}");
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    public void Tag(string tag)
    {
        Run("git", "tag", "-a", tag, "-m", tag);
        Run("git", "push", "origin", tag);
    }

    public string[] TagsAtHead() => Run("git", "tag", "--points-at", "HEAD")
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    public string GetCiVersion()
    {
        var tag = TagsAtHead().FirstOrDefault(value =>
            value != "v0.0.0" && Regex.IsMatch(value, @"^v\d+\.\d+\.\d+(?:-rc\.\d+)?$"));
        if (tag is not null)
            return tag[1..];
        var output = Run(_npx, "--no-install", "semantic-release", "--dry-run", "--no-ci");
        var match = Regex.Match(output, @"The next release version is (?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)", RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new InvalidOperationException($"Could not find next release version in:\n{output}");
        var version = match.Groups["version"].Value;
        var intent = File.ReadAllLines(Path.Combine(_directory, "release.env"))
            .Where(line => line.Contains('='))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1]);
        if (intent["SEMANTIC_RELEASE_PHASE"] == "stable") return version;
        if (intent["SEMANTIC_RELEASE_PHASE"] == "rc")
        {
            var tags = Run("git", "tag", "--merged", "HEAD", "--list", $"v{version}-rc.*");
            var nextRc = tags.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.Parse(value[(value.LastIndexOf('.') + 1)..])).DefaultIfEmpty(0).Max() + 1;
            return $"{version}-rc.{nextRc}";
        }
        var trainCommit = Run("git", "log", "-1", "--format=%H", "--", "release.env");
        var initialCommit = Run("git", "rev-list", "--max-parents=0", "HEAD");
        var height = int.Parse(Run("git", "rev-list", "--count", $"{trainCommit}..HEAD"));
        if (trainCommit != initialCommit) height++;
        return $"{version}-preview.{height}";
    }

    public string GetNextVersionWithSemanticRelease()
    {
        var version = Run("node", "scripts/calculate-version.mjs")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .SingleOrDefault(line => Regex.IsMatch(line, @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$")) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("semantic-release did not calculate a next release.");
        return version;
    }

    public string GetCalculatorVersion()
    {
        var tag = TagsAtHead().FirstOrDefault(value =>
            value != "v0.0.0" && Regex.IsMatch(value, @"^v\d+\.\d+\.\d+(?:-rc\.\d+)?$"));
        var output = RunWithEnvironment(
            _bash,
            ["scripts/calculate-version.sh", "github-output"],
            ("GITHUB_REF", tag is null ? "refs/heads/main" : $"refs/tags/{tag}"),
            ("GITHUB_REF_NAME", tag ?? "main"),
            ("GITHUB_RUN_NUMBER", string.Empty)
        );
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("version=", StringComparison.Ordinal))["version=".Length..];
    }

    public void Dispose()
    {
        DeleteDirectory(_directory);
        DeleteDirectory(_remoteDirectory);
    }

    private string Run(string fileName, params string[] arguments) => RunProcessAsync(fileName, _directory, arguments).GetAwaiter().GetResult();

    private string RunWithEnvironment(string fileName, string[] arguments, params (string Name, string Value)[] variables) =>
        RunProcessAsync(fileName, _directory, arguments, variables).GetAwaiter().GetResult();

    public static async Task<string> RunProcessAsync(string fileName, string workingDirectory, string[] arguments, params (string Name, string Value)[] variables)
    {
        var startInfo = new ProcessStartInfo {
            FileName = fileName, WorkingDirectory = workingDirectory, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        using var process = new Process();
        process.StartInfo = startInfo;
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        foreach (var variable in variables)
            process.StartInfo.Environment[variable.Name] = variable.Value;
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} {string.Join(' ', arguments)} failed.\n{output}\n{error}");
        return $"{output}\n{error}".Trim();
    }

    public static Task<string> RunProcessAsync(string fileName, string workingDirectory, params string[] arguments) =>
        RunProcessAsync(fileName, workingDirectory, arguments, []);

    private static void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    public static string FindSampleRoot()
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

    public static string FindExecutable(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException($"Could not find '{fileName}' on PATH.");
    }

    private static void LinkNodeModules(string directory, string dependencyDirectory)
    {
        var nodeModules = Path.Combine(directory, "node_modules");
        if (OperatingSystem.IsWindows())
        {
            RunProcessAsync("cmd.exe", directory, "/c", "mklink", "/J", nodeModules, Path.Combine(dependencyDirectory, "node_modules"))
                .GetAwaiter().GetResult();
            return;
        }
        Directory.CreateSymbolicLink(nodeModules, Path.Combine(dependencyDirectory, "node_modules"));
    }

    private static string FindBashExecutable(string directory)
    {
        if (!OperatingSystem.IsWindows())
            return "bash";
        var gitExecutable = RunProcessAsync("where.exe", directory, ["git"]).GetAwaiter().GetResult()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
        var gitRoot = Directory.GetParent(Path.GetDirectoryName(gitExecutable)!)!.FullName;
        return Path.Combine(gitRoot, "bin", "bash.exe");
    }
}
