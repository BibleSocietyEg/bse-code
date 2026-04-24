using System.Text;

/// <summary>
/// Console color palette and print helpers.
/// </summary>
public static class UI
{
    public static readonly ConsoleColor Accent   = ConsoleColor.Cyan;
    public static readonly ConsoleColor Muted    = ConsoleColor.DarkGray;
    public static readonly ConsoleColor Prompt   = ConsoleColor.Cyan;
    public static readonly ConsoleColor Response = ConsoleColor.White;
    public static readonly ConsoleColor ToolColor = ConsoleColor.DarkCyan;
    public static readonly ConsoleColor SuccessColor = ConsoleColor.Green;
    public static readonly ConsoleColor ErrColor     = ConsoleColor.Red;
    public static readonly ConsoleColor WarnColor    = ConsoleColor.DarkYellow;

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
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"  ⚠  {text}");
        Console.ResetColor();
    }

    public static void Success(string text)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✓  {text}");
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
            // Clear the spinner line
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
