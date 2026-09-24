using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace semantic_release_versioning_sample.Tests;

internal sealed class SemanticReleaseRepository : IDisposable
{
    private readonly string _directory;
    private readonly string _remoteDirectory;
    private readonly string _npx;

    private SemanticReleaseRepository(string directory, string remoteDirectory, string npx)
    {
        _directory = directory;
        _remoteDirectory = remoteDirectory;
        _npx = npx;
    }

    public static SemanticReleaseRepository Create(
        string dependencyDirectory,
        string npx,
        string stableVersion
    )
    {
        var sampleRoot = FindSampleRoot();
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"semantic-release-tests-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(directory);

        foreach (var fileName in new[] { "package.json", "package-lock.json" })
        {
            File.Copy(Path.Combine(sampleRoot, fileName), Path.Combine(directory, fileName));
        }
        File.WriteAllText(
            Path.Combine(directory, "release.config.cjs"),
            "module.exports = { branches: ['main'], tagFormat: 'v${version}', plugins: [['@semantic-release/commit-analyzer', { releaseRules: [{ breaking: true, release: 'major' }, { type: 'feat', release: 'patch' }] }], '@semantic-release/release-notes-generator'] };"
        );
        LinkNodeModules(directory, dependencyDirectory);

        var remoteDirectory = Path.Combine(
            Path.GetTempPath(),
            $"semantic-release-remote-{Guid.NewGuid():N}.git"
        );

        var repository = new SemanticReleaseRepository(directory, remoteDirectory, npx);
        repository.Run("git", "init");
        repository.Run("git", "config", "user.email", "versioning-tests@example.com");
        repository.Run("git", "config", "user.name", "Versioning Tests");
        repository.Run("git", "checkout", "-b", "main");
        repository.Run("git", "init", "--bare", remoteDirectory);
        repository.Run("git", "remote", "add", "origin", remoteDirectory);
        File.WriteAllText(Path.Combine(directory, "changes.txt"), "seed\n", Encoding.UTF8);
        repository.Run("git", "add", ".");
        repository.Run("git", "commit", "-m", "chore: initialize", "--no-verify");
        repository.Run("git", "tag", $"v{stableVersion}");
        repository.Run("git", "push", "--set-upstream", "origin", "main", "--tags");
        return repository;
    }

    public void Commit(string message)
    {
        File.AppendAllText(Path.Combine(_directory, "changes.txt"), $"{message}\n", Encoding.UTF8);
        Run("git", "add", "changes.txt");
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "push", "origin", "main");
    }

    public void MergePullRequest(string branch, string message)
    {
        Run("git", "checkout", "-b", branch);
        File.AppendAllText(Path.Combine(_directory, "changes.txt"), $"{message}\n", Encoding.UTF8);
        Run("git", "add", "changes.txt");
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "checkout", "main");
        Run("git", "merge", "--squash", branch);
        Run("git", "commit", "-m", message, "--no-verify");
        Run("git", "branch", "-D", branch);
        Run("git", "push", "origin", "main");
    }

    public void Tag(string tag)
    {
        Run("git", "tag", "-a", tag, "-m", tag);
        Run("git", "push", "origin", tag);
    }

    public string GetCiVersion(int runNumber)
    {
        var tags = Run("git", "tag", "--points-at", "HEAD")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(value => Regex.IsMatch(value, @"^v\d+\.\d+\.\d+(?:-rc\.\d+)?$"))
            .OrderByDescending(GetReleaseTagRank)
            .ToArray();
        if (tags.Length > 0)
            return tags[0][1..];

        var output = Run(_npx, "--no-install", "semantic-release", "--dry-run", "--no-ci");
        var match = Regex.Match(
            output,
            @"The next release version is (?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)",
            RegexOptions.IgnoreCase
        );
        if (!match.Success)
            throw new InvalidOperationException(
                $"Could not find next release version in:\n{output}"
            );

        return $"{match.Groups["version"].Value}-preview.{runNumber}";
    }

    private static (int ReleaseKind, int CandidateNumber) GetReleaseTagRank(string tag)
    {
        if (Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$"))
            return (2, 0);

        return (1, int.Parse(Regex.Match(tag, @"-rc\.(\d+)$").Groups[1].Value));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch { }
        try
        {
            Directory.Delete(_remoteDirectory, recursive: true);
        }
        catch { }
    }

    private string Run(string fileName, params string[] arguments) =>
        RunProcessAsync(fileName, _directory, arguments).GetAwaiter().GetResult();

    public static async Task<string> RunProcessAsync(
        string fileName,
        string workingDirectory,
        params string[] arguments
    )
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} {string.Join(' ', arguments)} failed.\n{output}\n{error}"
            );
        }

        return $"{output}\n{error}".Trim();
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
            RunProcessAsync(
                    "cmd.exe",
                    directory,
                    "/c",
                    "mklink",
                    "/J",
                    nodeModules,
                    Path.Combine(dependencyDirectory, "node_modules")
                )
                .GetAwaiter()
                .GetResult();
            return;
        }

        Directory.CreateSymbolicLink(
            nodeModules,
            Path.Combine(dependencyDirectory, "node_modules")
        );
    }
}
