using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Reflection;
using System.Text;
using System.Text.Json;

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

// Collect prompt if -p was passed
string? inlinePrompt = null;
var pIdx = Array.IndexOf(args, "-p");
if (pIdx >= 0 && pIdx + 1 < args.Length)
    inlinePrompt = args[pIdx + 1];

// Validate: if args were given but nothing useful was parsed
if (args.Length > 0 && inlinePrompt is null && modelOverride is null
    && !args.Contains("--config") && !args.Contains("--version") && !args.Contains("-v")
    && !args.Contains("--help") && !args.Contains("-h"))
{
    // Check for unknown flags
    var knownFlags = new HashSet<string> { "-p", "--model", "--config", "--version", "-v", "--help", "-h" };
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

// ── Build shared chat infrastructure ─────────────────────────────────────────

var client = new ChatClient(
    model: config.Model,
    credential: new ApiKeyCredential(config.ApiKey),
    options: new OpenAIClientOptions { Endpoint = new Uri(config.BaseUrl) }
);

var chatOptions = new ChatCompletionOptions
{
    Tools =
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
        )
    }
};

const string SystemPrompt = """
    You are BSE-Code, an AI coding assistant running in the user's terminal.
    You have access to three tools: read_file (read file contents), Write (write to files),
    and Bash (execute shell commands).

    Guidelines:
    - Be concise and direct in your responses.
    - When asked to modify code, read the relevant files first to understand context.
    - Confirm destructive operations (deleting files, overwriting) before executing.
    - When running shell commands, prefer safe, read-only commands unless explicitly asked otherwise.
    - Show relevant file paths and code snippets in your explanations.
    - If a task requires multiple steps, explain your plan briefly before starting.
    """;

// ── Decide: one-shot or interactive REPL ─────────────────────────────────────

if (inlinePrompt is not null)
{
    // One-shot mode: run prompt and exit
    if (string.IsNullOrWhiteSpace(inlinePrompt))
    {
        UI.Error("Prompt must not be empty.");
        Environment.Exit(1);
    }

    var messages = new List<ChatMessage> { new SystemChatMessage(SystemPrompt) };
    await RunTurnAsync(client, chatOptions, messages, inlinePrompt);
    Console.WriteLine();
}
else
{
    // Interactive REPL mode
    await RunReplAsync(client, chatOptions);
}

// ── REPL ──────────────────────────────────────────────────────────────────────

async Task RunReplAsync(ChatClient chatClient, ChatCompletionOptions opts)
{
    PrintBanner(config.Model);

    var messages = new List<ChatMessage> { new SystemChatMessage(SystemPrompt) };

    while (true)
    {
        // Prompt line: show cwd + input indicator
        var cwd = Path.GetFileName(Directory.GetCurrentDirectory());
        Console.WriteLine();
        UI.Print($" {cwd} ", UI.Accent, newline: false);
        Console.ForegroundColor = UI.Prompt;
        Console.Write(" ❯ ");
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

        // Slash commands
        if (input.Trim().StartsWith('/'))
        {
            HandleSlashCommand(input.Trim(), messages, config);
            continue;
        }

        await RunTurnAsync(chatClient, opts, messages, input.Trim());
    }
}

// ── Single turn (shared by one-shot and REPL) ─────────────────────────────────

async Task RunTurnAsync(
    ChatClient chatClient,
    ChatCompletionOptions opts,
    List<ChatMessage> messages,
    string userInput)
{
    messages.Add(new UserChatMessage(userInput));

    while (true)
    {
        // Spinner while waiting
        using var spinner = new Spinner("  Thinking");
        StreamingChatCompletionUpdate? lastUpdate = null;
        var contentBuilder = new StringBuilder();
        var toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();
        ChatFinishReason? finishReason = null;

        try
        {
            var stream = chatClient.CompleteChatStreamingAsync(messages, opts);

            spinner.Stop(); // stop before first token arrives
            Console.WriteLine();

            await foreach (var update in stream)
            {
                finishReason = update.FinishReason;

                // Stream text tokens
                foreach (var part in update.ContentUpdate)
                {
                    if (!string.IsNullOrEmpty(part.Text))
                    {
                        Console.ForegroundColor = UI.Response;
                        Console.Write(part.Text);
                        Console.ResetColor();
                        contentBuilder.Append(part.Text);
                    }
                }

                // Accumulate tool call deltas
                foreach (var tcDelta in update.ToolCallUpdates)
                {
                    if (!toolCallAccumulators.TryGetValue(tcDelta.Index, out var acc))
                    {
                        acc = new ToolCallAccumulator(tcDelta.ToolCallId ?? "", tcDelta.FunctionName ?? "");
                        toolCallAccumulators[tcDelta.Index] = acc;
                    }
                    if (!string.IsNullOrEmpty(tcDelta.FunctionName))
                        acc.Name = tcDelta.FunctionName;
                    if (!string.IsNullOrEmpty(tcDelta.ToolCallId))
                        acc.Id = tcDelta.ToolCallId;
                    acc.Arguments.Append(tcDelta.FunctionArgumentsUpdate);
                }

                lastUpdate = update;
            }
        }
        catch
        {
            spinner.Stop();
            throw;
        }

        // If we streamed text, add newline
        if (contentBuilder.Length > 0)
            Console.WriteLine();

        // No tool calls → done
        if (toolCallAccumulators.Count == 0)
        {
            if (contentBuilder.Length > 0)
                messages.Add(new AssistantChatMessage(contentBuilder.ToString()));
            break;
        }

        // Build tool call list for the assistant message
        var toolCalls = toolCallAccumulators.Values
            .OrderBy(a => a.Index)
            .Select(a => ChatToolCall.CreateFunctionToolCall(a.Id, a.Name, BinaryData.FromString(a.Arguments.ToString())))
            .ToList();

        messages.Add(new AssistantChatMessage(toolCalls));

        // Execute each tool call
        foreach (var (acc, toolCall) in toolCallAccumulators.Values.Zip(toolCalls))
        {
            PrintToolCall(acc.Name, acc.Arguments.ToString());

            string toolResult;
            bool success = true;
            try
            {
                toolResult = acc.Name switch
                {
                    "read_file" => HandleReadFile(BinaryData.FromString(acc.Arguments.ToString())),
                    "Write"     => HandleWrite(BinaryData.FromString(acc.Arguments.ToString())),
                    "Bash"      => HandleBash(BinaryData.FromString(acc.Arguments.ToString())),
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
        // Loop back to get the model's next response
    }
}

// ── Slash commands ────────────────────────────────────────────────────────────

void HandleSlashCommand(string cmd, List<ChatMessage> messages, AppConfig cfg)
{
    switch (cmd.ToLowerInvariant())
    {
        case "/clear":
            messages.RemoveAll(m => m is not SystemChatMessage);
            UI.Print("  conversation cleared", UI.Muted);
            break;

        case "/model":
            UI.Print($"  current model: {cfg.Model}", UI.Muted);
            break;

        case "/help":
            Console.WriteLine();
            UI.Print("  Slash commands:", UI.Accent);
            UI.Print("    /clear   — clear conversation history", UI.Muted);
            UI.Print("    /model   — show current model", UI.Muted);
            UI.Print("    /help    — show this help", UI.Muted);
            UI.Print("    /exit    — quit", UI.Muted);
            break;

        default:
            UI.Print($"  unknown command: {cmd}  (try /help)", UI.Muted);
            break;
    }
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
    var command = parsed["command"];
    bool isWin  = OperatingSystem.IsWindows();

    var startInfo = new System.Diagnostics.ProcessStartInfo
    {
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        UseShellExecute        = false,
        CreateNoWindow         = true
    };

    if (isWin) { startInfo.FileName = "cmd.exe"; startInfo.Arguments = "/c " + command; }
    else       { startInfo.FileName = "/bin/bash"; startInfo.ArgumentList.Add("-c"); startInfo.ArgumentList.Add(command); }

    using var process = new System.Diagnostics.Process { StartInfo = startInfo };
    process.Start();
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    return string.IsNullOrEmpty(stderr) ? stdout : $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}";
}

// ── UI helpers ────────────────────────────────────────────────────────────────

static void PrintBanner(string model)
{
    Console.WriteLine();
    Console.ForegroundColor = UI.Accent;
    Console.WriteLine("  ╭─────────────────────────────────────╮");
    Console.WriteLine("  │         BSE-Code  ·  AI Coding      │");
    Console.WriteLine("  ╰─────────────────────────────────────╯");
    Console.ResetColor();
    UI.Print($"  model  : {model}", UI.Muted);
    UI.Print($"  cwd    : {Directory.GetCurrentDirectory()}", UI.Muted);
    Console.ForegroundColor = UI.Muted;
    Console.WriteLine("  type /help for commands, /exit to quit");
    Console.ResetColor();
}

static void PrintToolCall(string name, string argsJson)
{
    Console.WriteLine();
    Console.ForegroundColor = UI.ToolColor;
    Console.Write($"  ⚙  {name}");
    Console.ResetColor();

    // Show a short human-readable summary of the args
    try
    {
        var d = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson);
        if (d is not null)
        {
            var summary = name switch
            {
                "read_file" => d.GetValueOrDefault("file_path", ""),
                "Write"     => d.GetValueOrDefault("file_path", ""),
                "Bash"      => Truncate(d.GetValueOrDefault("command", ""), 60),
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
    catch { /* ignore parse errors */ }

    Console.Write("  ");
}

static void PrintToolResult(string name, string result, bool success)
{
    if (success)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✓");
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("✗");
        Console.ForegroundColor = ConsoleColor.Red;
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
    UI.Print("    bse-code                      Interactive REPL mode", UI.Muted);
    UI.Print("    bse-code -p \"<prompt>\"         One-shot prompt", UI.Muted);
    UI.Print("    bse-code --model <id>          Override model for this session", UI.Muted);
    UI.Print("    bse-code --config              Re-run the setup wizard", UI.Muted);
    UI.Print("    bse-code --version, -v         Show version", UI.Muted);
    UI.Print("    bse-code --help, -h            Show this help", UI.Muted);
    Console.WriteLine();
    UI.Print("  REPL slash commands:", ConsoleColor.White);
    UI.Print("    /clear   clear conversation history", UI.Muted);
    UI.Print("    /model   show current model", UI.Muted);
    UI.Print("    /help    show commands", UI.Muted);
    UI.Print("    /exit    quit", UI.Muted);
    Console.WriteLine();
    UI.Print("  Environment variables:", ConsoleColor.White);
    UI.Print("    OPENROUTER_API_KEY   Your OpenRouter API key", UI.Muted);
    UI.Print("    OPENROUTER_MODEL     Model ID to use", UI.Muted);
    UI.Print("    OPENROUTER_BASE_URL  Override the API base URL", UI.Muted);
    Console.WriteLine();
    UI.Print($"  Config: ~/.bse-code/config.json", UI.Muted);
    Console.WriteLine();
}

// ── Accumulator for streaming tool calls ──────────────────────────────────────

class ToolCallAccumulator(string id, string name)
{
    public string Id   { get; set; } = id;
    public string Name { get; set; } = name;
    public int    Index { get; set; }
    public StringBuilder Arguments { get; } = new();
}
