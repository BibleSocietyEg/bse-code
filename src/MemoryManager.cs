/// <summary>
/// Manages project memory via BSE.md files (similar to Claude's CLAUDE.md / Gemini's GEMINI.md).
/// 
/// Files are loaded hierarchically:
///   1. ~/.bse-code/BSE.md          (user-level global memory)
///   2. ./BSE.md                    (project root)
///   3. ./BSE.local.md              (local overrides, gitignored)
/// 
/// The combined content is injected into the system prompt.
/// </summary>
public static class MemoryManager
{
    private static readonly string UserMemoryFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bse-code", "BSE.md");

    public record MemoryFile(string Path, string Content, string Label);

    private static List<MemoryFile> _files = [];

    public static IReadOnlyList<MemoryFile> Files => _files;

    /// <summary>Loads all BSE.md files from the hierarchy.</summary>
    public static void Reload()
    {
        _files = [];

        // 1. User-level global
        TryLoad(UserMemoryFile, "global (~/.bse-code/BSE.md)");

        // 2. Project root
        TryLoad(Path.Combine(Directory.GetCurrentDirectory(), "BSE.md"), "project (./BSE.md)");

        // 3. Local overrides (gitignored)
        TryLoad(Path.Combine(Directory.GetCurrentDirectory(), "BSE.local.md"), "local (./BSE.local.md)");
    }

    private static void TryLoad(string path, string label)
    {
        if (!File.Exists(path)) return;
        try
        {
            var content = File.ReadAllText(path);
            if (!string.IsNullOrWhiteSpace(content))
                _files.Add(new MemoryFile(path, content, label));
        }
        catch { /* ignore read errors */ }
    }

    /// <summary>
    /// Returns the combined memory content to inject into the system prompt.
    /// </summary>
    public static string BuildSystemContext()
    {
        if (_files.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n\n## Project Memory (BSE.md)\n");
        foreach (var f in _files)
            sb.AppendLine(f.Content);
        return sb.ToString();
    }

    /// <summary>
    /// Appends a note to the project BSE.md file.
    /// </summary>
    public static void AddNote(string text)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "BSE.md");
        var line = $"\n- {text.Trim()}";
        File.AppendAllText(path, line);
        Reload();
    }

    /// <summary>Creates the user-level BSE.md if it doesn't exist.</summary>
    public static void EnsureUserMemory()
    {
        var dir = Path.GetDirectoryName(UserMemoryFile)!;
        Directory.CreateDirectory(dir);
        if (!File.Exists(UserMemoryFile))
        {
            File.WriteAllText(UserMemoryFile, """
                # BSE-Code Global Memory

                This file is loaded automatically by bse-code in every project.
                Add your personal preferences, coding standards, or global instructions here.

                ## Preferences
                - Be concise and direct
                - Prefer modern C# idioms
                """);
        }
    }
}
