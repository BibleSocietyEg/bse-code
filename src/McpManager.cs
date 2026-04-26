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

// ── MCP Session ───────────────────────────────────────────────────────────────

/// <summary>
/// Represents a persistent, initialized connection to a single MCP server process.
/// </summary>
internal sealed class McpSession : IAsyncDisposable
{
    public string ServerName { get; }
    public Process Process { get; }
    public StreamWriter Stdin { get; }
    public StreamReader Stdout { get; }
    public bool IsAlive => !Process.HasExited;

    private int _nextId = 1;
    public int NextId() => Interlocked.Increment(ref _nextId);

    public McpSession(string serverName, Process process)
    {
        ServerName = serverName;
        Process = process;
        Stdin = process.StandardInput;
        Stdout = process.StandardOutput;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!Process.HasExited)
                Process.Kill(entireProcessTree: true);
        }
        catch { /* best-effort */ }

        try
        {
            await Process.WaitForExitAsync()
                .WaitAsync(TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
        }
        catch { /* timeout or already exited */ }

        Process.Dispose();
    }
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
    private static readonly Dictionary<string, McpSession> _sessions = [];
    private static readonly Dictionary<string, int> _restartCounts = [];
    private static readonly HashSet<string> _unavailable = [];

    public static IReadOnlyList<McpTool> Tools => _tools;

    public static IReadOnlyDictionary<string, McpServerConfig> Servers =>
        _sessions.Keys
            .Where(k => _config.McpServers.ContainsKey(k))
            .ToDictionary(k => k, k => _config.McpServers[k]);

    /// <summary>Loads MCP config and discovers tools from all enabled servers.</summary>
    public static Task LoadAsync() => LoadAsync(McpFile);

    /// <summary>Loads MCP config from the specified path and discovers tools from all enabled servers.</summary>
    internal static async Task LoadAsync(string mcpFilePath)
    {
        // Terminate existing sessions first
        await DisposeAsync();

        _tools.Clear();
        _restartCounts.Clear();
        _unavailable.Clear();

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

            try
            {
                var session = await SpawnSessionAsync(name, server);
                _sessions[name] = session;
                await DiscoverToolsAsync(name, session);
            }
            catch (Exception ex)
            {
                UI.Warn($"🔌 MCP server '{name}': failed to start — {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Spawns a new MCP session: starts the process, performs the initialize handshake,
    /// and returns the ready-to-use session.
    /// </summary>
    private static async Task<McpSession> SpawnSessionAsync(string serverName, McpServerConfig server)
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

        foreach (var arg in server.Args)
            startInfo.ArgumentList.Add(arg);

        foreach (var (k, v) in server.Env)
            startInfo.Environment[k] = v;

        var process = new Process { StartInfo = startInfo };
        process.Start();

        // Send initialize request (id=1)
        var initRequest = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "bse-code", version = "1.3.0" }
            }
        };

        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(initRequest));
        await process.StandardInput.FlushAsync();

        // Read initialize response
        var initLine = await ReadLineWithTimeoutAsync(process.StandardOutput, 5000);
        if (initLine is null)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            process.Dispose();
            throw new Exception($"MCP server '{serverName}' did not respond to initialize");
        }

        // Send notifications/initialized
        var initializedNotif = new { jsonrpc = "2.0", method = "notifications/initialized" };
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(initializedNotif));
        await process.StandardInput.FlushAsync();

        return new McpSession(serverName, process);
    }

    /// <summary>
    /// Ensures the session for the given server is alive, restarting up to 3 times if needed.
    /// Returns null if the server is unavailable or restart attempts are exhausted.
    /// </summary>
    private static async Task<McpSession?> EnsureSessionAliveAsync(string serverName)
    {
        if (_unavailable.Contains(serverName)) return null;

        if (_sessions.TryGetValue(serverName, out var session) && session.IsAlive)
            return session;

        _restartCounts.TryGetValue(serverName, out int count);
        if (count >= 3)
        {
            _unavailable.Add(serverName);
            return null;
        }

        UI.Warn($"🔌 MCP '{serverName}' exited unexpectedly. Restarting (attempt {count + 1}/3)...");
        _restartCounts[serverName] = count + 1;

        try
        {
            // Dispose old session if it exists
            if (_sessions.TryGetValue(serverName, out var oldSession))
            {
                await oldSession.DisposeAsync();
                _sessions.Remove(serverName);
            }

            var newSession = await SpawnSessionAsync(serverName, _config.McpServers[serverName]);
            _sessions[serverName] = newSession;
            return newSession;
        }
        catch (Exception ex)
        {
            UI.Warn($"🔌 MCP '{serverName}': restart failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Discovers tools from an MCP server using the persistent session.
    /// </summary>
    private static async Task DiscoverToolsAsync(string serverName, McpSession session)
    {
        try
        {
            var tools = await SendMcpRequestAsync(session, "tools/list", null);
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

    /// <summary>
    /// Executes an MCP tool call using the persistent session.
    /// </summary>
    public static async Task<string> CallToolAsync(string serverName, string toolName, string argsJson)
    {
        var session = await EnsureSessionAliveAsync(serverName);
        if (session is null)
            return $"❌ ERROR: MCP server '{serverName}' not found or disabled.";

        try
        {
            var callParams = new
            {
                name = toolName,
                arguments = JsonSerializer.Deserialize<JsonElement>(argsJson)
            };

            var result = await SendMcpRequestAsync(session, "tools/call", callParams);
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
    /// Sends a JSON-RPC 2.0 request to an MCP server via the persistent session's stdio streams.
    /// </summary>
    private static async Task<JsonElement?> SendMcpRequestAsync(
        McpSession session, string method, object? @params)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = session.NextId(),
            method,
            @params
        };

        await session.Stdin.WriteLineAsync(JsonSerializer.Serialize(request));
        await session.Stdin.FlushAsync();

        var responseLine = await ReadLineWithTimeoutAsync(session.Stdout, 10000);
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
    /// Gracefully terminates all active MCP session processes and releases resources.
    /// </summary>
    public static async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync();
        }
        _sessions.Clear();
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
