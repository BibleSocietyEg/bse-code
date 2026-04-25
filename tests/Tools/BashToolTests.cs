using FluentAssertions;

namespace BSE_Code.Tests.Tools;

public class BashToolTests
{
    private readonly BashTool _tool = new();

    [Fact]
    public async Task ExecuteAsync_SimpleEchoCommand_ReturnsOutput()
    {
        var command = OperatingSystem.IsWindows() ? "echo hello" : "echo hello";

        var result = await _tool.ExecuteAsync($$"""{"command": "{{command}}"}""");

        result.Trim().Should().Be("hello");
    }

    [Fact]
    public async Task ExecuteAsync_MissingCommand_ThrowsArgumentException()
    {
        var act = async () => await _tool.ExecuteAsync("{}");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*command*");
    }

    [Fact]
    public void RunShell_EchoCommand_ReturnsOutput()
    {
        var result = BashTool.RunShell("echo test");

        result.Trim().Should().Be("test");
    }

    [Fact]
    public void RunShell_CommandWithStderr_IncludesStderrInOutput()
    {
        // A command that writes to stderr
        var command = OperatingSystem.IsWindows()
            ? "echo error 1>&2"
            : "echo error >&2";

        var result = BashTool.RunShell(command);

        result.Should().Contain("error");
    }

    [Fact]
    public void Name_IsBash()
    {
        _tool.Name.Should().Be("Bash");
    }
}
