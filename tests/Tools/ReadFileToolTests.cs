using FluentAssertions;

namespace BSE_Code.Tests.Tools;

public class ReadFileToolTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly ReadFileTool _tool = new();

    public ReadFileToolTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task ExecuteAsync_ExistingFile_ReturnsContent()
    {
        var path = Path.Combine(_tempDir, "hello.txt");
        await File.WriteAllTextAsync(path, "hello world");

        var result = await _tool.ExecuteAsync($$"""{"file_path": "{{Escape(path)}}"}""");

        result.Should().Be("hello world");
    }

    [Fact]
    public async Task ExecuteAsync_MissingFile_ReturnsNotFoundMessage()
    {
        var path = Path.Combine(_tempDir, "missing.txt");

        var result = await _tool.ExecuteAsync($$"""{"file_path": "{{Escape(path)}}"}""");

        result.Should().StartWith("File not found:");
    }

    [Fact]
    public async Task ExecuteAsync_MissingFilePath_ThrowsArgumentException()
    {
        var act = async () => await _tool.ExecuteAsync("{}");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*file_path*");
    }

    [Fact]
    public void Name_IsReadFile()
    {
        _tool.Name.Should().Be("read_file");
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\");
}
