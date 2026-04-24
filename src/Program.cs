using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Reflection;
using System.Text.Json;

// ── Entry point ───────────────────────────────────────────────────────────────

if (args.Contains("--version") || args.Contains("-v"))
{
    var version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown";
    Console.WriteLine($"bse-code {version}");
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

if (args.Length < 2 || args[0] != "-p")
{
    Console.Error.WriteLine("Usage: bse-code -p \"<prompt>\"  |  bse-code --config  |  bse-code --help");
    Environment.Exit(1);
}

var prompt = args[1];
if (string.IsNullOrWhiteSpace(prompt))
{
    Console.Error.WriteLine("Prompt must not be empty.");
    Environment.Exit(1);
}

var config = await ConfigManager.LoadOrSetupAsync();

// ── Chat client ───────────────────────────────────────────────────────────────

var client = new ChatClient(
    model: config.Model,
    credential: new ApiKeyCredential(config.ApiKey),
    options: new OpenAIClientOptions { Endpoint = new Uri(config.BaseUrl) }
);

// Define the three tools the AI can invoke: read_file, Write, and Bash.
// Each tool is described with a JSON schema so the model knows the parameters.
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

var messages = new List<ChatMessage>
{
    new SystemChatMessage(SystemPrompt),
    new UserChatMessage(prompt)
};

// Main conversation loop: send messages to the model, process any tool calls,
// and repeat until the model produces a final text response.
while (true)
{
    ChatCompletion response = await client.CompleteChatAsync(messages, chatOptions);

    if (response.FinishReason == ChatFinishReason.ToolCalls)
    {
        messages.Add(new AssistantChatMessage(response.ToolCalls));

        foreach (var toolCall in response.ToolCalls)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write($"  [{toolCall.FunctionName}] ");
            Console.ResetColor();

            string toolResult;
            try
            {
                toolResult = toolCall.FunctionName switch
                {
                    "read_file" => HandleReadFile(toolCall.FunctionArguments),
                    "Write"     => HandleWrite(toolCall.FunctionArguments),
                    "Bash"      => HandleBash(toolCall.FunctionArguments),
                    _           => $"Unknown tool: {toolCall.FunctionName}"
                };

                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine("done");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                toolResult = $"ERROR: {ex.Message}";
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"error: {ex.Message}");
                Console.ResetColor();
            }

            messages.Add(new ToolChatMessage(toolCall.Id, toolResult));
        }
    }
    else
    {
        if (response.Content is { Count: > 0 })
            Console.Write(response.Content[0].Text);
        break;
    }
}

// ── Tool handlers ─────────────────────────────────────────────────────────────

/// <summary>
/// Reads and returns the entire contents of the file at the specified path.
/// </summary>
static string HandleReadFile(BinaryData arguments)
{
    var parsed   = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments)
                   ?? throw new Exception("Invalid arguments");
    return File.ReadAllText(parsed["file_path"]);
}

/// <summary>
/// Writes the provided content to a file, creating parent directories if needed.
/// </summary>
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

/// <summary>
/// Executes a shell command via cmd.exe (Windows) or /bin/bash (Unix)
/// and returns combined stdout/stderr output.
/// </summary>
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

    if (isWin)
    {
        startInfo.FileName  = "cmd.exe";
        startInfo.Arguments = "/c " + command;
    }
    else
    {
        startInfo.FileName = "/bin/bash";
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);
    }

    using var process = new System.Diagnostics.Process { StartInfo = startInfo };
    process.Start();
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    return string.IsNullOrEmpty(stderr) ? stdout : $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}";
}

/// <summary>Prints CLI usage information and available flags.</summary>
static void PrintHelp()
{
    Console.WriteLine("""
        BSE-Code - AI coding assistant CLI powered by OpenRouter

        Usage:
          bse-code -p "<prompt>"    Run a prompt
          bse-code --config         Re-run the setup wizard
          bse-code --version, -v    Show version
          bse-code --help, -h       Show this help

        Environment variables (override config file):
          OPENROUTER_API_KEY        Your OpenRouter API key
          OPENROUTER_MODEL          Model ID to use
          OPENROUTER_BASE_URL       Override the API base URL

        Config file: ~/.bse-code/config.json
        """);
}
