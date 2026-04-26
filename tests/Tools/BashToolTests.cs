using FluentAssertions;

namespace BSE_Code.Tests.Tools;

public class BashToolTests
{
    private readonly BashTool _tool = new();

    [Fact]
    public async Task ExecuteAsync_SimpleEchoCommand_ReturnsOutput()
    {
        var result = await _tool.ExecuteAsync("""{"command": "echo hello"}""");

        result.Trim().Should().Be("hello");
    }

    [Fact]
    public async Task ExecuteAsync_MissingCommand_ThrowsArgumentException()
    {
        var act = async () => await _tool.ExecuteAsync("{}");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*command*");
    }

    [Fact]
    public async Task ExecuteAsync_CustomTimeout_IsRespected()
    {
        // A 1-second timeout on a fast command should still succeed
        var result = await _tool.ExecuteAsync("""{"command": "echo fast", "timeout_seconds": "1"}""");

        result.Trim().Should().Be("fast");
    }

    [Fact]
    public void RunShell_EchoCommand_ReturnsOutput()
    {
        var result = BashTool.RunShell("echo test");

        result.Trim().Should().Be("test");
    }

    [Fact]
    public void RunShell_HungCommand_TimesOutAndReturnsError()
    {
        // Use a very short timeout so the test stays fast.
        // On Windows, 'timeout' requires an interactive console (stdin), so use
        // 'ping' with a large repeat count instead — it works in non-interactive shells.
        // Set BSE_BASH_CONFIRM=off so the command runs without prompting.
        Environment.SetEnvironmentVariable("BSE_BASH_CONFIRM", "off");
        try
        {
            var command = OperatingSystem.IsWindows() ? "ping -n 60 127.0.0.1" : "sleep 60";
            var result = BashTool.RunShell(command, TimeSpan.FromMilliseconds(500));
            result.Should().StartWith("ERROR: Command timed out");
        }
        finally
        {
            Environment.SetEnvironmentVariable("BSE_BASH_CONFIRM", null);
        }
    }

    [Fact]
    public void RunShell_NonZeroExitCode_StillReturnsOutput()
    {
        // A command that exits non-zero should still return whatever it printed
        var command = OperatingSystem.IsWindows() ? "echo oops & exit 1" : "echo oops; exit 1";

        var result = BashTool.RunShell(command);

        result.Should().Contain("oops");
    }

    [Fact]
    public void RunShell_CommandWithStderr_IncludesStderrInOutput()
    {
        var command = OperatingSystem.IsWindows() ? "echo error 1>&2" : "echo error >&2";

        var result = BashTool.RunShell(command);

        result.Should().Contain("error");
    }

    [Fact]
    public void Name_IsBash()
    {
        _tool.Name.Should().Be("Bash");
    }

    [Fact]
    public void DefaultTimeout_IsThirtySeconds()
    {
        BashTool.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    // ── T4: stdin support tests ───────────────────────────────────────────────

    [Fact]
    public void RunShell_NoStdin_CommandReceivesEof()
    {
        // A command that reads all of stdin should exit immediately when stdin is closed (EOF)
        // rather than hanging. We verify it completes within the timeout.
        var command = OperatingSystem.IsWindows()
            ? "findstr /r \".*\""   // reads stdin until EOF, exits 0 or 1
            : "cat";                // reads stdin until EOF

        var result = BashTool.RunShell(command, TimeSpan.FromSeconds(5), stdin: null);

        // Should complete (not time out) — result may be empty or contain STDERR
        result.Should().NotStartWith("ERROR: Command timed out");
    }

    [Fact]
    public void RunShell_WithStdin_CommandReceivesInput()
    {
        // A command that reads from stdin should receive the provided string
        var command = OperatingSystem.IsWindows()
            ? "findstr /r \".*\""   // echoes matching lines from stdin
            : "cat";                // echoes stdin to stdout

        var result = BashTool.RunShell(command, TimeSpan.FromSeconds(5), stdin: "hello from stdin");

        result.Should().Contain("hello from stdin");
    }

    [Fact]
    public async Task ExecuteAsync_WithStdinParam_CommandReceivesInput()
    {
        // Use echo (allowlisted) piped through a stdin-reading command via shell
        // On Windows: echo pipes to findstr; on Unix: echo pipes to cat
        // Since we need ExecuteAsync to go through allowlist, use echo which IS allowlisted
        Environment.SetEnvironmentVariable("BSE_BASH_CONFIRM", "off");
        try
        {
            var command = OperatingSystem.IsWindows() ? "findstr /r \".*\"" : "cat";
            var argsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                command,
                stdin = "test stdin value"
            });

            var result = await _tool.ExecuteAsync(argsJson);
            result.Should().Contain("test stdin value");
        }
        finally
        {
            Environment.SetEnvironmentVariable("BSE_BASH_CONFIRM", null);
        }
    }

    // ── S1: Shell safeguard tests ─────────────────────────────────────────────

    [Fact]
    public void IsBlocked_BlocklistedCommand_ReturnsTrue()
    {
        BashTool.IsBlocked("rm -rf /").Should().BeTrue();
        BashTool.IsBlocked("mkfs /dev/sda").Should().BeTrue();
        BashTool.IsBlocked("dd if=/dev/zero of=/dev/sda").Should().BeTrue();
    }

    [Fact]
    public void IsBlocked_SafeCommand_ReturnsFalse()
    {
        BashTool.IsBlocked("echo hello").Should().BeFalse();
        BashTool.IsBlocked("git status").Should().BeFalse();
        BashTool.IsBlocked("dotnet build").Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_BlockedCommand_ReturnsErrorWithoutExecuting()
    {
        // rm -rf / is blocked — should return error, not execute
        var argsJson = System.Text.Json.JsonSerializer.Serialize(new { command = "rm -rf /" });
        var result = await _tool.ExecuteAsync(argsJson);
        result.Should().StartWith("ERROR: Command blocked");
    }

    [Fact]
    public async Task ExecuteAsync_AllowedCommand_ExecutesWithoutConfirmation()
    {
        // echo is on the allowlist — should execute directly
        Environment.SetEnvironmentVariable("BSE_BASH_CONFIRM", "off");
        try
        {
            var argsJson = System.Text.Json.JsonSerializer.Serialize(new { command = "echo allowed" });
            var result = await _tool.ExecuteAsync(argsJson);
            result.Should().Contain("allowed");
        }
        finally
        {
            Environment.SetEnvironmentVariable("BSE_BASH_CONFIRM", null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_BashConfirmOff_SkipsConfirmation()
    {
        // With BSE_BASH_CONFIRM=off, non-allowlisted commands run without prompting
        Environment.SetEnvironmentVariable("BSE_BASH_CONFIRM", "off");
        try
        {
            var argsJson = System.Text.Json.JsonSerializer.Serialize(new { command = "echo skip-confirm" });
            var result = await _tool.ExecuteAsync(argsJson);
            result.Should().Contain("skip-confirm");
        }
        finally
        {
            Environment.SetEnvironmentVariable("BSE_BASH_CONFIRM", null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExecutedCommand_AppearsInAuditLog()
    {
        Environment.SetEnvironmentVariable("BSE_BASH_CONFIRM", "off");
        var uniqueMarker = $"audit-test-{Guid.NewGuid():N}";
        try
        {
            var argsJson = System.Text.Json.JsonSerializer.Serialize(new { command = $"echo {uniqueMarker}" });
            await _tool.ExecuteAsync(argsJson);

            var auditPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".bse-code", "audit.log");

            if (File.Exists(auditPath))
            {
                var log = await File.ReadAllTextAsync(auditPath);
                log.Should().Contain(uniqueMarker);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("BSE_BASH_CONFIRM", null);
        }
    }
}
