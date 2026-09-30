using System.Diagnostics;

namespace nerdbank_versioning_sample.Tests;

internal static class ProcessRunner
{
    public static string Run(
        string directory,
        string file,
        string[] arguments,
        (string Name, string Value)? environment = null
    )
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
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        if (environment is { } value)
            process.StartInfo.Environment[value.Name] = value.Value;
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{file} failed.\n{output}\n{error}");
        return output.Trim();
    }
}
