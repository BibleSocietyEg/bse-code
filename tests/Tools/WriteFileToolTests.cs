using FluentAssertions;

namespace BSE_Code.Tests.Tools;

public class WriteFileToolTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly WriteFileTool _tool = new();

    public WriteFileToolTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task ExecuteAsync_WritesFileContent()
    {
        var path = Path.Combine(_tempDir, "out.txt");

        var result = await _tool.ExecuteAsync($$"""{"file_path": "{{Escape(path)}}", "content": "test content"}""");

        result.Should().Be("File written successfully.");
        (await File.ReadAllTextAsync(path)).Should().Be("test content");
    }

    [Fact]
    public async Task ExecuteAsync_CreatesIntermediateDirectories()
    {
        var path = Path.Combine(_tempDir, "sub", "dir", "file.txt");

        await _tool.ExecuteAsync($$"""{"file_path": "{{Escape(path)}}", "content": "nested"}""");

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_OverwritesExistingFile()
    {
        var path = Path.Combine(_tempDir, "overwrite.txt");
        await File.WriteAllTextAsync(path, "old");

        await _tool.ExecuteAsync($$"""{"file_path": "{{Escape(path)}}", "content": "new"}""");

        (await File.ReadAllTextAsync(path)).Should().Be("new");
    }

    [Fact]
    public async Task ExecuteAsync_MissingFilePath_ThrowsArgumentException()
    {
        var act = async () => await _tool.ExecuteAsync("""{"content": "x"}""");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*file_path*");
    }

    [Fact]
    public async Task ExecuteAsync_MissingContent_ThrowsArgumentException()
    {
        var act = async () => await _tool.ExecuteAsync("""{"file_path": "x.txt"}""");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*content*");
    }

    [Fact]
    public void Name_IsWrite()
    {
        _tool.Name.Should().Be("Write");
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\");
}
