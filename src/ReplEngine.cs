using System.ClientModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;

/// <summary>
/// Encapsulates the interactive REPL loop and one-shot execution path.
/// Extracted from Program.cs to make the core logic unit-testable.
/// </summary>
public sealed class ReplEngine
{
    private readonly AppConfig _config;
    private readonly ToolRegistry _toolRegistry;
    private readonly Func<ChatClient> _buildClient;
    private readonly Func<string> _buildSystemPrompt;
    private readonly Func<ChatCompletionOptions> _buildOptions;

    private DateTime _sessionStart = DateTime.UtcNow;
    private int _sessionTurns = 0;
    private int _sessionToolCalls = 0;

    public ReplEngine(
        AppConfig config,
        ToolRegistry toolRegistry,
        Func<ChatClient> buildClient,
        Func<string> buildSystemPrompt,
        Func<ChatCompletionOptions> buildOptions)
    {
        _config = config;
        _toolRegistry = toolRegistry;
        _buildClient = buildClient;
        _buildSystemPrompt = buildSystemPrompt;
        _buildOptions = buildOptions;
    }

    // ── Entry points ──────────────────────────────────────────────────────────

    /// <summary>Runs the interactive REPL loop.</summary>
    public async Task RunAsync()
    {
        _sessionStart = DateTime.UtcNow;
        _sessionTurns = 0;
        _sessionToolCalls = 0;

        var client = _buildClient();
        var messages = new List<ChatMessage> { new SystemChatMessage(_buildSystemPrompt()) };
        var opts = _buildOptions();

        var slashHandler = new SlashCommandHandler(
            _config, _toolRegistry, client, _buildClient, _buildSystemPrompt,
            async (c, o, m, u) => await RunTurnAsync(c, o, m, u));

        PrintBanner(_config.Model, _config.Provider);

        while (true)
        {
            var cwd = Path.GetFileName(Directory.GetCurrentDirectory());
            var branch = GetGitBranch();

            Console.WriteLine();
            Console.ForegroundColor = UI.Accent;
            Console.Write($" {cwd} ");
            if (branch is not null)
            {
                Console.ForegroundColor = UI.GitColor;
                Console.Write($"({branch}) ");
            }
            Console.ForegroundColor = UI.Prompt;
            Console.Write("❯ ");
            Console.ResetColor();

            var input = InteractiveInput.ReadLine();

            if (input is null || input.Trim() is "/exit" or "/quit" or "exit" or "quit")
            {
                Console.WriteLine();
                UI.Print("  See ya! 👋 Happy coding! 🚀", UI.Muted);
                Console.WriteLine();
                break;
            }

            if (string.IsNullOrWhiteSpace(input)) continue;

            // ! prefix: shell passthrough
            if (input.TrimStart().StartsWith('!'))
            {
                var shellCmd = input.TrimStart()[1..].Trim();
                if (string.IsNullOrWhiteSpace(shellCmd))
                {
                    UI.Warn("🔧 Usage: !<command>  e.g. !git status");
                    continue;
                }
                var result = BashTool.RunShell(shellCmd);
                Console.ForegroundColor = UI.Muted;
                Console.WriteLine(result);
                Console.ResetColor();
                continue;
            }

            // @ prefix: inject file/directory content into prompt
            if (input.TrimStart().StartsWith('@'))
            {
                var parts = input.TrimStart().Split(' ', 2);
                var atPath = parts[0][1..].Trim();
                var rest = parts.Length > 1 ? parts[1] : "";
                var injected = InjectAtPath(atPath, rest);
                if (injected is null) continue;
                input = injected;
            }

            // Slash commands
            if (input.Trim().StartsWith('/'))
            {
                if (input.Trim().Equals("/stats", StringComparison.OrdinalIgnoreCase))
                {
                    PrintStats(messages, _sessionStart, _sessionTurns, _sessionToolCalls);
                    continue;
                }

                var exitCode = await slashHandler.HandleAsync(input.Trim(), messages, opts);
                // Sync client reference in case /model changed it
                client = slashHandler.Client;
                if (exitCode == 1) break;
                continue;
            }

            _sessionTurns++;
            await RunTurnAsync(client, opts, messages, input.Trim());
        }
    }

    /// <summary>Runs a single one-shot prompt and exits.</summary>
    public async Task RunOneShotAsync(string prompt, string outputFormat)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            UI.Error("Prompt must not be empty. Give me something to work with! 😅");
            Environment.Exit(1);
        }

        var client = _buildClient();
        var messages = new List<ChatMessage> { new SystemChatMessage(_buildSystemPrompt()) };
        var opts = _buildOptions();

        if (outputFormat == "json")
        {
            var result = new StringBuilder();
            await RunTurnAsync(client, opts, messages, prompt, captureOutput: result);
            Console.WriteLine(JsonSerializer.Serialize(new { response = result.ToString() }));
        }
        else
        {
            await RunTurnAsync(client, opts, messages, prompt);
            Console.WriteLine();
        }
    }

    // ── Single turn ───────────────────────────────────────────────────────────

    /// <summary>Executes one conversation turn, handling streaming and tool calls.</summary>
    internal async Task RunTurnAsync(
        ChatClient chatClient,
        ChatCompletionOptions opts,
        List<ChatMessage> messages,
        string userInput,
        StringBuilder? captureOutput = null)
    {
        messages.Add(new UserChatMessage(userInput));

        while (true)
        {
            using var spinner = new Spinner("  ✨ Thinking");
            var contentBuilder = new StringBuilder();
            var accumulators = new Dictionary<int, ToolCallAccumulator>();

            const int maxRetries = 5;
            int retryCount = 0;
            while (true)
            {
                contentBuilder.Clear();
                accumulators.Clear();
                try
                {
                    var stream = chatClient.CompleteChatStreamingAsync(messages, opts);
                    spinner.Stop();
                    Console.WriteLine();

                    await foreach (var update in stream)
                    {
                        foreach (var part in update.ContentUpdate)
                        {
                            if (!string.IsNullOrEmpty(part.Text))
                            {
                                // During streaming: only buffer, don't write to console
                                contentBuilder.Append(part.Text);
                                captureOutput?.Append(part.Text);
                            }
                        }

                        foreach (var delta in update.ToolCallUpdates)
                        {
                            if (!accumulators.TryGetValue(delta.Index, out var acc))
                            {
                                acc = new ToolCallAccumulator(delta.ToolCallId ?? "", delta.FunctionName ?? "")
                                { Index = delta.Index };
                                accumulators[delta.Index] = acc;
                            }
                            if (!string.IsNullOrEmpty(delta.FunctionName)) acc.Name = delta.FunctionName;
                            if (!string.IsNullOrEmpty(delta.ToolCallId)) acc.Id = delta.ToolCallId;
                            acc.AppendArguments(delta.FunctionArgumentsUpdate);
                        }
                    }
                    break; // success — exit retry loop
                }
                catch (ClientResultException ex) when (ex.Status == 429 && retryCount < maxRetries)
                {
                    spinner.Stop();
                    retryCount++;
                    int delaySeconds = (int)Math.Pow(2, retryCount);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n  ⚠ Rate limited (429). Retrying in {delaySeconds}s... (attempt {retryCount}/{maxRetries})");
                    Console.ResetColor();
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    spinner.Start();
                }
                catch
                {
                    spinner.Stop();
                    throw;
                }
            }

            if (contentBuilder.Length > 0)
            {
                MarkdownRenderer.Render(contentBuilder.ToString());
                Console.WriteLine();
            }

            if (accumulators.Count == 0)
            {
                if (contentBuilder.Length > 0)
                    messages.Add(new AssistantChatMessage(contentBuilder.ToString()));
                break;
            }

            var toolCalls = accumulators.Values
                .OrderBy(a => a.Index)
                .Select(a => ChatToolCall.CreateFunctionToolCall(
                    a.Id, a.Name, BinaryData.FromString(a.Arguments)))
                .ToList();

            messages.Add(new AssistantChatMessage(toolCalls));

            // ── Parallel tool dispatch with per-file serialization ────────────
            var orderedAccs = accumulators.Values.OrderBy(a => a.Index).ToList();
            var fileLocks = new System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>();

            // Print all tool call headers before dispatching
            foreach (var acc in orderedAccs)
                PrintToolCall(acc.Name, acc.Arguments);

            _sessionToolCalls += orderedAccs.Count;

            // Execute all tool calls concurrently, serializing same-file-path calls
            var tasks = orderedAccs.Select(async acc =>
            {
                var filePath = ExtractFilePath(acc.Name, acc.Arguments) ?? "__no_file__";
                var sem = fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
                await sem.WaitAsync();
                try
                {
                    string result;
                    bool success = true;
                    try
                    {
                        result = acc.Name.StartsWith("mcp__")
                            ? await HandleMcpToolAsync(acc.Name, acc.Arguments)
                            : await _toolRegistry.ExecuteAsync(acc.Name, acc.Arguments);
                    }
                    catch (Exception ex)
                    {
                        result = $"ERROR: {ex.Message}";
                        success = false;
                    }
                    PrintToolResult(acc.Name, result, success);
                    return result;
                }
                finally { sem.Release(); }
            }).ToList();

            var results = await Task.WhenAll(tasks);

            foreach (var (toolCall, result) in toolCalls.Zip(results))
                messages.Add(new ToolChatMessage(toolCall.Id, result));
        }
    }

    // ── MCP tool dispatch ─────────────────────────────────────────────────────

    /// <summary>Dispatches a tool call to the appropriate MCP server.</summary>
    internal static async Task<string> HandleMcpToolAsync(string fullName, string argsJson)
    {
        // fullName = mcp__serverName__toolName
        var parts = fullName.Split("__", 3);
        if (parts.Length < 3) return $"Invalid MCP tool name: {fullName}";
        return await McpManager.CallToolAsync(parts[1], parts[2], argsJson);
    }

    /// <summary>
    /// Extracts the file path from tool arguments for same-file serialization.
    /// Returns null for tools that don't operate on a specific file.
    /// </summary>
    internal static string? ExtractFilePath(string toolName, string argsJson)
    {
        try
        {
            if (toolName is "read_file" or "Write" or "edit_file")
            {
                var d = ArgumentParser.ParseElementMap(argsJson);
                if (d.TryGetValue("file_path", out var fp))
                    return fp.GetString();
            }
        }
        catch { /* ignore parse errors */ }
        return null;
    }

    // ── @ file injection ──────────────────────────────────────────────────────

    /// <summary>
    /// Injects file or directory content into a prompt string.
    /// Wraps file content in a fenced code block.
    /// Caps directory injection at 20 files.
    /// Returns <c>null</c> for missing paths.
    /// </summary>
    internal static string? InjectAtPath(string atPath, string rest)
    {
        atPath = atPath.Replace("\\ ", " ");

        if (File.Exists(atPath))
        {
            var content = File.ReadAllText(atPath);
            return string.IsNullOrEmpty(rest)
                ? $"File: {atPath}\n\n```\n{content}\n```"
                : $"{rest}\n\nFile: {atPath}\n\n```\n{content}\n```";
        }

        if (Directory.Exists(atPath))
        {
            var files = Directory.GetFiles(atPath, "*.*", SearchOption.AllDirectories)
                .Take(20).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"Directory: {atPath}");
            foreach (var f in files)
            {
                sb.AppendLine($"\n--- {Path.GetRelativePath(atPath, f)} ---");
                try { sb.AppendLine(File.ReadAllText(f)); }
                catch { sb.AppendLine("[unreadable]"); }
            }
            if (!string.IsNullOrEmpty(rest)) sb.Insert(0, rest + "\n\n");
            return sb.ToString();
        }

        UI.Error($"Path not found: {atPath}");
        return null;
    }

    // ── Flag validation ───────────────────────────────────────────────────────

    /// <summary>
    /// Validates that no unknown CLI flags are present.
    /// Throws <see cref="ArgumentException"/> instead of calling
    /// <see cref="Environment.Exit"/> so this method is unit-testable.
    /// </summary>
    internal static void ValidateUnknownFlags(string[] args, string? inlinePrompt, string? modelOverride)
    {
        if (args.Length == 0) return;
        if (inlinePrompt is not null || modelOverride is not null) return;
        if (args.Contains("--config") || args.Contains("--version") || args.Contains("-v")
            || args.Contains("--help") || args.Contains("-h")) return;

        var knownFlags = new HashSet<string>
            { "-p", "--model", "--theme", "--output-format", "--config",
              "--version", "-v", "--help", "-h" };

        foreach (var a in args)
        {
            if (a.StartsWith('-') && !knownFlags.Contains(a))
                throw new ArgumentException($"Unknown flag: {a} 🤔\nRun bse-code --help for usage.");
        }
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    internal static void PrintBanner(string model, string provider)
    {
        Console.WriteLine();
        Console.ForegroundColor = UI.Accent;
        Console.WriteLine("  ╭──────────────────────────────────────────╮");
        Console.WriteLine("  │   ██████╗ ███████╗███████╗                │");
        Console.WriteLine("  │   ██╔══██╗██╔════╝██╔════╝                │");
        Console.WriteLine("  │   ██████╔╝███████╗█████╗   ─ code         │");
        Console.WriteLine("  │   ██╔══██╗╚════██║██╔══╝                  │");
        Console.WriteLine("  │   ██████╔╝███████║███████╗                │");
        Console.WriteLine("  │   ╚═════╝ ╚══════╝╚══════╝                │");
        Console.WriteLine("  ╰──────────────────────────────────────────╯");
        Console.ResetColor();
        UI.Print($"  provider: {provider}", UI.Muted);
        UI.Print($"  model  : {model}", UI.Muted);
        UI.Print($"  theme  : {ThemeManager.Current.Name}", UI.Muted);
        UI.Print($"  cwd    : {Directory.GetCurrentDirectory()}", UI.Muted);
        if (SkillManager.All.Count > 0)
            UI.Print($"  🧠 skills : {SkillManager.All.Count} loaded", UI.SkillColor);
        if (McpManager.Tools.Count > 0)
            UI.Print($"  🔌 mcp    : {McpManager.Tools.Count} tools from {McpManager.Servers.Count} server(s)", UI.McpColor);
        if (MemoryManager.Files.Count > 0)
            UI.Print($"  💾 memory : {MemoryManager.Files.Count} BSE.md file(s) loaded", UI.Muted);
        Console.ForegroundColor = UI.Muted;
        Console.WriteLine("  type /help for commands · /exit to quit 🚀");
        Console.ResetColor();
    }

    internal void PrintStats(List<ChatMessage> messages, DateTime sessionStart,
                             int sessionTurns, int sessionToolCalls)
    {
        var elapsed = DateTime.UtcNow - sessionStart;
        var msgCount = messages.Count(m => m is UserChatMessage or AssistantChatMessage);
        Console.WriteLine();
        UI.Header("Session stats 📊");
        UI.Print($"    ⏱  duration   : {elapsed:hh\\:mm\\:ss}", UI.Muted);
        UI.Print($"    💬 turns      : {sessionTurns}", UI.Muted);
        UI.Print($"    🔧 tool calls : {sessionToolCalls}", UI.Muted);
        UI.Print($"    📨 messages   : {msgCount}", UI.Muted);
        UI.Print($"    🤖 model      : {_config.Model}", UI.Muted);
        UI.Print($"    🌐 provider   : {_config.Provider}", UI.Muted);
        UI.Print($"    🎨 theme      : {ThemeManager.Current.Name}", UI.Muted);
        UI.Print($"    🧠 skills     : {SkillManager.All.Count}", UI.Muted);
        UI.Print($"    🔌 mcp tools  : {McpManager.Tools.Count}", UI.Muted);
    }

    internal static void PrintToolCall(string name, string argsJson)
    {
        Console.WriteLine();
        var isMcp = name.StartsWith("mcp__");
        Console.ForegroundColor = isMcp ? UI.McpColor : UI.ToolColor;
        Console.Write($"  ⚙  {name}");
        Console.ResetColor();

        try
        {
            var d = ArgumentParser.ParseElementMap(argsJson);
            string summary = name switch
            {
                "read_file" => d.GetValueOrDefault("file_path").GetString() ?? "",
                "Write" => d.GetValueOrDefault("file_path").GetString() ?? "",
                "Bash" => Truncate(d.GetValueOrDefault("command").GetString() ?? "", 60),
                "list_dir" => d.GetValueOrDefault("path").GetString() ?? "",
                "glob" => d.GetValueOrDefault("pattern").GetString() ?? "",
                "grep" => d.GetValueOrDefault("pattern").GetString() ?? "",
                _ => ""
            };
            if (!string.IsNullOrEmpty(summary))
            {
                Console.ForegroundColor = UI.Muted;
                Console.Write($"  {summary}");
                Console.ResetColor();
            }
        }
        catch { /* ignore display errors */ }

        Console.Write("  ");
    }

    internal static void PrintToolResult(string name, string result, bool success)
    {
        if (success)
        {
            Console.ForegroundColor = UI.SuccessColor;
            Console.WriteLine("✓");
        }
        else
        {
            Console.ForegroundColor = UI.ErrColor;
            Console.WriteLine("✗");
            Console.ForegroundColor = UI.ErrColor;
            Console.WriteLine($"    {Truncate(result, 120)}");
        }
        Console.ResetColor();
    }

    internal static string? GetGitBranch()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --abbrev-ref HEAD",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(startInfo)!;
            var branch = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(1000);
            return p.ExitCode == 0 && !string.IsNullOrEmpty(branch) ? branch : null;
        }
        catch { return null; }
    }

    internal static ChatCompletionOptions BuildDefaultOptions(ToolRegistry toolRegistry)
    {
        var opts = new ChatCompletionOptions();
        foreach (var t in toolRegistry.ToChatTools().Concat(McpManager.ToChatTools())) opts.Tools.Add(t);
        return opts;
    }

    internal static string BuildDefaultSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.Append("""
            You are BSE-Code, an AI coding assistant running in the user's terminal.
            You have access to tools: read_file, Write, Bash, and any MCP tools listed below.

            Guidelines:
            - Be concise and direct in your responses.
            - When asked to modify code, read the relevant files first to understand context.
            - Confirm destructive operations (deleting files, overwriting) before executing.
            - When running shell commands, prefer safe, read-only commands unless explicitly asked otherwise.
            - Show relevant file paths and code snippets in your explanations.
            - If a task requires multiple steps, explain your plan briefly before starting.
            """);
        sb.Append(MemoryManager.BuildSystemContext());
        sb.Append(SkillManager.BuildSystemContext());
        return sb.ToString();
    }

    internal static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    internal static void PrintHelp()
    {
        Console.WriteLine();
        UI.Print("  🚀 BSE-Code — AI coding assistant (OpenRouter, OpenAI, Ollama, LM Studio & more)", UI.Accent);
        Console.WriteLine();
        UI.Print("  Usage:", ConsoleColor.White);
        UI.Print("    bse-code                          Interactive REPL mode", UI.Muted);
        UI.Print("    bse-code -p \"<prompt>\"             One-shot prompt", UI.Muted);
        UI.Print("    bse-code --model <id>              Override model for this session", UI.Muted);
        UI.Print("    bse-code --theme <name>            Set color theme for this session", UI.Muted);
        UI.Print("    bse-code --output-format json|text Output format for one-shot mode", UI.Muted);
        UI.Print("    bse-code --config                  Re-run the setup wizard", UI.Muted);
        UI.Print("    bse-code --version, -v             Show version", UI.Muted);
        UI.Print("    bse-code --help, -h                Show this help", UI.Muted);
        Console.WriteLine();
        UI.Print("  REPL slash commands:", ConsoleColor.White);
        SlashCommandHandler.PrintSlashHelp();
        Console.WriteLine();
        UI.Print("  Special prefixes:", ConsoleColor.White);
        UI.Print("    @<path>  📂 inject file/directory content into prompt", UI.Muted);
        UI.Print("    !<cmd>   🔧 run a shell command directly", UI.Muted);
        Console.WriteLine();
        UI.Print("  Environment variables:", ConsoleColor.White);
        UI.Print("    BSE_PROVIDER    Provider name (OpenRouter, OpenAI, Anthropic, Google,", UI.Muted);
        UI.Print("                    Ollama, LmStudio, LocalAiFoundry, Custom) 🌐", UI.Muted);
        UI.Print("    BSE_API_KEY     API key for the selected provider 🔑", UI.Muted);
        UI.Print("    BSE_MODEL       Model ID to use 🤖", UI.Muted);
        UI.Print("    BSE_BASE_URL    Override the API base URL 🌐", UI.Muted);
        UI.Print("    (Legacy: OPENROUTER_API_KEY, OPENROUTER_MODEL, OPENROUTER_BASE_URL)", UI.Muted);
        Console.WriteLine();
        UI.Print("  Config files:", ConsoleColor.White);
        UI.Print("    ~/.bse-code/config.json   Main config (provider, api_key, model, base_url)", UI.Muted);
        UI.Print("    ~/.bse-code/mcp.json      MCP server definitions", UI.Muted);
        UI.Print("    ~/.bse-code/skills/       User-level skills (*.md)", UI.Muted);
        UI.Print("    ~/.bse-code/BSE.md        Global memory", UI.Muted);
        UI.Print("    ./BSE.md                  Project memory", UI.Muted);
        UI.Print("    ./.bse-code/skills/       Project-level skills (*.md)", UI.Muted);
        Console.WriteLine();
    }
}
