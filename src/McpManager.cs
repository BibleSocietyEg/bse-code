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
    public string Name { get; init; } = "";
    public string FullName => $"mcp__{ServerName}__{Name}";
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
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bse-code");
    private static readonly string McpFile = Path.Combine(ConfigDir, "mcp.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static McpConfig _config = new();
    private static readonly List<McpTool> _tools = [];
    private static readonly Dictionary<string, McpServerConfig> _activeServerConfigs = [];
    private static readonly Dictionary<string, McpSession> _sessions = [];

    public static IReadOnlyList<McpTool> Tools => _tools;
    public static IReadOnlyDictionary<string, McpServerConfig> Servers => _activeServerConfigs;

    private class McpSession : IDisposable
    {
        public string ServerName { get; }
        public Process Process { get; }
        public int NextRequestId { get; private set; } = 1;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public McpSession(string serverName, Process process)
        {
            ServerName = serverName;
            Process = process;
        }

        public async Task<JsonElement?> SendRequestAsync(string method, object? @params, int timeoutMs = 10000)
        {
            await _semaphore.WaitAsync();
            try
            {
                var id = NextRequestId++;
                var request = new
                {
                    jsonrpc = "2.0",
                    id,
                    method,
                    @params
                };

                var json = JsonSerializer.Serialize(request);
                await Process.StandardInput.WriteLineAsync(json);
                await Process.StandardInput.FlushAsync();

                var deadline = Environment.TickCount64 + timeoutMs;
                while (true)
                {
                    var remaining = (int)(deadline - Environment.TickCount64);
                    if (remaining <= 0) return null;

                    var responseLine = await ReadLineWithTimeoutAsync(Process.StandardOutput, remaining);
                    if (responseLine is null) return null;

                    using var doc = JsonDocument.Parse(responseLine);
                    if (!doc.RootElement.TryGetProperty("id", out var resId)) continue; // notification
                    if (resId.ValueKind != JsonValueKind.Number || resId.GetInt32() != id) continue;

                    if (doc.RootElement.TryGetProperty("result", out var result))
                        return result.Clone();
                    if (doc.RootElement.TryGetProperty("error", out var error))
                        throw new Exception(error.GetRawText());
                    return null;
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task SendNotificationAsync(string method, object? @params = null)
        {
            await _semaphore.WaitAsync();
            try
            {
                var notif = new
                {
                    jsonrpc = "2.0",
                    method,
                    @params
                };
                await Process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(notif));
                await Process.StandardInput.FlushAsync();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Dispose()
        {
            try { if (!Process.HasExited) Process.Kill(true); } catch { }
            Process.Dispose();
            _semaphore.Dispose();
        }
    }

    /// <summary>Loads MCP config and discovers tools from all enabled servers.</summary>
    public static Task LoadAsync() => LoadAsync(McpFile);

    /// <summary>Loads MCP config from the specified path and discovers tools from all enabled servers.</summary>
    internal static async Task LoadAsync(string mcpFilePath)
    {
        _tools.Clear();
        _activeServerConfigs.Clear();
        foreach (var session in _sessions.Values) session.Dispose();
        _sessions.Clear();

        if (!File.Exists(mcpFilePath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(mcpFilePath);
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
            _activeServerConfigs[name] = server;
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
            var session = await GetOrCreateSessionAsync(serverName, server);
            if (session is null) return;

            var tools = await session.SendRequestAsync("tools/list", null);
            if (tools is null) return;

            if (tools.Value.TryGetProperty("tools", out var toolsArr))
            {
                foreach (var tool in toolsArr.EnumerateArray())
                {
                    var toolName = tool.GetProperty("name").GetString() ?? "";
                    var desc = tool.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                    var schema = tool.TryGetProperty("inputSchema", out var s) ? s : default;

                    _tools.Add(new McpTool
                    {
                        ServerName = serverName,
                        Name = toolName,
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

    private static async Task<McpSession?> GetOrCreateSessionAsync(string serverName, McpServerConfig server)
    {
        if (_sessions.TryGetValue(serverName, out var session))
        {
            if (!session.Process.HasExited) return session;
            session.Dispose();
            _sessions.Remove(serverName);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = server.Command,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var arg in server.Args) startInfo.ArgumentList.Add(arg);
            foreach (var (k, v) in server.Env) startInfo.Environment[k] = v;

            var process = new Process { StartInfo = startInfo };
            process.Start();

            var newSession = new McpSession(serverName, process);

            // Initialize
            var initResult = await newSession.SendRequestAsync("initialize", new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "bse-code", version = "1.3.0" }
            });

            if (initResult is null)
            {
                newSession.Dispose();
                return null;
            }

            await newSession.SendNotificationAsync("notifications/initialized");
            _sessions[serverName] = newSession;
            return newSession;
        }
        catch (Exception ex)
        {
            UI.Warn($"🔌 Failed to start MCP server '{serverName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Executes an MCP tool call by spawning the server process and sending a JSON-RPC request.
    /// </summary>
    public static async Task<string> CallToolAsync(string serverName, string toolName, string argsJson)
    {
        if (!_activeServerConfigs.TryGetValue(serverName, out var server))
            return $"❌ ERROR: MCP server '{serverName}' not found or disabled.";

        try
        {
            var session = await GetOrCreateSessionAsync(serverName, server);
            if (session is null) return "ERROR: Could not start/connect to MCP server.";

            var callParams = new
            {
                name = toolName,
                arguments = JsonSerializer.Deserialize<JsonElement>(argsJson)
            };

            var result = await session.SendRequestAsync("tools/call", callParams);
            if (result is null)
            {
                UI.Warn($"🔌 MCP '{serverName}/{toolName}': no response (timeout or empty).");
                return "ERROR: No response from MCP server.";
            }

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
            UI.Warn($"🔌 MCP '{serverName}/{toolName}' failed: {ex.Message}");
            return $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Sends a JSON-RPC 2.0 request to an MCP server via stdio.
    /// </summary>
    [Obsolete("Use McpSession.SendRequestAsync instead")]
    private static Task<JsonElement?> SendMcpRequestAsync(
        string serverName, McpServerConfig server, string method, object? @params)
    {
        return Task.FromResult<JsonElement?>(null);
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
                functionName: tool.FullName,
                functionDescription: $"[MCP:{tool.ServerName}] {tool.Description}",
                functionParameters: schema
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
                    Command = "npx",
                    Args = ["-y", "@example/mcp-server@latest"],
                    Disabled = true,
                }
            }
        };

        File.WriteAllText(McpFile, JsonSerializer.Serialize(example, JsonOpts));
    }
}
