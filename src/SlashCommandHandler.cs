using OpenAI.Chat;
using System.Text;

/// <summary>
/// Handles all <c>/command</c> inputs entered in the REPL.
/// Extracted from Program.cs to satisfy the Single Responsibility Principle —
/// the REPL loop should not also own command dispatch logic.
/// </summary>
public sealed class SlashCommandHandler
{
    private readonly AppConfig _config;
    private readonly ToolRegistry _toolRegistry;
    private readonly Func<ChatClient> _buildClient;
    private readonly Func<string> _buildSystemPrompt;
    private readonly Func<ChatClient, ChatCompletionOptions, List<ChatMessage>, string, Task> _runTurn;

    // Mutable reference so /model can swap the client
    public ChatClient Client { get; private set; }

    public SlashCommandHandler(
        AppConfig config,
        ToolRegistry toolRegistry,
        ChatClient initialClient,
        Func<ChatClient> buildClient,
        Func<string> buildSystemPrompt,
        Func<ChatClient, ChatCompletionOptions, List<ChatMessage>, string, Task> runTurn)
    {
        _config           = config;
        _toolRegistry     = toolRegistry;
        Client            = initialClient;
        _buildClient      = buildClient;
        _buildSystemPrompt = buildSystemPrompt;
        _runTurn          = runTurn;
    }

    /// <summary>
    /// Handles a slash command.
    /// Returns <c>1</c> to signal the REPL should exit, <c>0</c> to continue.
    /// </summary>
    public async Task<int> HandleAsync(
        string cmd,
        List<ChatMessage> messages,
        ChatCompletionOptions opts)
    {
        var parts = cmd.Split(' ', 2, StringSplitOptions.TrimEntries);
        var verb  = parts[0].ToLowerInvariant();
        var arg   = parts.Length > 1 ? parts[1] : "";

        switch (verb)
        {
            case "/exit":
            case "/quit":
                return 1;

            case "/clear":
                messages.RemoveAll(m => m is not SystemChatMessage);
                if (messages.Count > 0)
                    messages[0] = new SystemChatMessage(_buildSystemPrompt());
                UI.Print("  🧹 conversation cleared — fresh start!", UI.Muted);
                break;

            case "/model":
                if (!string.IsNullOrEmpty(arg))
                {
                    _config.Model = arg;
                    Client = _buildClient();
                    UI.Success($"🤖 model switched to: {arg}");
                }
                else
                {
                    UI.Print($"  🤖 current model: {_config.Model}", UI.Muted);
                }
                break;

            case "/help":
                PrintSlashHelp();
                break;

            case "/theme":
                HandleTheme(arg);
                break;

            case "/skills":
                HandleSkills();
                break;

            case "/mcp":
                await HandleMcpAsync(arg, opts);
                break;

            case "/memory":
                HandleMemory(arg, messages);
                break;

            case "/save":
                HandleSave(arg, messages);
                break;

            case "/resume":
            case "/load":
                HandleResume(arg, messages);
                break;

            case "/compact":
                await HandleCompactAsync(arg, messages, opts);
                break;

            case "/stats":
                // Stats are owned by the REPL; this command is handled there.
                // Returning a sentinel would complicate the interface, so we
                // leave stats in the REPL and skip here.
                break;

            case "/tools":
                HandleTools();
                break;

            case "/init":
                HandleInit(arg, messages);
                break;

            default:
                await HandleSkillInvocationAsync(verb, arg, messages, opts);
                break;
        }

        return 0;
    }

    // ── /theme ────────────────────────────────────────────────────────────────

    private void HandleTheme(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            UI.Header("Available themes");
            foreach (var t in ThemeManager.Names)
            {
                var marker = t == ThemeManager.Current.Name ? " ◀ active" : "";
                UI.Print($"    {t}{marker}", t == ThemeManager.Current.Name ? UI.Accent : UI.Muted);
            }
            UI.Print("  Usage: /theme <name>", UI.Muted);
        }
        else if (ThemeManager.TrySet(arg))
        {
            _config.Theme = arg;
            ConfigManager.SaveTheme(_config);
            UI.Success($"🎨 theme set to: {arg}");
        }
        else
        {
            UI.Error($"Unknown theme '{arg}' 😕 Try: {string.Join(", ", ThemeManager.Names)}");
        }
    }

    // ── /skills ───────────────────────────────────────────────────────────────

    private static void HandleSkills()
    {
        SkillManager.Reload();
        if (SkillManager.All.Count == 0)
        {
            UI.Print("  No skills found yet 🤷", UI.Muted);
            UI.Print("  Add .md files to ~/.bse-code/skills/ or .bse-code/skills/", UI.Muted);
        }
        else
        {
            UI.Header("Skills 🧠");
            foreach (var s in SkillManager.All)
            {
                var level = s.IsUserLevel ? "user" : "project";
                Console.ForegroundColor = UI.SkillColor;
                Console.Write($"    /{s.Name}");
                Console.ForegroundColor = UI.Muted;
                Console.WriteLine($"  [{level}]  {s.FilePath}");
                Console.ResetColor();
            }
        }
    }

    // ── /mcp ──────────────────────────────────────────────────────────────────

    private async Task HandleMcpAsync(string arg, ChatCompletionOptions opts)
    {
        var sub = arg.Split(' ', 2)[0].ToLowerInvariant();
        switch (sub)
        {
            case "list":
            case "ls":
            case "":
                if (McpManager.Servers.Count == 0)
                {
                    UI.Print("  No MCP servers configured yet 🔌", UI.Muted);
                    UI.Print("  Edit ~/.bse-code/mcp.json to add servers.", UI.Muted);
                }
                else
                {
                    UI.Header("MCP Servers 🔌");
                    foreach (var (name, srv) in McpManager.Servers)
                    {
                        Console.ForegroundColor = UI.McpColor;
                        Console.Write($"    {name}");
                        Console.ForegroundColor = UI.Muted;
                        Console.WriteLine($"  {srv.Command} {string.Join(" ", srv.Args)}");
                        Console.ResetColor();
                    }
                    UI.Header("MCP Tools 🛠️");
                    foreach (var t in McpManager.Tools)
                    {
                        Console.ForegroundColor = UI.McpColor;
                        Console.Write($"    {t.FullName}");
                        Console.ForegroundColor = UI.Muted;
                        Console.WriteLine($"  {t.Description}");
                        Console.ResetColor();
                    }
                }
                break;

            case "reload":
                using (new Spinner("Reloading MCP"))
                {
                    await McpManager.LoadAsync();
                    opts.Tools.Clear();
                    foreach (var t in _toolRegistry.ToChatTools()) opts.Tools.Add(t);
                    foreach (var t in McpManager.ToChatTools())    opts.Tools.Add(t);
                }
                UI.Success($"🔌 MCP reloaded — {McpManager.Tools.Count} tools ready to go!");
                break;

            default:
                UI.Print("  Usage: /mcp [list|reload]", UI.Muted);
                break;
        }
    }

    // ── /memory ───────────────────────────────────────────────────────────────

    private void HandleMemory(string arg, List<ChatMessage> messages)
    {
        var parts = arg.Split(' ', 2);
        switch (parts[0].ToLowerInvariant())
        {
            case "show":
            case "":
                MemoryManager.Reload();
                if (MemoryManager.Files.Count == 0)
                {
                    UI.Print("  No BSE.md files found yet 📭", UI.Muted);
                    UI.Print("  Create ./BSE.md or ~/.bse-code/BSE.md", UI.Muted);
                }
                else
                {
                    UI.Header("Memory files 💾");
                    foreach (var f in MemoryManager.Files)
                        UI.Print($"    {f.Label}", UI.Muted);
                }
                break;

            case "add":
                var note = parts.Length > 1 ? parts[1] : "";
                if (string.IsNullOrWhiteSpace(note))
                {
                    UI.Error("Usage: /memory add <text>");
                }
                else
                {
                    MemoryManager.AddNote(note);
                    RefreshSystemPrompt(messages);
                    UI.Success("📝 Note added to BSE.md!");
                }
                break;

            case "refresh":
                MemoryManager.Reload();
                RefreshSystemPrompt(messages);
                UI.Success("🔄 Memory refreshed!");
                break;

            default:
                UI.Print("  Usage: /memory [show|add <text>|refresh]", UI.Muted);
                break;
        }
    }

    // ── /save ─────────────────────────────────────────────────────────────────

    private void HandleSave(string tag, List<ChatMessage> messages)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            UI.Error("Usage: /save <tag>");
            return;
        }
        SessionManager.Save(tag, _config.Model, messages);
        UI.Success($"💾 Session saved as '{tag}'!");
    }

    // ── /resume ───────────────────────────────────────────────────────────────

    private void HandleResume(string arg, List<ChatMessage> messages)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            var sessions = SessionManager.List();
            if (sessions.Count == 0)
            {
                UI.Print("  No saved sessions yet 📭", UI.Muted);
            }
            else
            {
                UI.Header("Saved sessions 📂");
                foreach (var s in sessions)
                {
                    Console.ForegroundColor = UI.Accent;
                    Console.Write($"    {s.Tag}");
                    Console.ForegroundColor = UI.Muted;
                    Console.WriteLine($"  {s.SavedAt:yyyy-MM-dd HH:mm}  {s.Messages.Count} messages  [{s.Model}]");
                    Console.ResetColor();
                }
                UI.Print("  Usage: /resume <tag>", UI.Muted);
            }
            return;
        }

        var loaded = SessionManager.Resume(arg, out var meta);
        if (loaded is null)
        {
            UI.Error($"Session '{arg}' not found 😕");
            return;
        }

        messages.Clear();
        messages.Add(new SystemChatMessage(_buildSystemPrompt()));
        messages.AddRange(loaded);
        UI.Success($"▶️  Resumed session '{arg}' ({loaded.Count} messages) — welcome back!");

        if (meta?.Model is not null && meta.Model != _config.Model)
            UI.Warn($"Session was saved with model '{meta.Model}', current: '{_config.Model}'");
    }

    // ── /compact ──────────────────────────────────────────────────────────────

    private async Task HandleCompactAsync(
        string arg, List<ChatMessage> messages, ChatCompletionOptions opts)
    {
        var userCount = messages.Count(m => m is UserChatMessage);
        if (userCount < 3)
        {
            UI.Print("  Not enough history to compact yet 🤏", UI.Muted);
            return;
        }

        var prompt = string.IsNullOrEmpty(arg)
            ? "Summarize our conversation so far into a concise context summary. Keep key decisions, code changes, and important context."
            : arg;

        await _runTurn(Client, opts, messages, prompt);

        var summary = messages.LastOrDefault(m => m is AssistantChatMessage);
        messages.RemoveAll(m => m is not SystemChatMessage);
        if (summary is not null) messages.Add(summary);
        UI.Success("🗜️  Conversation compacted — nice and tidy!");
    }

    // ── /tools ────────────────────────────────────────────────────────────────

    private void HandleTools()
    {
        Console.WriteLine();
        UI.Header("Built-in tools 🔧");
        foreach (var name in _toolRegistry.ToolNames)
            UI.Print($"    {name}", UI.Muted);

        if (McpManager.Tools.Count > 0)
        {
            UI.Header("MCP tools 🔌");
            foreach (var t in McpManager.Tools)
            {
                Console.ForegroundColor = UI.McpColor;
                Console.Write($"    {t.FullName}");
                Console.ForegroundColor = UI.Muted;
                Console.WriteLine($"  — {t.Description}");
                Console.ResetColor();
            }
        }
    }

    // ── /init ─────────────────────────────────────────────────────────────────

    private void HandleInit(string arg, List<ChatMessage> messages)
    {
        var bseMdPath = Path.Combine(Directory.GetCurrentDirectory(), "BSE.md");
        if (File.Exists(bseMdPath) && string.IsNullOrEmpty(arg))
        {
            UI.Warn("BSE.md already exists 📄 Use /init --force to overwrite.");
            return;
        }

        var projectName = Path.GetFileName(Directory.GetCurrentDirectory());
        File.WriteAllText(bseMdPath, $"""
            # {projectName}

            ## Project Overview
            <!-- Describe your project here -->

            ## Tech Stack
            <!-- List your main technologies -->

            ## Development Commands
            ```sh
            # build
            # test
            # run
            ```

            ## Coding Standards
            <!-- Add your team's coding standards here -->
            """);

        MemoryManager.Reload();
        RefreshSystemPrompt(messages);
        UI.Success($"🎉 Created BSE.md in {Directory.GetCurrentDirectory()}");
    }

    // ── Dynamic skill invocation ──────────────────────────────────────────────

    private async Task HandleSkillInvocationAsync(
        string verb, string arg, List<ChatMessage> messages, ChatCompletionOptions opts)
    {
        var skillName = verb.TrimStart('/');
        var skill     = SkillManager.Find(skillName);
        if (skill is null)
        {
            UI.Print($"  🤔 unknown command: /{skillName}  (try /help)", UI.Muted);
            return;
        }

        Console.ForegroundColor = UI.SkillColor;
        Console.WriteLine($"  ◆ skill: {skill.Name} 🧠");
        Console.ResetColor();

        var skillPrompt = string.IsNullOrEmpty(arg)
            ? $"Execute the '{skill.Name}' skill:\n\n{skill.Content}"
            : $"Execute the '{skill.Name}' skill with argument: {arg}\n\n{skill.Content}";

        await _runTurn(Client, opts, messages, skillPrompt);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshSystemPrompt(List<ChatMessage> messages)
    {
        if (messages.Count > 0)
            messages[0] = new SystemChatMessage(_buildSystemPrompt());
    }

    public static void PrintSlashHelp()
    {
        Console.WriteLine();
        UI.Print("  Core:", UI.Accent);
        UI.Print("    /clear              🧹 clear conversation history", UI.Muted);
        UI.Print("    /model [id]         🤖 show or switch model", UI.Muted);
        UI.Print("    /compact [hint]     🗜️  summarize history to save tokens", UI.Muted);
        UI.Print("    /stats              📊 show session statistics", UI.Muted);
        UI.Print("    /tools              🔧 list available tools", UI.Muted);
        UI.Print("    /help               ❓ show this help", UI.Muted);
        UI.Print("    /exit               👋 quit", UI.Muted);
        Console.WriteLine();
        UI.Print("  Appearance:", UI.Accent);
        UI.Print("    /theme [name]       🎨 list or set color theme", UI.Muted);
        Console.WriteLine();
        UI.Print("  Skills:", UI.SkillColor);
        UI.Print("    /skills             🧠 list loaded skills", UI.Muted);
        UI.Print("    /<skill-name> [arg] ⚡ invoke a skill", UI.Muted);
        Console.WriteLine();
        UI.Print("  MCP:", UI.McpColor);
        UI.Print("    /mcp                🔌 list MCP servers and tools", UI.Muted);
        UI.Print("    /mcp reload         🔄 reload MCP servers", UI.Muted);
        Console.WriteLine();
        UI.Print("  Memory:", UI.Accent);
        UI.Print("    /memory             💾 show loaded BSE.md files", UI.Muted);
        UI.Print("    /memory add <text>  📝 append note to ./BSE.md", UI.Muted);
        UI.Print("    /memory refresh     🔄 reload BSE.md files", UI.Muted);
        UI.Print("    /init               🎉 create BSE.md in current directory", UI.Muted);
        Console.WriteLine();
        UI.Print("  Sessions:", UI.Accent);
        UI.Print("    /save <tag>         💾 save conversation", UI.Muted);
        UI.Print("    /resume [tag]       ▶️  list or resume a saved session", UI.Muted);
        Console.WriteLine();
    }
}
