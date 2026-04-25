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
        // Use a very short timeout so the test stays fast
        // On Windows, 'timeout' requires an interactive console; use 'ping' instead which works headlessly
        var command = OperatingSystem.IsWindows() ? "ping -n 61 127.0.0.1" : "sleep 60";

        var result = BashTool.RunShell(command, TimeSpan.FromMilliseconds(500));

        result.Should().StartWith("ERROR: Command timed out");
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
}
