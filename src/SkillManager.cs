/// <summary>
/// Manages skills — markdown files that provide reusable instructions/workflows.
/// Skills are loaded from:
///   1. ~/.bse-code/skills/   (user-level, all projects)
///   2. .bse-code/skills/     (project-level)
/// Each skill is a .md file. The filename (without extension) is the skill name.
/// Skills can be invoked with /skill-name or auto-injected into the system prompt.
/// </summary>
public static class SkillManager
{
    private static readonly string UserSkillsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bse-code", "skills");

    // Computed per-call so it reflects the current working directory, not the startup CWD.
    private static string ProjectSkillsDir =>
        Path.Combine(Directory.GetCurrentDirectory(), ".bse-code", "skills");

    public record Skill(string Name, string Content, string FilePath, bool IsUserLevel);

    private static List<Skill> _skills = [];

    /// <summary>Loads all skills from user and project directories.</summary>
    public static void Reload()
    {
        _skills = [];
        LoadFrom(UserSkillsDir, isUser: true);
        LoadFrom(ProjectSkillsDir, isUser: false);
    }

    private static void LoadFrom(string dir, bool isUser)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.GetFiles(dir, "*.md", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var content = File.ReadAllText(file);
            _skills.Add(new Skill(name, content, file, isUser));
        }
    }

    public static IReadOnlyList<Skill> All => _skills;

    /// <summary>Find a skill by name (case-insensitive).</summary>
    public static Skill? Find(string name) =>
        _skills.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the combined content of all skills to inject into the system prompt.
    /// </summary>
    public static string BuildSystemContext()
    {
        if (_skills.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n\n## Available Skills\n");
        foreach (var skill in _skills)
        {
            sb.AppendLine($"### Skill: {skill.Name}");
            sb.AppendLine(skill.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Ensures the user skills directory exists and creates an example skill if empty.
    /// </summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(UserSkillsDir);
        Directory.CreateDirectory(ProjectSkillsDir);

        // Create example skill if none exist
        var examplePath = Path.Combine(UserSkillsDir, "example.md");
        if (!File.Exists(examplePath))
        {
            File.WriteAllText(examplePath, """
                # Example Skill

                This is an example skill. Skills are markdown files that provide
                reusable instructions or workflows to the AI.

                You can invoke this skill with `/example` in the REPL.

                ## Instructions
                When this skill is invoked, greet the user and explain what skills are.
                """);
        }
    }
}
