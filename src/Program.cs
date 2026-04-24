
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

// ── Session stats ─────────────────────────────────────────────────────────────
var sessionStart    = DateTime.UtcNow;
var sessionTurns    = 0;
var sessionToolCalls = 0;

// ── Entry point ───────────────────────────────────────────────────────────────

if (args.Contains("--version") || args.Contains("-v"))
{
    var version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown";
    UI.Print($"bse-code {version}", UI.Muted);
    return;
}

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    return;
}

if (args.Contains("--config"))
{
    await ConfigManager.LoadOrSetupAsync(forceReconfigure: true);
    return;
}

// --model <id> flag: override model for this session
string? modelOverride = null;
var modelIdx = Array.IndexOf(args, "--model");
if (modelIdx >= 0 && modelIdx + 1 < args.Length)
    modelOverride = args[modelIdx + 1];

// --theme <name> flag
string? themeOverride = null;
var themeIdx = Array.IndexOf(args, "--theme");
if (themeIdx >= 0 && themeIdx + 1 < args.Length)
    themeOverride = args[themeIdx + 1];

// --output-format json|text (one-shot)
string outputFormat = "text";
var fmtIdx = Array.IndexOf(args, "--output-format");
if (fmtIdx >= 0 && fmtIdx + 1 < args.Length)
    outputFormat = args[fmtIdx + 1].ToLowerInvariant();

// Collect prompt if -p was passed
string? inlinePrompt = null;
var pIdx = Array.IndexOf(args, "-p");
if (pIdx >= 0 && pIdx + 1 < args.Length)
    inlinePrompt = args[pIdx + 1];

// Validate unknown flags
if (args.Length > 0 && inlinePrompt is null && modelOverride is null
    && !args.Contains("--config") && !args.Contains("--version") && !args.Contains("-v")
    && !args.Contains("--help") && !args.Contains("-h"))
{
    var knownFlags = new HashSet<string>
        { "-p", "--model", "--theme", "--output-format", "--config",
          "--version", "-v", "--help", "-h" };
    foreach (var a in args)
    {
        if (a.StartsWith('-') && !knownFlags.Contains(a))
        {
            UI.Error($"Unknown flag: {a}");
            UI.Print("Run bse-code --help for usage.", UI.Muted);
            Environment.Exit(1);
        }
    }
}

var config = await ConfigManager.LoadOrSetupAsync();
if (modelOverride is not null) config.Model = modelOverride;

// Apply theme (flag > config > default)
var themeName = themeOverride ?? config.Theme ?? "default";
ThemeManager.TrySet(themeName);

// Load memory, skills, MCP
MemoryManager.EnsureUserMemory();
MemoryManager.Reload();
SkillManager.EnsureDirectories();
SkillManager.Reload();
McpManager.EnsureExampleConfig();
await McpManager.LoadAsync();

// ── Build system prompt ───────────────────────────────────────────────────────

string BuildSystemPrompt()
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

// ── Build chat tools ──────────────────────────────────────────────────────────

List<ChatTool> BuildTools()
{
    var tools = new List<ChatTool>
    {
        ChatTool.CreateFunctionTool(
            functionName: "read_file",
            functionDescription: "Read and return the contents of a file",
            functionParameters: BinaryData.FromObjectAsJson(new
            {
                type = "object",
                required = new[] { "file_path" },
                properties = new
                {
                    file_path = new { type = "string", description = "The path to the file to read" }
                }
            })
        ),
        ChatTool.CreateFunctionTool(
            functionName: "Write",
            functionDescription: "Write content to a file",
            functionParameters: BinaryData.FromObjectAsJson(new
            {
                type = "object",
                required = new[] { "file_path", "content" },
                properties = new
                {
                    file_path = new { type = "string", description = "The path of the file to write to" },
                    content   = new { type = "string", description = "The content to write to the file" }
                }
            })
        ),
        ChatTool.CreateFunctionTool(
            functionName: "Bash",
            functionDescription: "Execute a shell command",
            functionParameters: BinaryData.FromObjectAsJson(new
            {
                type = "object",
                required = new[] { "command" },
                properties = new
                {
                    command = new { type = "string", description = "The shell command to execute" }
                }
            })
        ),
        ChatTool.CreateFunctionTool(
            functionName: "list_dir",
            functionDescription: "List files and directories at a path",
            functionParameters: BinaryData.FromObjectAsJson(new
            {
                type = "object",
                required = new[] { "path" },
                properties = new
                {
                    path = new { type = "string", description = "Directory path to list" }
                }
            })
        ),
        ChatTool.CreateFunctionTool(
            functionName: "glob",
            functionDescription: "Find files matching a glob pattern",
            functionParameters: BinaryData.FromObjectAsJson(new
            {
                type = "object",
                required = new[] { "pattern" },
                properties = new
                {
                    pattern   = new { type = "string", description = "Glob pattern, e.g. src/**/*.cs" },
                    base_path = new { type = "string", description = "Base directory (default: cwd)" }
                }
            })
        ),
        ChatTool.CreateFunctionTool(
            functionName: "grep",
            functionDescription: "Search for a pattern in files",
            functionParameters: BinaryData.FromObjectAsJson(new
            {
                type = "object",
                required = new[] { "pattern" },
                properties = new
                {
                    pattern   = new { type = "string", description = "Regex or text pattern to search" },
                    path      = new { type = "string", description = "File or directory to search in (default: cwd)" },
                    recursive = new { type = "boolean", description = "Search recursively (default: true)" }
                }
            })
        ),
    };

    // Add MCP tools
    tools.AddRange(McpManager.ToChatTools());
    return tools;
}

// ── Build client ──────────────────────────────────────────────────────────────

ChatClient BuildClient() => new ChatClient(
    model: config.Model,
    credential: new ApiKeyCredential(config.ApiKey),
    options: new OpenAIClientOptions { Endpoint = new Uri(config.BaseUrl) }
);

var client = BuildClient();

// ── Decide: one-shot or interactive REPL ─────────────────────────────────────

if (inlinePrompt is not null)
{
    if (string.IsNullOrWhiteSpace(inlinePrompt))
    {
        UI.Error("Prompt must not be empty.");
        Environment.Exit(1);
    }

    var opts = new ChatCompletionOptions();
    foreach (var t in BuildTools()) opts.Tools.Add(t);

    var messages = new List<ChatMessage> { new SystemChatMessage(BuildSystemPrompt()) };

    if (outputFormat == "json")
    {
        var result = new StringBuilder();
        await RunTurnAsync(client, opts, messages, inlinePrompt, captureOutput: result);
        Console.WriteLine(JsonSerializer.Serialize(new { response = result.ToString() }));
    }
    else
    {
        await RunTurnAsync(client, opts, messages, inlinePrompt);
        Console.WriteLine();
    }
}
else
{
    await RunReplAsync();
}

// ── REPL ──────────────────────────────────────────────────────────────────────

async Task RunReplAsync()
{
    PrintBanner(config.Model);

    var messages = new List<ChatMessage> { new SystemChatMessage(BuildSystemPrompt()) };
    var opts     = new ChatCompletionOptions();
    foreach (var t in BuildTools()) opts.Tools.Add(t);

    while (true)
    {
        // Prompt line: cwd + git branch + input indicator
        var cwd    = Path.GetFileName(Directory.GetCurrentDirectory());
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

        var input = Console.ReadLine();

        if (input is null || input.Trim() is "/exit" or "/quit" or "exit" or "quit")
        {
            Console.WriteLine();
            UI.Print("  bye 👋", UI.Muted);
            Console.WriteLine();
            break;
        }

        if (string.IsNullOrWhiteSpace(input)) continue;

        // ! prefix: shell passthrough (like Gemini CLI)
        if (input.TrimStart().StartsWith('!'))
        {
            var shellCmd = input.TrimStart()[1..].Trim();
            if (string.IsNullOrWhiteSpace(shellCmd))
            {
                UI.Warn("Usage: !<command>  e.g. !git status");
                continue;
            }
            var result = HandleBash(BinaryData.FromObjectAsJson(new { command = shellCmd }));
            Console.ForegroundColor = UI.Muted;
            Console.WriteLine(result);
            Console.ResetColor();
            continue;
        }

        // @ prefix: inject file content into prompt (like Gemini CLI)
        if (input.TrimStart().StartsWith('@'))
        {
            var parts   = input.TrimStart().Split(' ', 2);
            var atPath  = parts[0][1..].Trim();
            var rest    = parts.Length > 1 ? parts[1] : "";
            var injected = InjectAtPath(atPath, rest);
            if (injected is null) continue;
            input = injected;
        }

        // Slash commands
        if (input.Trim().StartsWith('/'))
        {
            var result = await HandleSlashCommandAsync(input.Trim(), messages, opts);
            if (result == 1) break;
            continue;
        }

        sessionTurns++;
        await RunTurnAsync(client, opts, messages, input.Trim());
    }
}

// ── Single turn ───────────────────────────────────────────────────────────────

async Task RunTurnAsync(
    ChatClient chatClient,
    ChatCompletionOptions opts,
    List<ChatMessage> messages,
    string userInput,
    StringBuilder? captureOutput = null)
{
    messages.Add(new UserChatMessage(userInput));

    while (true)
    {
        using var spinner = new Spinner("  Thinking");
        var contentBuilder = new StringBuilder();
        var toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();

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
                        Console.ForegroundColor = UI.Response;
                        Console.Write(part.Text);
                        Console.ResetColor();
                        contentBuilder.Append(part.Text);
                        captureOutput?.Append(part.Text);
                    }
                }

                foreach (var tcDelta in update.ToolCallUpdates)
                {
                    if (!toolCallAccumulators.TryGetValue(tcDelta.Index, out var acc))
                    {
                        acc = new ToolCallAccumulator(tcDelta.ToolCallId ?? "", tcDelta.FunctionName ?? "")
                            { Index = tcDelta.Index };
                        toolCallAccumulators[tcDelta.Index] = acc;
                    }
                    if (!string.IsNullOrEmpty(tcDelta.FunctionName))  acc.Name = tcDelta.FunctionName;
                    if (!string.IsNullOrEmpty(tcDelta.ToolCallId))    acc.Id   = tcDelta.ToolCallId;
                    acc.Arguments.Append(tcDelta.FunctionArgumentsUpdate);
                }
            }
        }
        catch
        {
            spinner.Stop();
            throw;
        }

        if (contentBuilder.Length > 0) Console.WriteLine();

        if (toolCallAccumulators.Count == 0)
        {
            if (contentBuilder.Length > 0)
                messages.Add(new AssistantChatMessage(contentBuilder.ToString()));
            break;
        }

        var toolCalls = toolCallAccumulators.Values
            .OrderBy(a => a.Index)
            .Select(a => ChatToolCall.CreateFunctionToolCall(
                a.Id, a.Name, BinaryData.FromString(a.Arguments.ToString())))
            .ToList();

        messages.Add(new AssistantChatMessage(toolCalls));

        foreach (var (acc, toolCall) in toolCallAccumulators.Values
            .OrderBy(a => a.Index).Zip(toolCalls))
        {
            PrintToolCall(acc.Name, acc.Arguments.ToString());
            sessionToolCalls++;

            string toolResult;
            bool success = true;
            try
            {
                var argsData = BinaryData.FromString(acc.Arguments.ToString());
                toolResult = acc.Name switch
                {
                    "read_file" => HandleReadFile(argsData),
                    "Write"     => HandleWrite(argsData),
                    "Bash"      => HandleBash(argsData),
                    "list_dir"  => HandleListDir(argsData),
                    "glob"      => HandleGlob(argsData),
                    "grep"      => HandleGrep(argsData),
                    _ when acc.Name.StartsWith("mcp__") => await HandleMcpToolAsync(acc.Name, acc.Arguments.ToString()),
                    _           => $"Unknown tool: {acc.Name}"
                };
            }
            catch (Exception ex)
            {
                toolResult = $"ERROR: {ex.Message}";
                success = false;
            }

            PrintToolResult(acc.Name, toolResult, success);
            messages.Add(new ToolChatMessage(toolCall.Id, toolResult));
        }
    }
}

// ── Slash commands ────────────────────────────────────────────────────────────

async Task<int> HandleSlashCommandAsync(
    string cmd, List<ChatMessage> messages, ChatCompletionOptions opts)
{
    var parts = cmd.Split(' ', 2, StringSplitOptions.TrimEntries);
    var verb  = parts[0].ToLowerInvariant();
    var arg   = parts.Length > 1 ? parts[1] : "";

    switch (verb)
    {
        // ── Core ──────────────────────────────────────────────────────────────
        case "/exit":
        case "/quit":
            return 1; // exit

        case "/clear":
            messages.RemoveAll(m => m is not SystemChatMessage);
            // Rebuild system prompt in case memory/skills changed
            if (messages.Count > 0) messages[0] = new SystemChatMessage(BuildSystemPrompt());
            UI.Print("  conversation cleared", UI.Muted);
            break;

        case "/model":
            if (!string.IsNullOrEmpty(arg))
            {
                config.Model = arg;
                client = BuildClient();
                UI.Success($"model switched to: {arg}");
            }
            else
            {
                UI.Print($"  current model: {config.Model}", UI.Muted);
            }
            break;

        case "/help":
            PrintSlashHelp();
            break;

        // ── Theme ─────────────────────────────────────────────────────────────
        case "/theme":
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
                config.Theme = arg;
                ConfigManager.SaveTheme(config);
                UI.Success($"theme set to: {arg}");
            }
            else
            {
                UI.Error($"Unknown theme '{arg}'. Try: {string.Join(", ", ThemeManager.Names)}");
            }
            break;

        // ── Skills ────────────────────────────────────────────────────────────
        case "/skills":
            SkillManager.Reload();
            if (SkillManager.All.Count == 0)
            {
                UI.Print("  No skills found.", UI.Muted);
                UI.Print($"  Add .md files to ~/.bse-code/skills/ or .bse-code/skills/", UI.Muted);
            }
            else
            {
                UI.Header("Skills");
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
            break;

        // ── MCP ───────────────────────────────────────────────────────────────
        case "/mcp":
            var mcpSub = arg.Split(' ', 2)[0].ToLowerInvariant();
            switch (mcpSub)
            {
                case "list":
                case "ls":
                case "":
                    if (McpManager.Servers.Count == 0)
                    {
                        UI.Print("  No MCP servers configured.", UI.Muted);
                        UI.Print($"  Edit ~/.bse-code/mcp.json to add servers.", UI.Muted);
                    }
                    else
                    {
                        UI.Header("MCP Servers");
                        foreach (var (name, srv) in McpManager.Servers)
                        {
                            Console.ForegroundColor = UI.McpColor;
                            Console.Write($"    {name}");
                            Console.ForegroundColor = UI.Muted;
                            Console.WriteLine($"  {srv.Command} {string.Join(" ", srv.Args)}");
                            Console.ResetColor();
                        }
                        UI.Header("MCP Tools");
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
                        // Rebuild tools in opts
                        opts.Tools.Clear();
                        foreach (var t in BuildTools()) opts.Tools.Add(t);
                    }
                    UI.Success($"MCP reloaded — {McpManager.Tools.Count} tools available");
                    break;

                default:
                    UI.Print("  Usage: /mcp [list|reload]", UI.Muted);
                    break;
            }
            break;

        // ── Memory ────────────────────────────────────────────────────────────
        case "/memory":
            var memSub = arg.Split(' ', 2);
            switch (memSub[0].ToLowerInvariant())
            {
                case "show":
                case "":
                    MemoryManager.Reload();
                    if (MemoryManager.Files.Count == 0)
                    {
                        UI.Print("  No BSE.md files found.", UI.Muted);
                        UI.Print("  Create ./BSE.md or ~/.bse-code/BSE.md", UI.Muted);
                    }
                    else
                    {
                        UI.Header("Memory files");
                        foreach (var f in MemoryManager.Files)
                            UI.Print($"    {f.Label}", UI.Muted);
                    }
                    break;

                case "add":
                    var note = memSub.Length > 1 ? memSub[1] : "";
                    if (string.IsNullOrWhiteSpace(note))
                    {
                        UI.Error("Usage: /memory add <text>");
                    }
                    else
                    {
                        MemoryManager.AddNote(note);
                        // Refresh system prompt
                        if (messages.Count > 0)
                            messages[0] = new SystemChatMessage(BuildSystemPrompt());
                        UI.Success("Note added to BSE.md");
                    }
                    break;

                case "refresh":
                    MemoryManager.Reload();
                    if (messages.Count > 0)
                        messages[0] = new SystemChatMessage(BuildSystemPrompt());
                    UI.Success("Memory refreshed");
                    break;

                default:
                    UI.Print("  Usage: /memory [show|add <text>|refresh]", UI.Muted);
                    break;
            }
            break;

        // ── Session save/resume ───────────────────────────────────────────────
        case "/save":
            if (string.IsNullOrWhiteSpace(arg))
            {
                UI.Error("Usage: /save <tag>");
            }
            else
            {
                SessionManager.Save(arg, config.Model, messages);
                UI.Success($"Session saved as '{arg}'");
            }
            break;

        case "/resume":
        case "/load":
            if (string.IsNullOrWhiteSpace(arg))
            {
                var sessions = SessionManager.List();
                if (sessions.Count == 0)
                {
                    UI.Print("  No saved sessions.", UI.Muted);
                }
                else
                {
                    UI.Header("Saved sessions");
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
            }
            else
            {
                var loaded = SessionManager.Resume(arg, out var meta);
                if (loaded is null)
                {
                    UI.Error($"Session '{arg}' not found.");
                }
                else
                {
                    messages.Clear();
                    messages.Add(new SystemChatMessage(BuildSystemPrompt()));
                    messages.AddRange(loaded);
                    UI.Success($"Resumed session '{arg}' ({loaded.Count} messages)");
                    if (meta?.Model is not null && meta.Model != config.Model)
                        UI.Warn($"Session was saved with model '{meta.Model}', current: '{config.Model}'");
                }
            }
            break;

        // ── Compact ───────────────────────────────────────────────────────────
        case "/compact":
            var userCount = messages.Count(m => m is UserChatMessage);
            if (userCount < 3)
            {
                UI.Print("  Not enough history to compact.", UI.Muted);
            }
            else
            {
                var compactPrompt = string.IsNullOrEmpty(arg)
                    ? "Summarize our conversation so far into a concise context summary. Keep key decisions, code changes, and important context."
                    : arg;
                await RunTurnAsync(client, opts, messages, compactPrompt);
                // Keep only system + last assistant summary
                var summary = messages.LastOrDefault(m => m is AssistantChatMessage);
                messages.RemoveAll(m => m is not SystemChatMessage);
                if (summary is not null) messages.Add(summary);
                UI.Success("Conversation compacted");
            }
            break;

        // ── Stats ─────────────────────────────────────────────────────────────
        case "/stats":
            var elapsed = DateTime.UtcNow - sessionStart;
            var msgCount = messages.Count(m => m is UserChatMessage or AssistantChatMessage);
            Console.WriteLine();
            UI.Header("Session stats");
            UI.Print($"    duration   : {elapsed:hh\\:mm\\:ss}", UI.Muted);
            UI.Print($"    turns      : {sessionTurns}", UI.Muted);
            UI.Print($"    tool calls : {sessionToolCalls}", UI.Muted);
            UI.Print($"    messages   : {msgCount}", UI.Muted);
            UI.Print($"    model      : {config.Model}", UI.Muted);
            UI.Print($"    theme      : {ThemeManager.Current.Name}", UI.Muted);
            UI.Print($"    skills     : {SkillManager.All.Count}", UI.Muted);
            UI.Print($"    mcp tools  : {McpManager.Tools.Count}", UI.Muted);
            break;

        // ── Tools ─────────────────────────────────────────────────────────────
        case "/tools":
            Console.WriteLine();
            UI.Header("Built-in tools");
            UI.Print("    read_file  — read file contents", UI.Muted);
            UI.Print("    Write      — write/create a file", UI.Muted);
            UI.Print("    Bash       — execute a shell command", UI.Muted);
            UI.Print("    list_dir   — list directory contents", UI.Muted);
            UI.Print("    glob       — find files by pattern", UI.Muted);
            UI.Print("    grep       — search text in files", UI.Muted);
            if (McpManager.Tools.Count > 0)
            {
                UI.Header("MCP tools");
                foreach (var t in McpManager.Tools)
                {
                    Console.ForegroundColor = UI.McpColor;
                    Console.Write($"    {t.FullName}");
                    Console.ForegroundColor = UI.Muted;
                    Console.WriteLine($"  — {t.Description}");
                    Console.ResetColor();
                }
            }
            break;

        // ── Init (create BSE.md) ──────────────────────────────────────────────
        case "/init":
            var bseMdPath = Path.Combine(Directory.GetCurrentDirectory(), "BSE.md");
            if (File.Exists(bseMdPath) && string.IsNullOrEmpty(arg))
            {
                UI.Warn("BSE.md already exists. Use /init --force to overwrite.");
            }
            else
            {
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
                if (messages.Count > 0)
                    messages[0] = new SystemChatMessage(BuildSystemPrompt());
                UI.Success($"Created BSE.md in {Directory.GetCurrentDirectory()}");
            }
            break;

        // ── Dynamic skill invocation ──────────────────────────────────────────
        default:
            var skillName = verb.TrimStart('/');
            var skill     = SkillManager.Find(skillName);
            if (skill is not null)
            {
                Console.ForegroundColor = UI.SkillColor;
                Console.WriteLine($"  ◆ skill: {skill.Name}");
                Console.ResetColor();
                var skillPrompt = string.IsNullOrEmpty(arg)
                    ? $"Execute the '{skill.Name}' skill:\n\n{skill.Content}"
                    : $"Execute the '{skill.Name}' skill with argument: {arg}\n\n{skill.Content}";
                sessionTurns++;
                await RunTurnAsync(client, opts, messages, skillPrompt);
            }
            else
            {
                UI.Print($"  unknown command: {verb}  (try /help)", UI.Muted);
            }
            break;
    }

    return 0; // continue
}

// ── Tool handlers ─────────────────────────────────────────────────────────────

static string HandleReadFile(BinaryData arguments)
{
    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments)
                 ?? throw new Exception("Invalid arguments");
    return File.ReadAllText(parsed["file_path"]);
}

static string HandleWrite(BinaryData arguments)
{
    var parsed   = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments)
                   ?? throw new Exception("Invalid arguments");
    var filePath = parsed["file_path"];
    var content  = parsed["content"];
    var dir      = Path.GetDirectoryName(filePath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    File.WriteAllText(filePath, content);
    return "File written successfully.";
}

static string HandleBash(BinaryData arguments)
{
    var parsed  = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments)
                  ?? throw new Exception("Invalid arguments");
    return RunShell(parsed["command"]);
}

static string RunShell(string command)
{
    bool isWin = OperatingSystem.IsWindows();
    var startInfo = new ProcessStartInfo
    {
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        UseShellExecute        = false,
        CreateNoWindow         = true
    };
    if (isWin) { startInfo.FileName = "cmd.exe"; startInfo.Arguments = "/c " + command; }
    else       { startInfo.FileName = "/bin/bash"; startInfo.ArgumentList.Add("-c"); startInfo.ArgumentList.Add(command); }

    using var process = new Process { StartInfo = startInfo };
    process.Start();
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    return string.IsNullOrEmpty(stderr) ? stdout : $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}";
}

static string HandleListDir(BinaryData arguments)
{
    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments)
                 ?? throw new Exception("Invalid arguments");
    var path   = parsed.GetValueOrDefault("path", ".");
    if (!Directory.Exists(path)) return $"Directory not found: {path}";

    var sb = new StringBuilder();
    foreach (var entry in Directory.GetFileSystemEntries(path).OrderBy(e => e))
    {
        var name = Path.GetFileName(entry);
        var isDir = Directory.Exists(entry);
        sb.AppendLine(isDir ? $"[DIR]  {name}/" : $"       {name}");
    }
    return sb.ToString();
}

static string HandleGlob(BinaryData arguments)
{
    var parsed  = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments)
                  ?? throw new Exception("Invalid arguments");
    var pattern = parsed["pattern"];
    var basePath = parsed.GetValueOrDefault("base_path", Directory.GetCurrentDirectory());

    var files = Directory.GetFiles(basePath, pattern, SearchOption.AllDirectories);
    return files.Length == 0
        ? "No files matched."
        : string.Join("\n", files.Select(f => Path.GetRelativePath(basePath, f)));
}

static string HandleGrep(BinaryData arguments)
{
    var parsed    = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments)
                    ?? throw new Exception("Invalid arguments");
    var pattern   = parsed["pattern"];
    var searchPath = parsed.GetValueOrDefault("path", Directory.GetCurrentDirectory());
    var recursive = !parsed.TryGetValue("recursive", out var r) || r != "false";

    var sb = new StringBuilder();
    var files = File.Exists(searchPath)
        ? [searchPath]
        : Directory.GetFiles(searchPath, "*.*",
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

    int matchCount = 0;
    foreach (var file in files)
    {
        try
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"{Path.GetRelativePath(Directory.GetCurrentDirectory(), file)}:{i + 1}: {lines[i].Trim()}");
                    matchCount++;
                    if (matchCount >= 200) { sb.AppendLine("... (truncated at 200 matches)"); return sb.ToString(); }
                }
            }
        }
        catch { /* skip unreadable files */ }
    }

    return matchCount == 0 ? "No matches found." : sb.ToString();
}

static async Task<string> HandleMcpToolAsync(string fullName, string argsJson)
{
    // fullName = mcp__serverName__toolName
    var parts = fullName.Split("__", 3);
    if (parts.Length < 3) return $"Invalid MCP tool name: {fullName}";
    return await McpManager.CallToolAsync(parts[1], parts[2], argsJson);
}

// ── @ file injection ──────────────────────────────────────────────────────────

static string? InjectAtPath(string atPath, string rest)
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
            try { sb.AppendLine(File.ReadAllText(f)); } catch { sb.AppendLine("[unreadable]"); }
        }
        if (!string.IsNullOrEmpty(rest)) sb.Insert(0, rest + "\n\n");
        return sb.ToString();
    }
    UI.Error($"Path not found: {atPath}");
    return null;
}

// ── Git helpers ───────────────────────────────────────────────────────────────

static string? GetGitBranch()
{
    try
    {
        var startInfo = new ProcessStartInfo
        {
            FileName               = "git",
            Arguments              = "rev-parse --abbrev-ref HEAD",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };
        using var p = Process.Start(startInfo)!;
        var branch = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit(1000);
        return p.ExitCode == 0 && !string.IsNullOrEmpty(branch) ? branch : null;
    }
    catch { return null; }
}

// ── UI helpers ────────────────────────────────────────────────────────────────

static void PrintBanner(string model)
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
    UI.Print($"  model  : {model}", UI.Muted);
    UI.Print($"  theme  : {ThemeManager.Current.Name}", UI.Muted);
    UI.Print($"  cwd    : {Directory.GetCurrentDirectory()}", UI.Muted);
    if (SkillManager.All.Count > 0)
        UI.Print($"  skills : {SkillManager.All.Count} loaded", UI.SkillColor);
    if (McpManager.Tools.Count > 0)
        UI.Print($"  mcp    : {McpManager.Tools.Count} tools from {McpManager.Servers.Count} server(s)", UI.McpColor);
    if (MemoryManager.Files.Count > 0)
        UI.Print($"  memory : {MemoryManager.Files.Count} BSE.md file(s) loaded", UI.Muted);
    Console.ForegroundColor = UI.Muted;
    Console.WriteLine("  type /help for commands · /exit to quit");
    Console.ResetColor();
}

static void PrintToolCall(string name, string argsJson)
{
    Console.WriteLine();
    var isMcp = name.StartsWith("mcp__");
    Console.ForegroundColor = isMcp ? UI.McpColor : UI.ToolColor;
    Console.Write($"  ⚙  {name}");
    Console.ResetColor();

    try
    {
        var d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson);
        if (d is not null)
        {
            string summary = name switch
            {
                "read_file" => d.GetValueOrDefault("file_path").GetString() ?? "",
                "Write"     => d.GetValueOrDefault("file_path").GetString() ?? "",
                "Bash"      => Truncate(d.GetValueOrDefault("command").GetString() ?? "", 60),
                "list_dir"  => d.GetValueOrDefault("path").GetString() ?? "",
                "glob"      => d.GetValueOrDefault("pattern").GetString() ?? "",
                "grep"      => d.GetValueOrDefault("pattern").GetString() ?? "",
                _           => ""
            };
            if (!string.IsNullOrEmpty(summary))
            {
                Console.ForegroundColor = UI.Muted;
                Console.Write($"  {summary}");
                Console.ResetColor();
            }
        }
    }
    catch { /* ignore */ }

    Console.Write("  ");
}

static void PrintToolResult(string name, string result, bool success)
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

static string Truncate(string s, int max) =>
    s.Length <= max ? s : s[..max] + "…";

static void PrintHelp()
{
    Console.WriteLine();
    UI.Print("  BSE-Code — AI coding assistant powered by OpenRouter", UI.Accent);
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
    PrintSlashHelp();
    Console.WriteLine();
    UI.Print("  Special prefixes:", ConsoleColor.White);
    UI.Print("    @<path>  inject file/directory content into prompt", UI.Muted);
    UI.Print("    !<cmd>   run a shell command directly", UI.Muted);
    Console.WriteLine();
    UI.Print("  Environment variables:", ConsoleColor.White);
    UI.Print("    OPENROUTER_API_KEY   Your OpenRouter API key", UI.Muted);
    UI.Print("    OPENROUTER_MODEL     Model ID to use", UI.Muted);
    UI.Print("    OPENROUTER_BASE_URL  Override the API base URL", UI.Muted);
    Console.WriteLine();
    UI.Print("  Config files:", ConsoleColor.White);
    UI.Print("    ~/.bse-code/config.json   Main config", UI.Muted);
    UI.Print("    ~/.bse-code/mcp.json      MCP server definitions", UI.Muted);
    UI.Print("    ~/.bse-code/skills/       User-level skills (*.md)", UI.Muted);
    UI.Print("    ~/.bse-code/BSE.md        Global memory", UI.Muted);
    UI.Print("    ./BSE.md                  Project memory", UI.Muted);
    UI.Print("    ./.bse-code/skills/       Project-level skills (*.md)", UI.Muted);
    Console.WriteLine();
}

static void PrintSlashHelp()
{
    Console.WriteLine();
    UI.Print("  Core:", UI.Accent);
    UI.Print("    /clear              clear conversation history", UI.Muted);
    UI.Print("    /model [id]         show or switch model", UI.Muted);
    UI.Print("    /compact [hint]     summarize history to save tokens", UI.Muted);
    UI.Print("    /stats              show session statistics", UI.Muted);
    UI.Print("    /tools              list available tools", UI.Muted);
    UI.Print("    /help               show this help", UI.Muted);
    UI.Print("    /exit               quit", UI.Muted);
    Console.WriteLine();
    UI.Print("  Appearance:", UI.Accent);
    UI.Print("    /theme [name]       list or set color theme", UI.Muted);
    Console.WriteLine();
    UI.Print("  Skills:", UI.SkillColor);
    UI.Print("    /skills             list loaded skills", UI.Muted);
    UI.Print("    /<skill-name> [arg] invoke a skill", UI.Muted);
    Console.WriteLine();
    UI.Print("  MCP:", UI.McpColor);
    UI.Print("    /mcp                list MCP servers and tools", UI.Muted);
    UI.Print("    /mcp reload         reload MCP servers", UI.Muted);
    Console.WriteLine();
    UI.Print("  Memory:", UI.Accent);
    UI.Print("    /memory             show loaded BSE.md files", UI.Muted);
    UI.Print("    /memory add <text>  append note to ./BSE.md", UI.Muted);
    UI.Print("    /memory refresh     reload BSE.md files", UI.Muted);
    UI.Print("    /init               create BSE.md in current directory", UI.Muted);
    Console.WriteLine();
    UI.Print("  Sessions:", UI.Accent);
    UI.Print("    /save <tag>         save conversation", UI.Muted);
    UI.Print("    /resume [tag]       list or resume a saved session", UI.Muted);
    Console.WriteLine();
}

// ── Tool call accumulator ─────────────────────────────────────────────────────

class ToolCallAccumulator(string id, string name)
{
    public string Id    { get; set; } = id;
    public string Name  { get; set; } = name;
    public int    Index { get; set; }
    public StringBuilder Arguments { get; } = new();
}
