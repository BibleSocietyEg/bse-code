using FluentAssertions;

namespace BSE_Code.Tests.Tools;

public class ListDirToolTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly ListDirTool _tool = new();

    public ListDirToolTests()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "alpha.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "beta.txt"), "");
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir"));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task ExecuteAsync_ListsFilesAndDirectories()
    {
        var result = await _tool.ExecuteAsync($$"""{"path": "{{Escape(_tempDir)}}"}""");

        result.Should().Contain("alpha.txt");
        result.Should().Contain("beta.txt");
        result.Should().Contain("subdir/");
    }

    [Fact]
    public async Task ExecuteAsync_DirectoriesHaveDirPrefix()
    {
        var result = await _tool.ExecuteAsync($$"""{"path": "{{Escape(_tempDir)}}"}""");

        result.Should().Contain("[DIR]  subdir/");
    }

    [Fact]
    public async Task ExecuteAsync_MissingDirectory_ReturnsNotFoundMessage()
    {
        var missing = Path.Combine(_tempDir, "nonexistent");

        var result = await _tool.ExecuteAsync($$"""{"path": "{{Escape(missing)}}"}""");

        result.Should().StartWith("Directory not found:");
    }

    [Fact]
    public void Name_IsListDir()
    {
        _tool.Name.Should().Be("list_dir");
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\");
}
