using FluentAssertions;

namespace BSE_Code.Tests.Tools;

public class GrepToolTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly GrepTool _tool = new();

    public GrepToolTests()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllLines(Path.Combine(_tempDir, "code.cs"), [
            "public class Foo {",
            "    public void Bar() {}",
            "    private int count = 0;",
            "}"
        ]);
        File.WriteAllLines(Path.Combine(_tempDir, "notes.txt"), [
            "This is a note.",
            "Another line here."
        ]);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task ExecuteAsync_MatchingPattern_ReturnsMatchLines()
    {
        var result = await _tool.ExecuteAsync(
            $$"""{"pattern": "public", "path": "{{Escape(_tempDir)}}"}""");

        result.Should().Contain("public class Foo");
        result.Should().Contain("public void Bar");
    }

    [Fact]
    public async Task ExecuteAsync_NoMatches_ReturnsNoMatchesFound()
    {
        var result = await _tool.ExecuteAsync(
            $$"""{"pattern": "zzznomatch", "path": "{{Escape(_tempDir)}}"}""");

        result.Should().Be("No matches found.");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidRegex_ReturnsErrorMessage()
    {
        var result = await _tool.ExecuteAsync(
            $$"""{"pattern": "[invalid", "path": "{{Escape(_tempDir)}}"}""");

        result.Should().StartWith("Invalid regex pattern:");
    }

    [Fact]
    public async Task ExecuteAsync_SingleFile_SearchesOnlyThatFile()
    {
        var filePath = Path.Combine(_tempDir, "code.cs");

        var result = await _tool.ExecuteAsync(
            $$"""{"pattern": "note", "path": "{{Escape(filePath)}}"}""");

        result.Should().Be("No matches found.");
    }

    [Fact]
    public async Task ExecuteAsync_CaseInsensitive_MatchesRegardlessOfCase()
    {
        var result = await _tool.ExecuteAsync(
            $$"""{"pattern": "PUBLIC", "path": "{{Escape(_tempDir)}}"}""");

        result.Should().Contain("public class Foo");
    }

    [Fact]
    public void Name_IsGrep()
    {
        _tool.Name.Should().Be("grep");
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\");
}
