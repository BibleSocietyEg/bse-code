using FluentAssertions;

namespace BSE_Code.Tests;

/// <summary>
/// MemoryManager uses Directory.GetCurrentDirectory() at call time, so we redirect
/// it to a temp dir. Tests run sequentially to avoid CWD races.
/// </summary>
[Collection("Sequential")]
public class MemoryManagerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"bse-mem-{Guid.NewGuid():N}");
    private readonly string _originalDir = Directory.GetCurrentDirectory();
    private readonly int _globalFileCount; // user-level BSE.md may exist on this machine

    public MemoryManagerTests()
    {
        Directory.CreateDirectory(_tempDir);
        Directory.SetCurrentDirectory(_tempDir);
        MemoryManager.Reload();
        _globalFileCount = MemoryManager.Files.Count; // baseline (0 or 1 global file)
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalDir);
        Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Reload_WithProjectBseMd_AddsOneFile()
    {
        File.WriteAllText(Path.Combine(_tempDir, "BSE.md"), "# My Project");

        MemoryManager.Reload();

        MemoryManager.Files.Should().HaveCount(_globalFileCount + 1);
        MemoryManager.Files.Should().Contain(f => f.Content.Contains("My Project"));
    }

    [Fact]
    public void Reload_WithLocalBseMd_AddsTwoFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "BSE.md"), "# Project");
        File.WriteAllText(Path.Combine(_tempDir, "BSE.local.md"), "# Local");

        MemoryManager.Reload();

        MemoryManager.Files.Should().HaveCount(_globalFileCount + 2);
    }

    [Fact]
    public void Reload_EmptyBseMdFile_IsNotLoaded()
    {
        File.WriteAllText(Path.Combine(_tempDir, "BSE.md"), "   ");

        MemoryManager.Reload();

        MemoryManager.Files.Should().HaveCount(_globalFileCount);
    }

    [Fact]
    public void Reload_ProjectFileLabel_ContainsProjectLabel()
    {
        File.WriteAllText(Path.Combine(_tempDir, "BSE.md"), "# Content");

        MemoryManager.Reload();

        MemoryManager.Files.Should().Contain(f => f.Label.Contains("project"));
    }

    [Fact]
    public void BuildSystemContext_WithProjectFile_ContainsMemorySection()
    {
        File.WriteAllText(Path.Combine(_tempDir, "BSE.md"), "# Project Memory");

        MemoryManager.Reload();
        var context = MemoryManager.BuildSystemContext();

        context.Should().Contain("Project Memory (BSE.md)");
        context.Should().Contain("# Project Memory");
    }

    [Fact]
    public void BuildSystemContext_NoProjectFiles_RespectsGlobalState()
    {
        // No project files — context is empty only if no global file exists
        var context = MemoryManager.BuildSystemContext();

        if (_globalFileCount == 0)
            context.Should().BeEmpty();
        else
            context.Should().Contain("Project Memory (BSE.md)");
    }

    [Fact]
    public void AddNote_AppendsNoteToProjectBseMd()
    {
        File.WriteAllText(Path.Combine(_tempDir, "BSE.md"), "# Project");

        MemoryManager.AddNote("Use tabs for indentation");

        var content = File.ReadAllText(Path.Combine(_tempDir, "BSE.md"));
        content.Should().Contain("Use tabs for indentation");
    }

    [Fact]
    public void AddNote_CreatesBseMdIfNotExists()
    {
        MemoryManager.AddNote("New note");

        File.Exists(Path.Combine(_tempDir, "BSE.md")).Should().BeTrue();
    }

    [Fact]
    public void Files_IsReadOnly()
    {
        MemoryManager.Files.Should().BeAssignableTo<IReadOnlyList<MemoryManager.MemoryFile>>();
    }
}
