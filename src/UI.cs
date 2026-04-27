using System.Text;

/// <summary>
/// Console color palette and print helpers — theme-aware.
/// All color references go through ThemeManager.Current so switching
/// themes takes effect immediately.
/// </summary>
public static class UI
{
    // ── Theme-aware color accessors ───────────────────────────────────────────
    public static ConsoleColor Accent => ThemeManager.Current.Accent;
    public static ConsoleColor Muted => ThemeManager.Current.Muted;
    public static ConsoleColor Prompt => ThemeManager.Current.Prompt;
    public static ConsoleColor Response => ThemeManager.Current.Response;
    public static ConsoleColor ToolColor => ThemeManager.Current.Tool;
    public static ConsoleColor SuccessColor => ThemeManager.Current.Success;
    public static ConsoleColor ErrColor => ThemeManager.Current.Error;
    public static ConsoleColor WarnColor => ThemeManager.Current.Warning;
    public static ConsoleColor SkillColor => ThemeManager.Current.Skill;
    public static ConsoleColor McpColor => ThemeManager.Current.Mcp;
    public static ConsoleColor GitColor => ThemeManager.Current.Git;

    // ── Print helpers ─────────────────────────────────────────────────────────

    public static void Print(string text, ConsoleColor color, bool newline = true)
    {
        Console.ForegroundColor = color;
        if (newline) Console.WriteLine(text);
        else Console.Write(text);
        Console.ResetColor();
    }

    public static void Error(string text)
    {
        Console.ForegroundColor = ErrColor;
        Console.Error.WriteLine($"  ❌  {text}");
        Console.ResetColor();
    }

    public static void Warn(string text)
    {
        Console.ForegroundColor = WarnColor;
        Console.WriteLine($"  ⚠️  {text}");
        Console.ResetColor();
    }

    public static void Success(string text)
    {
        Console.ForegroundColor = SuccessColor;
        Console.WriteLine($"  ✅  {text}");
        Console.ResetColor();
    }

    public static void Header(string text)
    {
        Console.ForegroundColor = Accent;
        Console.WriteLine($"  ✨ {text} ✨");
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
    private Thread? _thread;
    private volatile bool _running;
    private bool _stopped;

    public Spinner(string label = "Working")
    {
        _label = label;
        Start();
    }

    public void Start()
    {
        if (_running) return;
        _stopped = false;
        _running = true;
        try { Console.CursorVisible = false; } catch { /* non-interactive environment */ }

        _thread = new Thread(() =>
        {
            int i = 0;
            while (_running)
            {
                try
                {
                    Console.ForegroundColor = UI.Accent;
                    Console.Write($"\r  {Frames[i % Frames.Length]}  {_label}...");
                    Console.ResetColor();
                }
                catch { break; }
                Thread.Sleep(80);
                i++;
            }
            try
            {
                Console.Write($"\r{new string(' ', _label.Length + 12)}\r");
                Console.CursorVisible = true;
            }
            catch { /* non-interactive environment */ }
        })
        { IsBackground = true };

        _thread.Start();
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _running = false;
        _thread?.Join(500);
    }

    public void Dispose() => Stop();
}

/// <summary>
/// Inline spinner that animates on the current cursor position while a tool is executing.
/// Call Stop() (or Dispose()) to clear the spinner frames before writing the result.
/// The spinner uses the theme's ToolColor so it respects the active color theme.
/// Safe to use in non-interactive environments (CI, redirected output) — console
/// operations that require a real terminal are guarded with try/catch.
/// </summary>
public sealed class ToolSpinner : IDisposable
{
    private static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private Thread? _thread;
    private volatile bool _running;
    private bool _stopped;

    public ToolSpinner()
    {
        _running = true;
        try { Console.CursorVisible = false; } catch { /* non-interactive environment */ }

        _thread = new Thread(() =>
        {
            int i = 0;
            while (_running)
            {
                try
                {
                    Console.ForegroundColor = UI.ToolColor;
                    Console.Write($"\b{Frames[i % Frames.Length]}");
                    Console.ResetColor();
                }
                catch { /* non-interactive environment — stop gracefully */ break; }
                Thread.Sleep(80);
                i++;
            }
            // Erase the spinner frame so the result (✓/✗) can be written cleanly
            try
            {
                Console.Write("\b ");
                Console.CursorVisible = true;
            }
            catch { /* non-interactive environment */ }
        })
        { IsBackground = true };

        _thread.Start();
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _running = false;
        _thread?.Join(500);
    }

    public void Dispose() => Stop();
}
