using System.Diagnostics;

namespace arcade_versioning_sample.Tests;

internal static class ProcessRunner
{
    public static string Run(string directory, string file, params string[] arguments) => Run(directory, file, arguments, []);

    public static string Run(string directory, string file, string[] arguments, params (string Name, string Value)[] environment)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = file,
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        foreach (var variable in environment) process.StartInfo.Environment[variable.Name] = variable.Value;
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{file} {string.Join(' ', arguments)} failed.\n{output}\n{error}");
        return output.Trim();
    }
}