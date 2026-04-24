using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

// ── Entry point ───────────────────────────────────────────────────────────────

bool reconfigure = args.Contains("--config");

var config = await ConfigManager.LoadOrSetupAsync(reconfigure);

if (args.Length < 2 || args[0] != "-p")
{
    Console.Error.WriteLine("Usage: bse-code -p \"<prompt>\"  |  bse-code --config");
    Environment.Exit(1);
}

var prompt = args[1];
if (string.IsNullOrWhiteSpace(prompt))
{
    Console.Error.WriteLine("Prompt must not be empty.");
    Environment.Exit(1);
}

// ── Chat client ───────────────────────────────────────────────────────────────

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

var messages = new List<ChatMessage> { new UserChatMessage(prompt) };

while (true)
{
    ChatCompletion response = client.CompleteChat(messages, chatOptions);

    if (response.FinishReason == ChatFinishReason.ToolCalls)
    {
        messages.Add(new AssistantChatMessage(response.ToolCalls));

        foreach (var toolCall in response.ToolCalls)
        {
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
            }
            catch (Exception ex)
            {
                toolResult = $"ERROR: {ex.Message}";
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

static string HandleReadFile(BinaryData arguments)
{
    var parsed   = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments)
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

    var process = new System.Diagnostics.Process
    {
        StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = isWin ? "cmd.exe" : "/bin/bash",
            Arguments              = isWin ? $"/c \"{command}\"" : $"-c \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        }
    };

    process.Start();
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    return string.IsNullOrEmpty(stderr) ? stdout : $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}";
}
