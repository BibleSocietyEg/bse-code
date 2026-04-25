using FluentAssertions;

namespace BSE_Code.Tests.Tools;

public class GlobToolTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly GlobTool _tool = new();

    public GlobToolTests()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "file1.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "file2.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "");
        var sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "nested.cs"), "");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task ExecuteAsync_MatchingPattern_ReturnsRelativePaths()
    {
        var result = await _tool.ExecuteAsync(
            $$"""{"pattern": "*.cs", "base_path": "{{Escape(_tempDir)}}"}""");

        result.Should().Contain("file1.cs");
        result.Should().Contain("file2.cs");
        result.Should().NotContain("readme.md");
    }

    [Fact]
    public async Task ExecuteAsync_NoMatches_ReturnsNoFilesMatched()
    {
        var result = await _tool.ExecuteAsync(
            $$"""{"pattern": "*.xyz", "base_path": "{{Escape(_tempDir)}}"}""");

        result.Should().Be("No files matched.");
    }

    [Fact]
    public void Name_IsGlob()
    {
        _tool.Name.Should().Be("glob");
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\");
}
