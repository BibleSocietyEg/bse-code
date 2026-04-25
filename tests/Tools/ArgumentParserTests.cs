using FluentAssertions;

namespace BSE_Code.Tests.Tools;

public class ArgumentParserTests
{
    // ── ParseStringMap ────────────────────────────────────────────────────────

    [Fact]
    public void ParseStringMap_ValidJson_ReturnsDictionary()
    {
        var result = ArgumentParser.ParseStringMap("""{"file_path": "foo.txt"}""");

        result.Should().ContainKey("file_path").WhoseValue.Should().Be("foo.txt");
    }

    [Fact]
    public void ParseStringMap_PreservesKeyCase()
    {
        // JSON dictionary keys are case-sensitive; the key is preserved as-is
        var result = ArgumentParser.ParseStringMap("""{"FILE_PATH": "bar.txt"}""");

        result.Should().ContainKey("FILE_PATH");
    }

    [Fact]
    public void ParseStringMap_EmptyObject_ReturnsEmptyDictionary()
    {
        var result = ArgumentParser.ParseStringMap("{}");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseStringMap_InvalidJson_ThrowsArgumentException()
    {
        var act = () => ArgumentParser.ParseStringMap("not-json");

        act.Should().Throw<ArgumentException>().WithMessage("Invalid tool arguments:*");
    }

    [Fact]
    public void ParseStringMap_NullJson_ThrowsArgumentException()
    {
        var act = () => ArgumentParser.ParseStringMap("null");

        act.Should().Throw<ArgumentException>().WithMessage("Arguments must be a JSON object.");
    }

    // ── ParseElementMap ───────────────────────────────────────────────────────

    [Fact]
    public void ParseElementMap_ValidJson_ReturnsDictionary()
    {
        var result = ArgumentParser.ParseElementMap("""{"recursive": true, "count": 5}""");

        result.Should().ContainKey("recursive");
        result.Should().ContainKey("count");
    }

    [Fact]
    public void ParseElementMap_InvalidJson_ThrowsArgumentException()
    {
        var act = () => ArgumentParser.ParseElementMap("{bad}");

        act.Should().Throw<ArgumentException>();
    }
}
