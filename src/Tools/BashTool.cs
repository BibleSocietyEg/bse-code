using System.Diagnostics;

/// <summary>Executes a shell command and returns its combined stdout/stderr output.</summary>
public sealed class BashTool : IToolHandler
{
    public string Name        => "Bash";
    public string Description => "Execute a shell command";
    public object ParameterSchema => new
    {
        type     = "object",
        required = new[] { "command" },
        properties = new
        {
            command = new { type = "string", description = "The shell command to execute" }
        }
    };

    public Task<string> ExecuteAsync(string argsJson)
    {
        var args = ArgumentParser.ParseStringMap(argsJson);
        if (!args.TryGetValue("command", out var command) || string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("'command' is required.");

        return Task.FromResult(RunShell(command));
    }

    /// <summary>
    /// Runs <paramref name="command"/> in the platform-appropriate shell.
    /// Exposed internally so the REPL can reuse it for the <c>!</c> passthrough prefix.
    /// </summary>
    internal static string RunShell(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName  = "cmd.exe";
            startInfo.Arguments = "/c " + command;
        }
        else
        {
            startInfo.FileName = "/bin/bash";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return string.IsNullOrEmpty(stderr)
            ? stdout
            : $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}";
    }
}
