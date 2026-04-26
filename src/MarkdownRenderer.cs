using Spectre.Console;

/// <summary>
/// Renders markdown text to the terminal using Spectre.Console.
/// Falls back to plain text when NO_COLOR is set or output is redirected.
/// </summary>
public static class MarkdownRenderer
{
    /// <summary>
    /// True when the terminal does not support ANSI escape codes.
    /// Checked at render time (not cached) to respect runtime changes.
    /// </summary>
    public static bool IsPlainText =>
        Environment.GetEnvironmentVariable("NO_COLOR") is not null
        || Console.IsOutputRedirected;

    /// <summary>
    /// Renders markdown text to the terminal.
    /// Uses Spectre.Console markup when ANSI is supported; plain text otherwise.
    /// </summary>
    public static void Render(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return;

        if (IsPlainText)
        {
            Console.Write(markdown);
            return;
        }

        try
        {
            var markup = ConvertMarkdownToSpectreMarkup(markdown);
            AnsiConsole.Markup(markup);
        }
        catch
        {
            // Fall back to plain text on any Spectre markup error
            Console.Write(markdown);
        }
    }

    /// <summary>
    /// Converts a markdown string to Spectre.Console markup syntax.
    /// Handles: fenced code blocks, headers, bold, italic, inline code.
    /// </summary>
    internal static string ConvertMarkdownToSpectreMarkup(string markdown)
    {
        var sb = new System.Text.StringBuilder();
        var lines = markdown.Split('\n');
        bool inCodeBlock = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Fenced code block start/end
            if (line.TrimStart().StartsWith("```"))
            {
                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    var lang = line.TrimStart()[3..].Trim();
                    var langLabel = string.IsNullOrEmpty(lang) ? "code" : lang;
                    sb.AppendLine($"[grey]── {EscapeMarkup(langLabel)} ──[/]");
                }
                else
                {
                    inCodeBlock = false;
                    sb.AppendLine("[grey]────────────────[/]");
                }
                continue;
            }

            if (inCodeBlock)
            {
                sb.AppendLine($"[cyan]{EscapeMarkup(line)}[/]");
                continue;
            }

            // Headers
            if (line.StartsWith("### "))
            {
                sb.AppendLine($"[bold yellow]{EscapeMarkup(line[4..])}[/]");
                continue;
            }
            if (line.StartsWith("## "))
            {
                sb.AppendLine($"[bold blue]{EscapeMarkup(line[3..])}[/]");
                continue;
            }
            if (line.StartsWith("# "))
            {
                sb.AppendLine($"[bold underline]{EscapeMarkup(line[2..])}[/]");
                continue;
            }

            sb.AppendLine(ProcessInlineMarkdown(line));
        }

        return sb.ToString();
    }

    /// <summary>Processes inline bold (**text**), italic (*text*), and code (`text`) markdown.</summary>
    internal static string ProcessInlineMarkdown(string line)
    {
        // Bold: **text** → [bold]text[/]
        var result = System.Text.RegularExpressions.Regex.Replace(
            line,
            @"\*\*(.+?)\*\*",
            m => $"[bold]{EscapeMarkup(m.Groups[1].Value)}[/]");

        // Italic: *text* → [italic]text[/]
        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)",
            m => $"[italic]{EscapeMarkup(m.Groups[1].Value)}[/]");

        // Inline code: `text` → [cyan]text[/]
        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            @"`([^`]+)`",
            m => $"[cyan]{EscapeMarkup(m.Groups[1].Value)}[/]");

        return result;
    }

    /// <summary>Escapes Spectre.Console markup special characters.</summary>
    internal static string EscapeMarkup(string text) =>
        text.Replace("[", "[[").Replace("]", "]]");
}
