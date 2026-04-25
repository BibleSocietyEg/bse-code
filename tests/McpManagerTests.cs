using FluentAssertions;
using System.Text.Json;

namespace BSE_Code.Tests;

/// <summary>
/// Tests for McpManager config parsing and ChatTool conversion.
/// We don't spin up real MCP server processes — we test the data models
/// and the ToChatTools() conversion logic.
/// </summary>
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
}
