using System.Text.Json.Serialization;

/// <summary>
/// A named color theme for the terminal UI.
/// </summary>
public class Theme
{
    public string Name        { get; init; } = "default";
    public ConsoleColor Accent     { get; init; } = ConsoleColor.Cyan;
    public ConsoleColor Muted      { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor Prompt     { get; init; } = ConsoleColor.Cyan;
    public ConsoleColor Response   { get; init; } = ConsoleColor.White;
    public ConsoleColor Tool       { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor Success    { get; init; } = ConsoleColor.Green;
    public ConsoleColor Error      { get; init; } = ConsoleColor.Red;
    public ConsoleColor Warning    { get; init; } = ConsoleColor.DarkYellow;
    public ConsoleColor Header     { get; init; } = ConsoleColor.Cyan;
    public ConsoleColor Skill      { get; init; } = ConsoleColor.Magenta;
    public ConsoleColor Mcp        { get; init; } = ConsoleColor.Blue;
    public ConsoleColor Git        { get; init; } = ConsoleColor.DarkGreen;
}

/// <summary>
/// Built-in themes and theme management.
/// </summary>
public static class ThemeManager
{
    public static readonly Dictionary<string, Theme> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = new Theme
        {
            Name = "default",
            Accent   = ConsoleColor.Cyan,
            Muted    = ConsoleColor.DarkGray,
            Prompt   = ConsoleColor.Cyan,
            Response = ConsoleColor.White,
            Tool     = ConsoleColor.DarkCyan,
            Success  = ConsoleColor.Green,
            Error    = ConsoleColor.Red,
            Warning  = ConsoleColor.DarkYellow,
            Header   = ConsoleColor.Cyan,
            Skill    = ConsoleColor.Magenta,
            Mcp      = ConsoleColor.Blue,
            Git      = ConsoleColor.DarkGreen,
        },
        ["dracula"] = new Theme
        {
            Name = "dracula",
            Accent   = ConsoleColor.Magenta,
            Muted    = ConsoleColor.DarkGray,
            Prompt   = ConsoleColor.Magenta,
            Response = ConsoleColor.Gray,
            Tool     = ConsoleColor.DarkMagenta,
            Success  = ConsoleColor.Green,
            Error    = ConsoleColor.Red,
            Warning  = ConsoleColor.Yellow,
            Header   = ConsoleColor.Magenta,
            Skill    = ConsoleColor.Cyan,
            Mcp      = ConsoleColor.DarkCyan,
            Git      = ConsoleColor.Green,
        },
        ["monokai"] = new Theme
        {
            Name = "monokai",
            Accent   = ConsoleColor.Yellow,
            Muted    = ConsoleColor.DarkGray,
            Prompt   = ConsoleColor.Yellow,
            Response = ConsoleColor.White,
            Tool     = ConsoleColor.DarkYellow,
            Success  = ConsoleColor.Green,
            Error    = ConsoleColor.Red,
            Warning  = ConsoleColor.DarkYellow,
            Header   = ConsoleColor.Yellow,
            Skill    = ConsoleColor.Cyan,
            Mcp      = ConsoleColor.Blue,
            Git      = ConsoleColor.DarkGreen,
        },
        ["ocean"] = new Theme
        {
            Name = "ocean",
            Accent   = ConsoleColor.Blue,
            Muted    = ConsoleColor.DarkGray,
            Prompt   = ConsoleColor.Blue,
            Response = ConsoleColor.White,
            Tool     = ConsoleColor.DarkBlue,
            Success  = ConsoleColor.Cyan,
            Error    = ConsoleColor.Red,
            Warning  = ConsoleColor.DarkYellow,
            Header   = ConsoleColor.Blue,
            Skill    = ConsoleColor.Cyan,
            Mcp      = ConsoleColor.DarkCyan,
            Git      = ConsoleColor.Green,
        },
        ["forest"] = new Theme
        {
            Name = "forest",
            Accent   = ConsoleColor.Green,
            Muted    = ConsoleColor.DarkGray,
            Prompt   = ConsoleColor.Green,
            Response = ConsoleColor.White,
            Tool     = ConsoleColor.DarkGreen,
            Success  = ConsoleColor.Green,
            Error    = ConsoleColor.Red,
            Warning  = ConsoleColor.Yellow,
            Header   = ConsoleColor.Green,
            Skill    = ConsoleColor.Cyan,
            Mcp      = ConsoleColor.Blue,
            Git      = ConsoleColor.DarkGreen,
        },
        ["light"] = new Theme
        {
            Name = "light",
            Accent   = ConsoleColor.DarkCyan,
            Muted    = ConsoleColor.Gray,
            Prompt   = ConsoleColor.DarkCyan,
            Response = ConsoleColor.Black,
            Tool     = ConsoleColor.DarkBlue,
            Success  = ConsoleColor.DarkGreen,
            Error    = ConsoleColor.DarkRed,
            Warning  = ConsoleColor.DarkYellow,
            Header   = ConsoleColor.DarkCyan,
            Skill    = ConsoleColor.DarkMagenta,
            Mcp      = ConsoleColor.DarkBlue,
            Git      = ConsoleColor.DarkGreen,
        },
    };

    public static Theme Current { get; private set; } = BuiltIn["default"];

    public static bool TrySet(string name)
    {
        if (!BuiltIn.TryGetValue(name, out var theme)) return false;
        Current = theme;
        return true;
    }

    public static IEnumerable<string> Names => BuiltIn.Keys;
}
