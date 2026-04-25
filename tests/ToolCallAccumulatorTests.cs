using FluentAssertions;

namespace BSE_Code.Tests;

public class ToolCallAccumulatorTests
{
    [Fact]
    public void Constructor_SetsIdAndName()
    {
        var acc = new ToolCallAccumulator("call-123", "read_file");

        acc.Id.Should().Be("call-123");
        acc.Name.Should().Be("read_file");
    }

    [Fact]
    public void Arguments_InitiallyEmpty()
    {
        var acc = new ToolCallAccumulator("id", "tool");

        acc.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void AppendArguments_SingleFragment_BuildsString()
    {
        var acc = new ToolCallAccumulator("id", "tool");

        acc.AppendArguments(BinaryData.FromString("""{"file_path":"""));

        acc.Arguments.Should().Be("""{"file_path":""");
    }

    [Fact]
    public void AppendArguments_MultipleFragments_ConcatenatesInOrder()
    {
        var acc = new ToolCallAccumulator("id", "read_file");

        acc.AppendArguments(BinaryData.FromString("{\"file_"));
        acc.AppendArguments(BinaryData.FromString("path\":"));
        acc.AppendArguments(BinaryData.FromString("\"foo.txt\"}"));

        acc.Arguments.Should().Be("{\"file_path\":\"foo.txt\"}");
    }

    [Fact]
    public void AppendArguments_NullFragment_IsIgnored()
    {
        var acc = new ToolCallAccumulator("id", "tool");
        acc.AppendArguments(BinaryData.FromString("hello"));

        acc.AppendArguments(null);

        acc.Arguments.Should().Be("hello");
    }

    [Fact]
    public void AppendArguments_EmptyFragment_AppendsNothing()
    {
        var acc = new ToolCallAccumulator("id", "tool");

        acc.AppendArguments(BinaryData.FromString(""));

        acc.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void AppendArguments_ProducesValidJson_WhenFragmentsAreComplete()
    {
        var acc = new ToolCallAccumulator("id", "Write");
        var fragments = new[] { "{\"file_path\":", "\"out.txt\",", "\"content\":", "\"hello\"}" };

        foreach (var f in fragments)
            acc.AppendArguments(BinaryData.FromString(f));

        var act = () => System.Text.Json.JsonDocument.Parse(acc.Arguments);
        act.Should().NotThrow();
    }

    [Fact]
    public void Index_DefaultsToZero()
    {
        var acc = new ToolCallAccumulator("id", "tool");

        acc.Index.Should().Be(0);
    }

    [Fact]
    public void Index_CanBeSet()
    {
        var acc = new ToolCallAccumulator("id", "tool") { Index = 3 };

        acc.Index.Should().Be(3);
    }
}
