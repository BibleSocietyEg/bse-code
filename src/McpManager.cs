using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenAI.Chat;

// ── MCP Config models ─────────────────────────────────────────────────────────

/// <summary>
/// Configuration for a single MCP server entry.
/// </summary>
public class McpServerConfig
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = [];

    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } = [];

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; } = false;
}

/// <summary>
/// Root of ~/.bse-code/mcp.json
/// </summary>
public class McpConfig
{
    [JsonPropertyName("mcpServers")]
    public Dictionary<string, McpServerConfig> McpServers { get; set; } = [];
}

// ── MCP Tool descriptor ───────────────────────────────────────────────────────

/// <summary>
/// Represents a tool exposed by an MCP server.
/// </summary>
public class McpTool
{
    public string ServerName { get; init; } = "";
    public string Name       { get; init; } = "";
    public string FullName   => $"mcp__{ServerName}__{Name}";
    public string Description { get; init; } = "";
    public JsonElement InputSchema { get; init; }
}

// ── Manager ───────────────────────────────────────────────────────────────────

/// <summary>
/// Manages MCP (Model Context Protocol) server connections.
/// Config file: ~/.bse-code/mcp.json
/// 
/// Supports stdio-based MCP servers (the most common type).
/// Tools from MCP servers are exposed as bse-code tools with the naming
/// convention: mcp__serverName__toolName
/// </summary>
public static class McpManager
{
    private static readonly string ConfigDir  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bse-code");
    private static readonly string McpFile = Path.Combine(ConfigDir, "mcp.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static McpConfig _config = new();
    private static readonly List<McpTool> _tools = [];
    private static readonly Dictionary<string, McpServerConfig> _activeServers = [];

    public static IReadOnlyList<McpTool> Tools => _tools;
    public static IReadOnlyDictionary<string, McpServerConfig> Servers => _activeServers;

    /// <summary>Loads MCP config and discovers tools from all enabled servers.</summary>
    public static async Task LoadAsync()
    {
        _tools.Clear();
        _activeServers.Clear();

        if (!File.Exists(McpFile)) return;

        try
        {
            var json = await File.ReadAllTextAsync(McpFile);
            _config = JsonSerializer.Deserialize<McpConfig>(json, JsonOpts) ?? new McpConfig();
        }
        catch (Exception ex)
        {
            UI.Warn($"😬 Failed to parse mcp.json: {ex.Message}");
            return;
        }

        foreach (var (name, server) in _config.McpServers)
        {
            if (server.Disabled) continue;
            _activeServers[name] = server;
            await DiscoverToolsAsync(name, server);
        }
    }

    /// <summary>
    /// Discovers tools from an MCP server by sending the initialize + tools/list requests.
    /// </summary>
    private static async Task DiscoverToolsAsync(string serverName, McpServerConfig server)
    {
        try
        {
            var tools = await SendMcpRequestAsync(serverName, server, "tools/list", null);
            if (tools is null) return;

            if (tools.Value.TryGetProperty("tools", out var toolsArr))
            {
                foreach (var tool in toolsArr.EnumerateArray())
                {
                    var toolName = tool.GetProperty("name").GetString() ?? "";
                    var desc     = tool.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                    var schema   = tool.TryGetProperty("inputSchema", out var s) ? s : default;

                    _tools.Add(new McpTool
                    {
                        ServerName  = serverName,
                        Name        = toolName,
                        Description = desc,
                        InputSchema = schema,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            UI.Warn($"🔌 MCP server '{serverName}': failed to discover tools — {ex.Message}");
        }
    }

    /// <summary>
    /// Executes an MCP tool call by spawning the server process and sending a JSON-RPC request.
    /// </summary>
    public static async Task<string> CallToolAsync(string serverName, string toolName, string argsJson)
    {
        if (!_activeServers.TryGetValue(serverName, out var server))
            return $"❌ ERROR: MCP server '{serverName}' not found or disabled.";

        try
        {
            var callParams = new
            {
                name      = toolName,
                arguments = JsonSerializer.Deserialize<JsonElement>(argsJson)
            };

            var result = await SendMcpRequestAsync(serverName, server, "tools/call", callParams);
            if (result is null) return "ERROR: No response from MCP server.";

            // Extract text content from result
            if (result.Value.TryGetProperty("content", out var content))
            {
                var sb = new StringBuilder();
                foreach (var item in content.EnumerateArray())
                {
                    if (item.TryGetProperty("text", out var text))
                        sb.AppendLine(text.GetString());
                }
                return sb.ToString().TrimEnd();
            }

            return result.Value.GetRawText();
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Sends a JSON-RPC 2.0 request to an MCP server via stdio.
    /// </summary>
    private static async Task<JsonElement?> SendMcpRequestAsync(
        string serverName, McpServerConfig server, string method, object? @params)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName               = server.Command,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        foreach (var arg in server.Args)
            startInfo.ArgumentList.Add(arg);

        foreach (var (k, v) in server.Env)
            startInfo.Environment[k] = v;

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Send initialize first
        var initRequest = new
        {
            jsonrpc = "2.0",
            id      = 1,
            method  = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities    = new { },
                clientInfo      = new { name = "bse-code", version = "1.3.0" }
            }
        };

        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(initRequest));
        await process.StandardInput.FlushAsync();

        // Read initialize response
        var initLine = await ReadLineWithTimeoutAsync(process.StandardOutput, 5000);
        if (initLine is null) { process.Kill(); return null; }

        // Send initialized notification
        var initializedNotif = new { jsonrpc = "2.0", method = "notifications/initialized" };
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(initializedNotif));
        await process.StandardInput.FlushAsync();

        // Send actual request
        var request = new
        {
            jsonrpc = "2.0",
            id      = 2,
            method,
            @params
        };

        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
        await process.StandardInput.FlushAsync();

        // Read response
        var responseLine = await ReadLineWithTimeoutAsync(process.StandardOutput, 10000);
        process.Kill();

        if (responseLine is null) return null;

        var doc = JsonDocument.Parse(responseLine);
        if (doc.RootElement.TryGetProperty("result", out var result))
            return result;

        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new Exception(error.GetRawText());

        return null;
    }

    private static async Task<string?> ReadLineWithTimeoutAsync(
        System.IO.StreamReader reader, int timeoutMs)
    {
        using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
        try
        {
            return await reader.ReadLineAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts discovered MCP tools into ChatTool definitions for the OpenAI SDK.
    /// </summary>
    public static IEnumerable<ChatTool> ToChatTools()
    {
        foreach (var tool in _tools)
        {
            BinaryData schema;
            try
            {
                schema = tool.InputSchema.ValueKind == JsonValueKind.Undefined
                    ? BinaryData.FromObjectAsJson(new { type = "object", properties = new { } })
                    : BinaryData.FromString(tool.InputSchema.GetRawText());
            }
            catch
            {
                schema = BinaryData.FromObjectAsJson(new { type = "object", properties = new { } });
            }

            yield return ChatTool.CreateFunctionTool(
                functionName:        tool.FullName,
                functionDescription: $"[MCP:{tool.ServerName}] {tool.Description}",
                functionParameters:  schema
            );
        }
    }

    /// <summary>Creates the example mcp.json if it doesn't exist.</summary>
    public static void EnsureExampleConfig()
    {
        Directory.CreateDirectory(ConfigDir);
        if (File.Exists(McpFile)) return;

        var example = new McpConfig
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["example-server"] = new McpServerConfig
                {
                    Command  = "npx",
                    Args     = ["-y", "@example/mcp-server@latest"],
                    Disabled = true,
                }
            }
        };

        File.WriteAllText(McpFile, JsonSerializer.Serialize(example, JsonOpts));
    }
}
