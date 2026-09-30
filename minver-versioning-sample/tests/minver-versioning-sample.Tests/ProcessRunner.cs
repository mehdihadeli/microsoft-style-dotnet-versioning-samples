using System.Diagnostics;

namespace minver_versioning_sample.Tests;

internal static class ProcessRunner
{
    public static string Run(string directory, string fileName, params string[] arguments) =>
        Run(directory, fileName, arguments, null);

    public static string Run(
        string directory,
        string fileName,
        string[] arguments,
        (string Name, string Value)? environment
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
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        if (environment is { } value)
            process.StartInfo.Environment[value.Name] = value.Value;
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