using System.Diagnostics;

/// <summary>Executes a shell command and returns its combined stdout/stderr output.</summary>
public sealed class BashTool : IToolHandler
{
    /// <summary>Default timeout for shell commands (30 seconds).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static readonly SemaphoreSlim _confirmLock = new(1, 1);

    // ── Security: blocklist / allowlist ──────────────────────────────────────

    internal static readonly string[] Blocklist =
    [
        "rm -rf /", "rm -rf ~", "rm -rf *",
        "format c:", "format c:/", "mkfs",
        "dd if=", ":(){:|:&};:",
        "del /f /s /q c:\\"
    ];

    internal static readonly string[] Allowlist =
    [
        "echo ", "cat ", "ls", "dir", "git status", "git log",
        "git diff", "git branch", "git show", "pwd", "type ",
        "dotnet build", "dotnet test", "dotnet run", "dotnet format",
        "grep ", "find ", "where ", "which "
    ];

    private static readonly string AuditLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".bse-code", "audit.log");

    internal static bool IsBlocked(string cmd) =>
        Blocklist.Any(b => cmd.Contains(b, StringComparison.OrdinalIgnoreCase));

    internal static bool IsAllowed(string cmd) =>
        Allowlist.Any(a => cmd.StartsWith(a, StringComparison.OrdinalIgnoreCase)
                        || cmd.Equals(a.Trim(), StringComparison.OrdinalIgnoreCase));

    private static void AppendAuditLog(string command, int exitCode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AuditLogPath)!);
            File.AppendAllText(AuditLogPath,
                $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} | exit:{exitCode} | {command}{Environment.NewLine}");
        }
        catch { /* best-effort */ }
    }

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

    public async Task<string> ExecuteAsync(string argsJson)
    {
        var args = ArgumentParser.ParseStringMap(argsJson);
        // WARNING: 'command' is passed directly to the platform shell without sanitization.
        if (!args.TryGetValue("command", out var command) || string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("'command' is required.");

        var timeout = DefaultTimeout;
        if (args.TryGetValue("timeout_seconds", out var ts) && int.TryParse(ts, out var secs) && secs > 0)
            timeout = TimeSpan.FromSeconds(secs);

        args.TryGetValue("stdin", out var stdinInput);

        // ── Security checks ───────────────────────────────────────────────────
        if (IsBlocked(command))
            return $"ERROR: Command blocked for safety: '{command}'";

        var skipConfirm = Environment.GetEnvironmentVariable("BSE_BASH_CONFIRM")
                              ?.Equals("off", StringComparison.OrdinalIgnoreCase) == true;

        if (!IsAllowed(command) && !skipConfirm)
        {
            await _confirmLock.WaitAsync();
            try
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"  ⚠️  Allow command? [y/N]: {command}\n  > ");
                Console.ResetColor();
                var answer = Console.ReadLine()?.Trim();
                if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase))
                    return "ERROR: Command denied by user.";
            }
            finally
            {
                _confirmLock.Release();
            }
        }

        var result = RunShell(command, timeout, stdinInput);

        // Determine exit code from result for audit log
        var exitCode = result.StartsWith("ERROR:") ? -1 : 0;
        AppendAuditLog(command, exitCode);

        return result;
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

    /// <summary>Runs a shell command and returns both the output string and the actual process exit code.</summary>
    internal static (string output, int exitCode) RunShellWithExitCode(
        string command, TimeSpan? timeout = null, string? stdin = null)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;

        var startInfo = new ProcessStartInfo
        {
            RedirectStandardInput = true,
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

        if (stdin is not null) process.StandardInput.Write(stdin);
        process.StandardInput.Close();

        var stdoutTask2 = process.StandardOutput.ReadToEndAsync();
        var stderrTask2 = process.StandardError.ReadToEndAsync();

        bool finished = process.WaitForExit((int)effectiveTimeout.TotalMilliseconds);
        if (!finished)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return ($"ERROR: Command timed out after {effectiveTimeout.TotalSeconds:0}s: {command}", -1);
        }

        var stdoutResult = stdoutTask2.GetAwaiter().GetResult();
        var stderrResult = stderrTask2.GetAwaiter().GetResult();
        int exitCode = process.ExitCode;

        string output;
        if (string.IsNullOrEmpty(stderrResult)) output = stdoutResult;
        else if (string.IsNullOrEmpty(stdoutResult)) output = $"STDERR:\n{stderrResult}";
        else output = $"STDOUT:\n{stdoutResult}\nSTDERR:\n{stderrResult}";

        return (output, exitCode);
    }
}
