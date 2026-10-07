using System.Diagnostics;

namespace semantic_release_versioning_sample.Tests;

internal static class ProcessRunner
{
    public static string Run(string directory, string fileName, params string[] arguments) =>
        Run(directory, fileName, arguments, null);

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
        foreach (var name in new[] { "GITHUB_ACTIONS", "GITHUB_REF", "GITHUB_REF_NAME", "GITHUB_RUN_NUMBER" })
            process.StartInfo.Environment.Remove(name);
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (name, value) in environment)
                process.StartInfo.Environment[name] = value;
        process.Start();
        // semantic-release logs every analyzed commit, so both streams must be drained
        // while the process runs. Reading one to the end before the other deadlocks as
        // soon as the unread pipe buffer fills.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{fileName} {string.Join(' ', arguments)} failed.\n{output}\n{error}"
            );
        return output.Trim();
    }
}
