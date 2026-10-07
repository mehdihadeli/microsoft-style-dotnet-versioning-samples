using System.Diagnostics;

namespace arcade_versioning_sample.Tests;

internal static class ProcessRunner
{
    public static string Run(string directory, string fileName, params string[] arguments) =>
        Run(directory, fileName, arguments, null);

    // The Arcade adapter reads GITHUB_* and OFFICIAL_BUILD_ID, so every inherited value is
    // removed first; a developer machine that happens to export one cannot change a result.
    public static string Run(
        string directory,
        string fileName,
        string[] arguments,
        IReadOnlyDictionary<string, string>? environment
    )
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var name in new[] { "GITHUB_ACTIONS", "GITHUB_REF", "GITHUB_REF_NAME", "GITHUB_RUN_NUMBER", "OFFICIAL_BUILD_ID" })
            process.StartInfo.Environment.Remove(name);
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (name, value) in environment)
                process.StartInfo.Environment[name] = value;
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{fileName} {string.Join(' ', arguments)} failed.\n{output}\n{error}"
            );
        return output.Trim();
    }
}
