using System.Diagnostics;

namespace gitversion_versioning_sample.Tests;

internal static class ProcessRunner
{
    public static string Run(string directory, string file, params string[] arguments) =>
        Run(directory, file, arguments, null);

    public static string Run(string directory, string file, string[] arguments, (string Name, string Value)? environment)
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
        process.StartInfo.Environment.Remove("GITHUB_ACTIONS");
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (environment is { } value) process.StartInfo.Environment[value.Name] = value.Value;
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{file} {string.Join(' ', arguments)} failed.\n{output}\n{error}");
        return output.Trim();
    }
}