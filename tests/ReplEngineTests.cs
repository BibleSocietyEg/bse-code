using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace BSE_Code.Tests;

/// <summary>
/// Custom FsCheck generators for ReplEngine property tests.
/// </summary>
public static class ReplEngineGenerators
{
    private static readonly HashSet<string> KnownFlags = new(StringComparer.Ordinal)
    {
        "-p", "--model", "--theme", "--output-format", "--config",
        "--version", "-v", "--help", "-h"
    };

    /// <summary>
    /// Generates strings that start with "--" or "-" but are NOT in the known flags set.
    /// </summary>
    public static Arbitrary<string> UnknownFlags()
    {
        // Generate a suffix of at least one alphanumeric char, then prepend "--" or "-"
        var suffixGen = ArbMap.Default.GeneratorFor<NonEmptyString>()
            .Select(s => s.Get.Replace("-", "x").Replace(" ", "x"))
            .Where(s => s.Length > 0 && s.All(c => char.IsLetterOrDigit(c) || c == '-'));

        var doubleDashGen = suffixGen
            .Select(s => "--" + s)
            .Where(flag => !KnownFlags.Contains(flag));

        // Use double-dash flags primarily (more reliable generation)
        return doubleDashGen.ToArbitrary();
    }

    /// <summary>
    /// Generates arbitrary non-null, non-empty file content strings.
    /// </summary>
    public static Arbitrary<string> FileContents()
    {
        return ArbMap.Default.GeneratorFor<NonEmptyString>()
            .Select(s => s.Get)
            .ToArbitrary();
    }

    /// <summary>
    /// Generates integers in the range 0–50 for directory file counts.
    /// </summary>
    public static Arbitrary<int> FileCounts()
    {
        return Gen.Choose(0, 50).ToArbitrary();
    }
}

[Collection("Sequential")]
public class ReplEngineTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch { /* best-effort cleanup */ }
        }
    }

    private string CreateTempFile(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        _tempPaths.Add(path);
        return path;
    }

    private string CreateTempDir(int fileCount)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        _tempPaths.Add(dir);
        for (int i = 0; i < fileCount; i++)
            File.WriteAllText(Path.Combine(dir, $"file{i:D3}.txt"), $"content {i}");
        return dir;
    }

    // ── ValidateUnknownFlags unit tests ───────────────────────────────────────

    [Fact]
    public void ValidateUnknownFlags_KnownFlags_DoesNotThrow()
    {
        var act = () => ReplEngine.ValidateUnknownFlags(
            ["-p", "hello", "--model", "gpt-4o"],
            inlinePrompt: "hello",
            modelOverride: "gpt-4o");

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateUnknownFlags_UnknownFlag_ThrowsArgumentException()
    {
        var act = () => ReplEngine.ValidateUnknownFlags(
            ["--foo"],
            inlinePrompt: null,
            modelOverride: null);

        act.Should().Throw<ArgumentException>();
    }

    // ── InjectAtPath unit tests ───────────────────────────────────────────────

    [Fact]
    public void InjectAtPath_ExistingFile_ReturnsFencedCodeBlock()
    {
        const string content = "Hello, world!";
        var path = CreateTempFile(content);

        var result = ReplEngine.InjectAtPath(path, "");

        result.Should().NotBeNull();
        result.Should().Contain(content);
        result.Should().Contain("```");
    }

    [Fact]
    public void InjectAtPath_MissingPath_ReturnsNull()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "missing.txt");

        var result = ReplEngine.InjectAtPath(nonExistent, "");

        result.Should().BeNull();
    }

    [Fact]
    public void InjectAtPath_Directory_ListsUpToTwentyFiles()
    {
        var dir = CreateTempDir(25);

        var result = ReplEngine.InjectAtPath(dir, "");

        result.Should().NotBeNull();
        var fileRefs = result!.Split('\n')
            .Count(line => line.TrimStart().StartsWith("---") && line.TrimEnd().EndsWith("---"));
        fileRefs.Should().BeLessThanOrEqualTo(20);
    }

    // ── Task 16.1: Property 1 ─────────────────────────────────────────────────

    // Feature: codebase-quality-improvements, Property 1: ValidateUnknownFlags rejects any unrecognised flag
    // Validates: Requirements 2.3
    [Property(MaxTest = 100, Arbitrary = [typeof(ReplEngineGenerators)])]
    public bool ValidateUnknownFlags_UnknownFlag_AlwaysThrowsArgumentException(string unknownFlag)
    {
        try
        {
            ReplEngine.ValidateUnknownFlags([unknownFlag], inlinePrompt: null, modelOverride: null);
            return false; // should have thrown
        }
        catch (ArgumentException)
        {
            return true; // expected
        }
        catch
        {
            return false; // wrong exception type
        }
    }

    // ── Task 16.2: Property 2 ─────────────────────────────────────────────────

    // Feature: codebase-quality-improvements, Property 2: InjectAtPath wraps any file content in a fenced code block
    // Validates: Requirements 2.4
    [Property(MaxTest = 100, Arbitrary = [typeof(ReplEngineGenerators)])]
    public bool InjectAtPath_AnyFileContent_WrapsInFencedCodeBlock(string content)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            var result = ReplEngine.InjectAtPath(path, "");
            return result is not null
                && result.Contains(content)
                && result.Contains("```");
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }

    // ── Task 16.3: Property 3 ─────────────────────────────────────────────────

    // Feature: codebase-quality-improvements, Property 3: InjectAtPath caps directory injection at 20 files
    // Validates: Requirements 2.5
    [Property(MaxTest = 100, Arbitrary = [typeof(ReplEngineGenerators)])]
    public bool InjectAtPath_AnyDirectorySize_CapsAtTwentyFiles(int fileCount)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            for (int i = 0; i < fileCount; i++)
                File.WriteAllText(Path.Combine(dir, $"file{i:D3}.txt"), $"content {i}");

            var result = ReplEngine.InjectAtPath(dir, "");

            if (fileCount == 0)
                return result is not null; // empty dir still returns a string

            if (result is null) return false;

            var fileRefs = result.Split('\n')
                .Count(line => line.TrimStart().StartsWith("---") && line.TrimEnd().EndsWith("---"));
            return fileRefs <= 20;
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
