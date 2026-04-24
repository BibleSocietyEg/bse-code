using System.Text;

/// <summary>
/// Console color palette and print helpers — theme-aware.
/// All color references go through ThemeManager.Current so switching
/// themes takes effect immediately.
/// </summary>
public static class UI
{
    // ── Theme-aware color accessors ───────────────────────────────────────────
    public static ConsoleColor Accent      => ThemeManager.Current.Accent;
    public static ConsoleColor Muted       => ThemeManager.Current.Muted;
    public static ConsoleColor Prompt      => ThemeManager.Current.Prompt;
    public static ConsoleColor Response    => ThemeManager.Current.Response;
    public static ConsoleColor ToolColor   => ThemeManager.Current.Tool;
    public static ConsoleColor SuccessColor => ThemeManager.Current.Success;
    public static ConsoleColor ErrColor    => ThemeManager.Current.Error;
    public static ConsoleColor WarnColor   => ThemeManager.Current.Warning;
    public static ConsoleColor SkillColor  => ThemeManager.Current.Skill;
    public static ConsoleColor McpColor    => ThemeManager.Current.Mcp;
    public static ConsoleColor GitColor    => ThemeManager.Current.Git;

    // ── Print helpers ─────────────────────────────────────────────────────────

    public static void Print(string text, ConsoleColor color, bool newline = true)
    {
        Console.ForegroundColor = color;
        if (newline) Console.WriteLine(text);
        else         Console.Write(text);
        Console.ResetColor();
    }

    public static void Error(string text)
    {
        Console.ForegroundColor = ErrColor;
        Console.Error.WriteLine($"  ✗  {text}");
        Console.ResetColor();
    }

    public static void Warn(string text)
    {
        Console.ForegroundColor = WarnColor;
        Console.WriteLine($"  ⚠  {text}");
        Console.ResetColor();
    }

    public static void Success(string text)
    {
        Console.ForegroundColor = SuccessColor;
        Console.WriteLine($"  ✓  {text}");
        Console.ResetColor();
    }

    public static void Header(string text)
    {
        Console.ForegroundColor = Accent;
        Console.WriteLine($"  ── {text} ──");
        Console.ResetColor();
    }

    public static void Rule(int width = 45)
    {
        Console.ForegroundColor = Muted;
        Console.WriteLine("  " + new string('─', width));
        Console.ResetColor();
    }
}

/// <summary>
/// Animated terminal spinner. Dispose or call Stop() to clear it.
/// </summary>
public sealed class Spinner : IDisposable
{
    private static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly string _label;
    private readonly Thread _thread;
    private volatile bool _running = true;
    private bool _stopped;

    public Spinner(string label = "Working")
    {
        _label = label;
        Console.CursorVisible = false;

        _thread = new Thread(() =>
        {
            int i = 0;
            while (_running)
            {
                Console.ForegroundColor = UI.Accent;
                Console.Write($"\r  {Frames[i % Frames.Length]}  {_label}...");
                Console.ResetColor();
                Thread.Sleep(80);
                i++;
            }
            Console.Write($"\r{new string(' ', _label.Length + 12)}\r");
            Console.CursorVisible = true;
        })
        { IsBackground = true };

        _thread.Start();
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _running = false;
        _thread.Join(500);
    }

    public void Dispose() => Stop();
}
