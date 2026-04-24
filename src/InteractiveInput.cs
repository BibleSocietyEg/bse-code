using System.Text;

/// <summary>
/// Interactive readline replacement with:
///   - Up/Down arrow history navigation
///   - '/' triggers an inline slash-command picker
///   - '@' triggers a file-path autocomplete picker
///   - Tab completes the current slash command or file path
///   - Ctrl+C / Ctrl+D returns null (exit signal)
///   - Left/Right/Home/End cursor movement
///   - Backspace / Delete editing
/// </summary>
public static class InteractiveInput
{
    // ── History ───────────────────────────────────────────────────────────────

    private static readonly List<string> _history = [];
    private static int _historyIndex = -1;

    // ── Slash command registry ────────────────────────────────────────────────

    private static readonly (string Command, string Description)[] BuiltinCommands =
    [
        ("/clear",          "🧹 clear conversation history"),
        ("/model",          "🤖 show or switch model"),
        ("/compact",        "🗜️  summarize history to save tokens"),
        ("/stats",          "📊 show session statistics"),
        ("/tools",          "🔧 list available tools"),
        ("/theme",          "🎨 list or set color theme"),
        ("/skills",         "🧠 list loaded skills"),
        ("/mcp",            "🔌 list MCP servers and tools"),
        ("/memory",         "💾 show loaded BSE.md files"),
        ("/memory add",     "📝 append note to ./BSE.md"),
        ("/memory refresh", "🔄 reload BSE.md files"),
        ("/init",           "🎉 create BSE.md in current directory"),
        ("/save",           "💾 save conversation"),
        ("/resume",         "▶️  list or resume a saved session"),
        ("/help",           "❓ show help"),
        ("/exit",           "👋 quit"),
    ];

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Reads a line of input with full interactive editing.
    /// Returns <c>null</c> on Ctrl+C / Ctrl+D (exit signal).
    /// </summary>
    public static string? ReadLine()
    {
        var buf     = new StringBuilder();
        int cursor  = 0;          // logical cursor position in buf
        _historyIndex = -1;
        string savedLine = "";    // preserves draft when browsing history

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            // ── Exit signals ──────────────────────────────────────────────────
            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                Console.WriteLine("^C");
                return null;
            }
            if (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control)
                && buf.Length == 0)
            {
                Console.WriteLine();
                return null;
            }

            // ── Enter ─────────────────────────────────────────────────────────
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                var line = buf.ToString();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    // Avoid duplicate consecutive entries
                    if (_history.Count == 0 || _history[^1] != line)
                        _history.Add(line);
                }
                return line;
            }

            // ── History navigation ────────────────────────────────────────────
            if (key.Key == ConsoleKey.UpArrow)
            {
                if (_history.Count == 0) continue;
                if (_historyIndex == -1)
                {
                    savedLine = buf.ToString();
                    _historyIndex = _history.Count - 1;
                }
                else if (_historyIndex > 0)
                {
                    _historyIndex--;
                }
                ReplaceBuffer(buf, _history[_historyIndex], ref cursor);
                continue;
            }

            if (key.Key == ConsoleKey.DownArrow)
            {
                if (_historyIndex == -1) continue;
                if (_historyIndex < _history.Count - 1)
                {
                    _historyIndex++;
                    ReplaceBuffer(buf, _history[_historyIndex], ref cursor);
                }
                else
                {
                    _historyIndex = -1;
                    ReplaceBuffer(buf, savedLine, ref cursor);
                }
                continue;
            }

            // ── Cursor movement ───────────────────────────────────────────────
            if (key.Key == ConsoleKey.LeftArrow)
            {
                if (cursor > 0)
                {
                    cursor--;
                    Console.CursorLeft--;
                }
                continue;
            }

            if (key.Key == ConsoleKey.RightArrow)
            {
                if (cursor < buf.Length)
                {
                    cursor++;
                    Console.CursorLeft++;
                }
                continue;
            }

            if (key.Key == ConsoleKey.Home)
            {
                MoveCursorTo(buf, ref cursor, 0);
                continue;
            }

            if (key.Key == ConsoleKey.End)
            {
                MoveCursorTo(buf, ref cursor, buf.Length);
                continue;
            }

            // ── Delete / Backspace ────────────────────────────────────────────
            if (key.Key == ConsoleKey.Backspace)
            {
                if (cursor > 0)
                {
                    buf.Remove(cursor - 1, 1);
                    cursor--;
                    RedrawFromCursor(buf, cursor);
                }
                continue;
            }

            if (key.Key == ConsoleKey.Delete)
            {
                if (cursor < buf.Length)
                {
                    buf.Remove(cursor, 1);
                    RedrawFromCursor(buf, cursor);
                }
                continue;
            }

            // ── Tab completion ────────────────────────────────────────────────
            if (key.Key == ConsoleKey.Tab)
            {
                var text = buf.ToString();
                if (text.StartsWith('/'))
                    TryCompleteSlash(buf, ref cursor, text);
                else if (text.StartsWith('@'))
                    TryCompletePath(buf, ref cursor, text[1..], "@");
                continue;
            }

            // ── '/' at start → interactive picker ────────────────────────────
            if (key.KeyChar == '/' && buf.Length == 0)
            {
                var picked = RunSlashPicker("");
                if (picked is not null)
                {
                    buf.Clear();
                    buf.Append(picked);
                    cursor = buf.Length;
                    RedrawLine(buf, cursor);
                }
                else
                {
                    // User pressed Escape — put '/' in buffer so they can type manually
                    buf.Append('/');
                    cursor = 1;
                    Console.Write('/');
                }
                continue;
            }

            // ── '@' at start → file picker ────────────────────────────────────
            if (key.KeyChar == '@' && buf.Length == 0)
            {
                buf.Append('@');
                cursor = 1;
                Console.Write('@');
                // Immediately try to show completions for current dir
                continue;
            }

            // ── Regular character ─────────────────────────────────────────────
            if (key.KeyChar != '\0')
            {
                buf.Insert(cursor, key.KeyChar);
                cursor++;

                // After typing '/' characters, show live filter picker
                if (buf[0] == '/' && buf.Length > 1)
                {
                    var partial = buf.ToString();
                    // Only show picker if no space yet (still typing command name)
                    if (!partial.Contains(' '))
                    {
                        var picked = RunSlashPicker(partial);
                        if (picked is not null)
                        {
                            buf.Clear();
                            buf.Append(picked);
                            cursor = buf.Length;
                            RedrawLine(buf, cursor);
                        }
                        else
                        {
                            // Escape pressed — keep what was typed
                            RedrawLine(buf, cursor);
                        }
                        continue;
                    }
                }

                RedrawFromCursor(buf, cursor - 1);
            }
        }
    }

    // ── Slash command picker ──────────────────────────────────────────────────

    /// <summary>
    /// Shows an inline filterable list of slash commands.
    /// Returns the selected command string, or null if Escape was pressed.
    /// </summary>
    private static string? RunSlashPicker(string initial)
    {
        // Clear current line
        ClearCurrentLine();

        var filter    = initial.Length > 1 ? initial[1..] : "";
        var selected  = 0;

        while (true)
        {
            var allItems = GetSlashItems(filter);

            if (allItems.Count == 0)
            {
                // Nothing matches — fall back to manual typing
                Console.Write(initial);
                return null;
            }

            selected = Math.Clamp(selected, 0, allItems.Count - 1);
            RenderPicker(allItems, selected, filter, "/");

            var k = Console.ReadKey(intercept: true);

            if (k.Key == ConsoleKey.Escape)
            {
                ClearPickerLines(allItems.Count);
                return null;
            }

            if (k.Key == ConsoleKey.Enter)
            {
                ClearPickerLines(allItems.Count);
                return allItems[selected].Value;
            }

            if (k.Key == ConsoleKey.UpArrow)
            {
                selected = selected > 0 ? selected - 1 : allItems.Count - 1;
                ClearPickerLines(allItems.Count);
                continue;
            }

            if (k.Key == ConsoleKey.DownArrow)
            {
                selected = selected < allItems.Count - 1 ? selected + 1 : 0;
                ClearPickerLines(allItems.Count);
                continue;
            }

            if (k.Key == ConsoleKey.Backspace)
            {
                if (filter.Length > 0)
                    filter = filter[..^1];
                selected = 0;
                ClearPickerLines(allItems.Count);
                continue;
            }

            if (k.KeyChar != '\0' && k.Key != ConsoleKey.Tab)
            {
                filter += k.KeyChar;
                selected = 0;
                ClearPickerLines(allItems.Count);
                continue;
            }

            // Tab — accept top match
            if (k.Key == ConsoleKey.Tab && allItems.Count > 0)
            {
                ClearPickerLines(allItems.Count);
                return allItems[0].Value;
            }
        }
    }

    // ── File path picker ──────────────────────────────────────────────────────

    private static void TryCompletePath(StringBuilder buf, ref int cursor, string partial, string prefix)
    {
        var dir      = Path.GetDirectoryName(partial) ?? ".";
        var stem     = Path.GetFileName(partial);
        if (string.IsNullOrEmpty(dir)) dir = ".";

        List<string> matches;
        try
        {
            matches = Directory.GetFileSystemEntries(dir, stem + "*")
                .Select(p => Path.GetRelativePath(".", p).Replace('\\', '/'))
                .Take(20)
                .ToList();
        }
        catch { return; }

        if (matches.Count == 0) return;

        if (matches.Count == 1)
        {
            var completed = prefix + matches[0];
            buf.Clear();
            buf.Append(completed);
            cursor = buf.Length;
            RedrawLine(buf, cursor);
            return;
        }

        // Show picker
        ClearCurrentLine();
        var items = matches.Select(m => new PickerItem(m, m)).ToList();
        var picked = RunGenericPicker(items, "@");
        if (picked is not null)
        {
            buf.Clear();
            buf.Append(prefix + picked);
            cursor = buf.Length;
            RedrawLine(buf, cursor);
        }
        else
        {
            RedrawLine(buf, cursor);
        }
    }

    private static void TryCompleteSlash(StringBuilder buf, ref int cursor, string text)
    {
        var filter = text.Length > 1 ? text[1..] : "";
        var items  = GetSlashItems(filter);
        if (items.Count == 0) return;

        if (items.Count == 1)
        {
            buf.Clear();
            buf.Append(items[0].Value);
            cursor = buf.Length;
            RedrawLine(buf, cursor);
            return;
        }

        ClearCurrentLine();
        var picked = RunGenericPicker(items, "/");
        if (picked is not null)
        {
            buf.Clear();
            buf.Append(picked);
            cursor = buf.Length;
            RedrawLine(buf, cursor);
        }
        else
        {
            RedrawLine(buf, cursor);
        }
    }

    // ── Generic picker (reusable) ─────────────────────────────────────────────

    private record PickerItem(string Label, string Value);

    private static string? RunGenericPicker(List<PickerItem> items, string prefix)
    {
        var selected = 0;
        while (true)
        {
            RenderPicker(items, selected, "", prefix);
            var k = Console.ReadKey(intercept: true);

            if (k.Key == ConsoleKey.Escape)
            {
                ClearPickerLines(items.Count);
                return null;
            }
            if (k.Key == ConsoleKey.Enter || k.Key == ConsoleKey.Tab)
            {
                ClearPickerLines(items.Count);
                return items[selected].Value;
            }
            if (k.Key == ConsoleKey.UpArrow)
                selected = selected > 0 ? selected - 1 : items.Count - 1;
            else if (k.Key == ConsoleKey.DownArrow)
                selected = selected < items.Count - 1 ? selected + 1 : 0;

            ClearPickerLines(items.Count);
        }
    }

    // ── Picker rendering ──────────────────────────────────────────────────────

    private static void RenderPicker(List<PickerItem> items, int selected, string filter, string prefix)
    {
        var maxVisible = Math.Min(items.Count, 10);
        var startIdx   = Math.Max(0, Math.Min(selected - maxVisible / 2, items.Count - maxVisible));

        // Show filter hint on current line
        Console.ForegroundColor = UI.Prompt;
        Console.Write($"  {prefix}{filter}");
        Console.ResetColor();
        Console.ForegroundColor = UI.Muted;
        Console.Write("  ↑↓ navigate · Enter select · Esc cancel");
        Console.ResetColor();
        Console.WriteLine();

        for (int i = startIdx; i < startIdx + maxVisible; i++)
        {
            var item    = items[i];
            var isActive = i == selected;

            if (isActive)
            {
                Console.ForegroundColor = UI.Accent;
                Console.Write("  ▶ ");
            }
            else
            {
                Console.ForegroundColor = UI.Muted;
                Console.Write("    ");
            }

            // Split label into command + description parts
            var parts = item.Label.Split("  ", 2);
            Console.ForegroundColor = isActive ? UI.Accent : ConsoleColor.White;
            Console.Write(parts[0].PadRight(22));

            if (parts.Length > 1)
            {
                Console.ForegroundColor = UI.Muted;
                Console.Write(parts[1]);
            }

            Console.ResetColor();
            Console.WriteLine();
        }

        if (items.Count > maxVisible)
        {
            Console.ForegroundColor = UI.Muted;
            Console.WriteLine($"  … {items.Count - maxVisible} more (keep typing to filter)");
        }
    }

    private static void ClearPickerLines(int itemCount)
    {
        var visibleItems = Math.Min(itemCount, 10);
        var extraLine    = itemCount > 10 ? 1 : 0;
        var totalLines   = visibleItems + 1 + extraLine; // items + header + optional "… N more"
        for (int i = 0; i < totalLines; i++)
        {
            if (Console.CursorTop == 0) break;
            Console.SetCursorPosition(0, Console.CursorTop - 1);
            Console.Write(new string(' ', Console.WindowWidth - 1));
            Console.SetCursorPosition(0, Console.CursorTop);
        }
    }

    // ── Slash items builder ───────────────────────────────────────────────────

    private static List<PickerItem> GetSlashItems(string filter)
    {
        var all = BuiltinCommands
            .Select(c => new PickerItem($"{c.Command}  {c.Description}", c.Command))
            .Concat(SkillManager.All.Select(s =>
            {
                var cmd  = $"/{s.Name}";
                var desc = $"⚡ skill [{(s.IsUserLevel ? "user" : "project")}]";
                return new PickerItem($"{cmd}  {desc}", cmd);
            }))
            .ToList();

        if (string.IsNullOrEmpty(filter)) return all;

        return all
            .Where(i => i.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
                     || i.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ── Buffer / cursor helpers ───────────────────────────────────────────────

    private static void ReplaceBuffer(StringBuilder buf, string newText, ref int cursor)
    {
        ClearCurrentLine();
        buf.Clear();
        buf.Append(newText);
        cursor = buf.Length;
        Console.Write(newText);
    }

    private static void RedrawFromCursor(StringBuilder buf, int fromPos)
    {
        // fromPos is the position BEFORE the change; cursor is already updated in buf.
        // We need to rewrite from fromPos to end of buf, then erase any leftover char.
        var suffix = buf.ToString()[fromPos..];
        int startLeft = Console.CursorLeft;
        Console.Write(suffix + " "); // trailing space erases a deleted character
        // Move cursor back to logical position (end of suffix, before the trailing space)
        int targetLeft = startLeft + suffix.Length;
        Console.CursorLeft = Math.Max(0, Math.Min(targetLeft, Console.BufferWidth - 1));
    }

    private static void RedrawLine(StringBuilder buf, int cursor)
    {
        ClearCurrentLine();
        Console.Write(buf.ToString());
        // Position cursor correctly
        if (cursor < buf.Length)
            Console.CursorLeft = Console.CursorLeft - (buf.Length - cursor);
    }

    private static void ClearCurrentLine()
    {
        Console.CursorLeft = 0;
        Console.Write(new string(' ', Console.WindowWidth - 1));
        Console.CursorLeft = 0;
    }

    private static void MoveCursorTo(StringBuilder buf, ref int cursor, int target)
    {
        var delta = target - cursor;
        cursor = target;
        Console.CursorLeft += delta;
    }
}
