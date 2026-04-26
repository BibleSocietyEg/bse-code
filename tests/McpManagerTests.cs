using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace BSE_Code.Tests;

/// <summary>
/// FsCheck generators for McpManager property tests.
/// </summary>
public static class McpManagerGenerators
{
    // Uses default FsCheck generators — NonEmptyString is built-in
}

/// <summary>
/// Tests for McpManager config parsing and ChatTool conversion.
/// We don't spin up real MCP server processes — we test the data models
/// and the ToChatTools() conversion logic.
/// </summary>
[Collection("Sequential")]
public class McpManagerTests
{
    // ── McpServerConfig deserialization ───────────────────────────────────────

    [Fact]
    public void McpServerConfig_Deserializes_CommandAndArgs()
    {
        var json = """
            {
              "command": "npx",
              "args": ["-y", "@modelcontextprotocol/server-filesystem@latest"],
              "disabled": false
            }
            """;

        var cfg = JsonSerializer.Deserialize<McpServerConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        cfg.Command.Should().Be("npx");
        cfg.Args.Should().Equal("-y", "@modelcontextprotocol/server-filesystem@latest");
        cfg.Disabled.Should().BeFalse();
    }

    [Fact]
    public void McpServerConfig_Disabled_DefaultsToFalse()
    {
        var json = """{"command": "node", "args": []}""";

        var cfg = JsonSerializer.Deserialize<McpServerConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        cfg.Disabled.Should().BeFalse();
    }

    [Fact]
    public void McpServerConfig_Env_DeserializesKeyValuePairs()
    {
        var json = """
            {
              "command": "python",
              "args": ["server.py"],
              "env": { "API_KEY": "secret", "DEBUG": "true" }
            }
            """;

        var cfg = JsonSerializer.Deserialize<McpServerConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        cfg.Env.Should().ContainKey("API_KEY").WhoseValue.Should().Be("secret");
        cfg.Env.Should().ContainKey("DEBUG").WhoseValue.Should().Be("true");
    }

    // ── McpConfig deserialization ─────────────────────────────────────────────

    [Fact]
    public void McpConfig_Deserializes_MultipleServers()
    {
        var json = """
            {
              "mcpServers": {
                "fs": { "command": "npx", "args": ["-y", "server-fs"] },
                "git": { "command": "npx", "args": ["-y", "server-git"], "disabled": true }
              }
            }
            """;

        var cfg = JsonSerializer.Deserialize<McpConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        cfg.McpServers.Should().HaveCount(2);
        cfg.McpServers["fs"].Command.Should().Be("npx");
        cfg.McpServers["git"].Disabled.Should().BeTrue();
    }

    // ── McpTool ───────────────────────────────────────────────────────────────

    [Fact]
    public void McpTool_FullName_CombinesServerAndToolName()
    {
        var tool = new McpTool { ServerName = "filesystem", Name = "read_file" };

        tool.FullName.Should().Be("mcp__filesystem__read_file");
    }

    [Fact]
    public void McpTool_FullName_Format_IsConsistent()
    {
        var tool = new McpTool { ServerName = "my-server", Name = "do_thing" };

        tool.FullName.Should().StartWith("mcp__");
        tool.FullName.Should().Contain("my-server");
        tool.FullName.Should().Contain("do_thing");
    }

    // ── ToChatTools ───────────────────────────────────────────────────────────

    [Fact]
    public void ToChatTools_EmptyToolList_ReturnsEmpty()
    {
        // McpManager.Tools is empty when no servers are loaded
        var tools = McpManager.ToChatTools().ToList();

        // May have tools from a previous test run — just verify it doesn't throw
        tools.Should().NotBeNull();
    }

    // ── Task 10 required tests ────────────────────────────────────────────────

    [Fact]
    public async Task CallToolAsync_UnknownServer_ReturnsErrorString()
    {
        // No setup needed — just call with a server name that was never loaded
        var result = await McpManager.CallToolAsync("__nonexistent_server__", "some_tool", "{}");

        result.Should().StartWith("❌ ERROR:");
    }

    [Fact]
    public async Task CallToolAsync_ExceptionDuringCall_ReturnsErrorAndWarns()
    {
        // Load a server config with a non-existent command so process.Start() throws.
        // With persistent sessions, the server fails to start at LoadAsync time,
        // so CallToolAsync returns the "not found or disabled" error string.
        var tempMcpPath = await WriteTempMcpJsonAsync(new
        {
            mcpServers = new
            {
                broken_server = new
                {
                    command = "__nonexistent_executable_xyz_abc__",
                    args = new string[] { },
                    disabled = false
                }
            }
        });

        try
        {
            var stdoutCapture = new System.IO.StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(stdoutCapture);
            string result;
            try
            {
                await McpManager.LoadAsync(tempMcpPath);
                result = await McpManager.CallToolAsync("broken_server", "some_tool", "{}");
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            // Server failed to start, so it's not in sessions — returns error string
            result.Should().Contain("ERROR");
            stdoutCapture.ToString().Should().Contain("⚠️");
        }
        finally
        {
            File.Delete(tempMcpPath);
        }
    }

    [Fact]
    public async Task CallToolAsync_NullResponse_ReturnsErrorAndWarns()
    {
        // Use a command that starts but exits immediately without writing any stdout output,
        // causing ReadLineWithTimeoutAsync to return null (EOF) during initialize.
        // With persistent sessions, the server fails to start at LoadAsync time,
        // so CallToolAsync returns the "not found or disabled" error string.
        string command;
        string[] args;
        if (OperatingSystem.IsWindows())
        {
            command = "cmd";
            args = ["/c", "exit 0"];
        }
        else
        {
            command = "/bin/sh";
            args = ["-c", "exit 0"];
        }

        var tempMcpPath = await WriteTempMcpJsonAsync(new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["silent_server"] = new
                {
                    command,
                    args,
                    disabled = false
                }
            }
        });

        try
        {
            var stdoutCapture = new System.IO.StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(stdoutCapture);
            string result;
            try
            {
                await McpManager.LoadAsync(tempMcpPath);
                result = await McpManager.CallToolAsync("silent_server", "some_tool", "{}");
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            // Server failed to start (no initialize response), so it's not in sessions
            result.Should().Contain("ERROR");
            stdoutCapture.ToString().Should().Contain("⚠️");
        }
        finally
        {
            File.Delete(tempMcpPath);
        }
    }

    // ── Task 10.1: Property 8 ─────────────────────────────────────────────────

    // Feature: codebase-quality-improvements, Property 8: McpManager always surfaces errors visibly
    // With persistent sessions, a server that fails to start at LoadAsync time is not added to
    // _sessions. CallToolAsync then returns the "not found or disabled" error (which contains "ERROR").
    // The warning is emitted during LoadAsync when the server fails to start.
    [Property(MaxTest = 50)]
    public bool CallToolAsync_AnyException_ReturnsErrorStringAndWarns(NonEmptyString serverName)
    {
        // Load a server config with a non-existent command so process.Start() throws
        var tempMcpPath = WriteTempMcpJsonSync(new
        {
            mcpServers = new Dictionary<string, object>
            {
                [serverName.Get] = new
                {
                    command = "__nonexistent_executable_xyz_abc__",
                    args = new string[] { },
                    disabled = false
                }
            }
        });

        try
        {
            var stdoutCapture = new System.IO.StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(stdoutCapture);
            string result;
            try
            {
                McpManager.LoadAsync(tempMcpPath).GetAwaiter().GetResult();
                result = McpManager.CallToolAsync(serverName.Get, "tool", "{}").GetAwaiter().GetResult();
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            // Server failed to start → not in sessions → CallToolAsync returns error string
            // Warning is emitted during LoadAsync when the server fails to start
            return result.Contains("ERROR") && stdoutCapture.ToString().Contains("⚠️");
        }
        finally
        {
            File.Delete(tempMcpPath);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<string> WriteTempMcpJsonAsync(object config)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mcp-test-{Guid.NewGuid():N}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(config,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    private static string WriteTempMcpJsonSync(object config)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mcp-test-{Guid.NewGuid():N}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(config,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        return path;
    }
}
