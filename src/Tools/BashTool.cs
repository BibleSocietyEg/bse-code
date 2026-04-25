using System.Diagnostics;

/// <summary>Executes a shell command and returns its combined stdout/stderr output.</summary>
public sealed class BashTool : IToolHandler
{
    /// <summary>Default timeout for shell commands (30 seconds).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public string Name        => "Bash";
    public string Description => "Execute a shell command";
    public object ParameterSchema => new
    {
        type     = "object",
        required = new[] { "command" },
        properties = new
        {
            command = new { type = "string", description = "The shell command to execute" },
            timeout_seconds = new { type = "integer", description = "Timeout in seconds (default: 30)" }
        }
    };

    public Task<string> ExecuteAsync(string argsJson)
    {
        var args = ArgumentParser.ParseStringMap(argsJson);
        if (!args.TryGetValue("command", out var command) || string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("'command' is required.");

        var timeout = DefaultTimeout;
        if (args.TryGetValue("timeout_seconds", out var ts) && int.TryParse(ts, out var secs) && secs > 0)
            timeout = TimeSpan.FromSeconds(secs);

        return Task.FromResult(RunShell(command, timeout));
    }

    /// <summary>
    /// Runs <paramref name="command"/> in the platform-appropriate shell.
    /// Exposed internally so the REPL can reuse it for the <c>!</c> passthrough prefix.
    /// Kills the process and returns an error message if <paramref name="timeout"/> elapses.
    /// </summary>
    internal static string RunShell(string command, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;

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

        // Read stdout/stderr concurrently to avoid deadlocks on large output
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        bool finished = process.WaitForExit((int)effectiveTimeout.TotalMilliseconds);
        if (!finished)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            return $"ERROR: Command timed out after {effectiveTimeout.TotalSeconds:0}s: {command}";
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        if (string.IsNullOrEmpty(stderr)) return stdout;
        if (string.IsNullOrEmpty(stdout)) return $"STDERR:\n{stderr}";
        return $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}";
    }
}
