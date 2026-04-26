using System.Diagnostics;

/// <summary>Executes a shell command and returns its combined stdout/stderr output.</summary>
public sealed class BashTool : IToolHandler
{
    /// <summary>Default timeout for shell commands (30 seconds).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public string Name => "Bash";
    public string Description => "Execute a shell command";
    public object ParameterSchema => new
    {
        type = "object",
        required = new[] { "command" },
        properties = new
        {
            command = new { type = "string", description = "The shell command to execute" },
            timeout_seconds = new { type = "integer", description = "Timeout in seconds (default: 30)" },
            stdin = new { type = "string", description = "Optional string to write to stdin before closing" }
        }
    };

    public Task<string> ExecuteAsync(string argsJson)
    {
        var args = ArgumentParser.ParseStringMap(argsJson);
        // WARNING: 'command' is passed directly to the platform shell without sanitization.
        if (!args.TryGetValue("command", out var command) || string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("'command' is required.");

        var timeout = DefaultTimeout;
        if (args.TryGetValue("timeout_seconds", out var ts) && int.TryParse(ts, out var secs) && secs > 0)
            timeout = TimeSpan.FromSeconds(secs);

        args.TryGetValue("stdin", out var stdinInput);

        return Task.FromResult(RunShell(command, timeout, stdinInput));
    }

    /// <summary>
    /// Runs <paramref name="command"/> in the platform-appropriate shell
    /// (cmd.exe on Windows, /bin/bash on Unix).
    /// </summary>
    /// <remarks>
    /// <b>SECURITY WARNING:</b> This method executes arbitrary shell commands
    /// supplied by the LLM without any input sanitization or sandboxing.
    /// Callers are responsible for ensuring the command originates from a
    /// trusted source (i.e., the user has reviewed and approved it).
    /// Do not call this method with untrusted or unvalidated input.
    /// </remarks>
    internal static string RunShell(string command, TimeSpan? timeout = null, string? stdin = null)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;

        var startInfo = new ProcessStartInfo
        {
            RedirectStandardInput = true,   // always redirect so we can close it
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = "cmd.exe";
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

        // Write stdin if provided, then always close to send EOF
        if (stdin is not null)
            process.StandardInput.Write(stdin);
        process.StandardInput.Close();

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
