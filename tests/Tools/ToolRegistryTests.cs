using FluentAssertions;

namespace BSE_Code.Tests.Tools;

public class ToolRegistryTests
{
    private readonly ToolRegistry _registry = ToolRegistry.CreateDefault();

    [Theory]
    [InlineData("read_file")]
    [InlineData("Write")]
    [InlineData("Bash")]
    [InlineData("list_dir")]
    [InlineData("glob")]
    [InlineData("grep")]
    public void CreateDefault_ContainsAllBuiltInTools(string toolName)
    {
        _registry.Contains(toolName).Should().BeTrue();
    }

    [Fact]
    public void Contains_UnknownTool_ReturnsFalse()
    {
        _registry.Contains("nonexistent_tool").Should().BeFalse();
    }

    [Fact]
    public void ToolNames_ReturnsAllRegisteredNames()
    {
        _registry.ToolNames.Should().HaveCountGreaterThanOrEqualTo(6);
    }

    [Fact]
    public void ToChatTools_ReturnsOneToolPerHandler()
    {
        var tools = _registry.ToChatTools().ToList();

        tools.Should().HaveCountGreaterThanOrEqualTo(6);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsErrorString()
    {
        var result = await _registry.ExecuteAsync("unknown_tool", "{}");

        result.Should().StartWith("Unknown tool:");
    }

    [Fact]
    public void Constructor_DuplicateToolNames_ThrowsException()
    {
        var handlers = new[] { new ReadFileTool(), new ReadFileTool() };

        var act = () => new ToolRegistry(handlers);

        act.Should().Throw<ArgumentException>();
    }
}
