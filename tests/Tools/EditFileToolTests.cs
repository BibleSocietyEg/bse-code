using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace BSE_Code.Tests.Tools;

public static class EditFileToolGenerators
{
    /// <summary>
    /// Generates a tuple of (prefix, token, suffix, replacement) where token appears
    /// exactly once in the combined content (prefix + token + suffix).
    /// </summary>
    public static Arbitrary<(string prefix, string token, string suffix, string replacement)> UniqueTokenEdits()
    {
        var gen =
            from prefix in ArbMap.Default.GeneratorFor<NonEmptyString>().Select(s => s.Get)
            from token in ArbMap.Default.GeneratorFor<NonEmptyString>().Select(s => s.Get)
            from suffix in ArbMap.Default.GeneratorFor<NonEmptyString>().Select(s => s.Get)
            from replacement in ArbMap.Default.GeneratorFor<NonEmptyString>().Select(s => s.Get)
                // Ensure token appears exactly once: not in prefix or suffix, and replacement doesn't re-introduce it
            where !prefix.Contains(token, StringComparison.Ordinal)
               && !suffix.Contains(token, StringComparison.Ordinal)
               && !replacement.Contains(token, StringComparison.Ordinal)
            select (prefix, token, suffix, replacement);

        return gen.ToArbitrary();
    }
}

public class EditFileToolTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly EditFileTool _tool = new();

    public EditFileToolTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static string Escape(string path) => path.Replace("\\", "\\\\");

    private string TempFile(string content)
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid() + ".txt");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task ExecuteAsync_ValidEdit_ReplacesFirstOccurrence()
    {
        var path = TempFile("Hello World");
        var argsJson = $$"""{"file_path": "{{Escape(path)}}", "old_str": "World", "new_str": "C#"}""";

        var result = await _tool.ExecuteAsync(argsJson);

        result.Should().Contain("Edited");
        (await File.ReadAllTextAsync(path)).Should().Be("Hello C#");
    }

    [Fact]
    public async Task ExecuteAsync_FileNotFound_ReturnsError()
    {
        var path = Path.Combine(_tempDir, "nonexistent.txt");
        var argsJson = $$"""{"file_path": "{{Escape(path)}}", "old_str": "x", "new_str": "y"}""";

        var result = await _tool.ExecuteAsync(argsJson);

        result.Should().Contain("file not found");
    }

    [Fact]
    public async Task ExecuteAsync_OldStrNotFound_ReturnsError()
    {
        var path = TempFile("Hello World");
        var argsJson = $$"""{"file_path": "{{Escape(path)}}", "old_str": "missing", "new_str": "y"}""";

        var result = await _tool.ExecuteAsync(argsJson);

        result.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteAsync_AmbiguousOldStr_ReturnsError()
    {
        var path = TempFile("foo foo foo");
        var argsJson = $$"""{"file_path": "{{Escape(path)}}", "old_str": "foo", "new_str": "bar"}""";

        var result = await _tool.ExecuteAsync(argsJson);

        result.Should().Contain("ambiguous");
    }

    [Fact]
    public async Task ExecuteAsync_MissingFilePathParam_ThrowsArgumentException()
    {
        var act = async () => await _tool.ExecuteAsync("""{"old_str": "x", "new_str": "y"}""");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*file_path*");
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulEdit_ConfirmationContainsFilePath()
    {
        var path = TempFile("alpha beta");
        var argsJson = $$"""{"file_path": "{{Escape(path)}}", "old_str": "alpha", "new_str": "gamma"}""";

        var result = await _tool.ExecuteAsync(argsJson);

        result.Should().Contain(path);
    }

    /// <summary>
    /// **Validates: Requirements 2.1**
    /// For any content with a unique substring, after editing old_str is absent and new_str is present.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(EditFileToolGenerators)])]
    public bool EditFileTool_EditRoundTrip((string prefix, string token, string suffix, string replacement) input)
    {
        var (prefix, token, suffix, replacement) = input;
        var content = prefix + token + suffix;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            File.WriteAllText(path, content);
            var argsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                file_path = path,
                old_str = token,
                new_str = replacement
            });

            var tool = new EditFileTool();
            tool.ExecuteAsync(argsJson).GetAwaiter().GetResult();

            var result = File.ReadAllText(path);
            return !result.Contains(token, StringComparison.Ordinal)
                && result.Contains(replacement, StringComparison.Ordinal);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// **Validates: Requirements 2.2**
    /// For any valid edit, the confirmation message contains the file path.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(EditFileToolGenerators)])]
    public bool EditFileTool_ConfirmationContainsFilePath((string prefix, string token, string suffix, string replacement) input)
    {
        var (prefix, token, suffix, replacement) = input;
        var content = prefix + token + suffix;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            File.WriteAllText(path, content);
            var argsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                file_path = path,
                old_str = token,
                new_str = replacement
            });

            var tool = new EditFileTool();
            var result = tool.ExecuteAsync(argsJson).GetAwaiter().GetResult();

            return result.Contains(path, StringComparison.Ordinal);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
        }
    }
}
