# Contributing to BSE-Code

Thanks for your interest in contributing. Here's everything you need to get started.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

## Getting started

```sh
git clone https://github.com/BibleSocietyEg/bse-code
cd bse-code
dotnet restore
```

## Running the app locally

```sh
dotnet run --project BSE_Code.csproj
```

## Running tests

```sh
dotnet test tests/BSE_Code.Tests.csproj
```

With coverage:

```sh
dotnet test tests/BSE_Code.Tests.csproj --collect:"XPlat Code Coverage"
```

## Code formatting

This project uses `dotnet format`. Run it before committing:

```sh
dotnet format
```

To verify without making changes (same check CI runs):

```sh
dotnet format --verify-no-changes
```

A pre-commit hook is provided to automate this. Install it once:

```sh
cp .githooks/pre-commit .git/hooks/pre-commit
chmod +x .git/hooks/pre-commit
```

Or configure Git to use the hooks directory globally:

```sh
git config core.hooksPath .githooks
```

## Windows Contributors

On Windows, the standard shell pre-commit hook (`.githooks/pre-commit`) requires a POSIX-compatible shell (e.g. Git Bash). If you're using PowerShell natively, a dedicated PowerShell hook is provided.

**Install the hook** (works for both the shell and PowerShell hooks):

```powershell
git config core.hooksPath .githooks
```

**Run it manually at any time:**

```powershell
pwsh .githooks/pre-commit.ps1
```

**What it checks:**

1. `dotnet format --verify-no-changes` — ensures all code is correctly formatted
2. `dotnet test tests/BSE_Code.Tests.csproj -c Release` — runs the full test suite

If either check fails, the script prints a remediation message and exits with a non-zero code.

## Branch strategy

- `main` — stable, always releasable
- Feature branches — `feat/your-feature`, merged via PR
- Releases are triggered by pushing a `v*` tag (e.g. `v1.9.0`)

## Commit messages

This project uses [Conventional Commits](https://www.conventionalcommits.org/) for automated changelog generation:

```
feat: add timeout support to BashTool
fix: handle empty BSE.md files in MemoryManager
docs: update provider list in README
test: add ToolCallAccumulator tests
chore: bump OpenAI SDK to 2.11.0
```

## Cutting a release

1. Update `<Version>` in `BSE_Code.csproj`
2. Commit: `chore: bump version to 1.9.0`
3. Tag and push: `git tag v1.9.0 && git push origin v1.9.0`
4. The release workflow handles the rest (tests → binaries → GitHub Release → npm → NuGet)

## Adding a new tool

1. Create `src/Tools/YourTool.cs` implementing `IToolHandler`
2. Register it in `ToolRegistry.CreateDefault()`
3. Add tests in `tests/Tools/YourToolTests.cs`

---

## Adding a New Tool

Implement the `IToolHandler` interface in a new file under `src/Tools/`:

```csharp
/// <summary>Does something useful for the AI.</summary>
public sealed class MyTool : IToolHandler
{
    // The name the LLM uses to call this tool. Must be alphanumeric + underscores.
    public string Name => "my_tool";

    // Description shown to the LLM in the system prompt.
    public string Description => "Does something useful";

    // JSON Schema describing the tool's parameters.
    public object ParameterSchema => new
    {
        type = "object",
        required = new[] { "input" },
        properties = new
        {
            input = new { type = "string", description = "The input to process" },
            optional_flag = new { type = "boolean", description = "Optional flag (default: false)" }
        }
    };

    public Task<string> ExecuteAsync(string argsJson)
    {
        // Use ArgumentParser to parse the JSON arguments
        var args = ArgumentParser.ParseStringMap(argsJson);

        if (!args.TryGetValue("input", out var input) || string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("'input' is required.");

        // Return an error string (never throw) for expected failure cases
        if (input.Length > 1000)
            return Task.FromResult("ERROR: Input too long (max 1000 chars)");

        var result = $"Processed: {input}";
        return Task.FromResult(result);
    }
}
```

Then register it in `ToolRegistry.CreateDefault()` in `src/Tools/ToolRegistry.cs`:

```csharp
public static ToolRegistry CreateDefault(AppConfig? config = null) => new(
[
    // ... existing tools ...
    new MyTool(),
]);
```

Add tests in `tests/Tools/MyToolTests.cs` (see [Testing New Tools](#testing-new-tools) below).

---

## Tool Naming Conventions

- `IToolHandler.Name` must be a valid function name: alphanumeric characters and underscores only (no spaces, hyphens, or special characters).
- `ToolRegistry` dispatches by name case-insensitively.
- MCP server tools are automatically named using the scheme `mcp__serverName__toolName` (e.g., `mcp__filesystem__read_file`). Do not use this prefix for built-in tools.
- Keep names short and descriptive: `read_file`, `edit_file`, `semantic_search`, `diagnostic`.

---

## Adding a New LLM Provider

1. Add an entry to the `LlmProvider` enum in `src/ConfigManager.cs`:

```csharp
public enum LlmProvider
{
    // ... existing entries ...
    MyProvider
}
```

2. Add a `ProviderDef` entry to the `Providers` array:

```csharp
new(9, LlmProvider.MyProvider, "My Provider", "Description of the provider",
    needsApiKey: true,
    defaultBaseUrl: "https://api.myprovider.com/v1",
    defaultModel: "my-model-id",
    apiKeyUrl: "https://myprovider.com/api-keys"),
```

Fields:
- `Number`: next sequential integer (used in the setup wizard menu)
- `NeedsApiKey`: `false` for local providers (Ollama, LM Studio)
- `DefaultBaseUrl`: the OpenAI-compatible API base URL
- `DefaultModel`: pre-selected model in the wizard
- `ApiKeyUrl`: link shown to the user when prompting for an API key

3. Add a `FallbackModels` entry for the model picker:

```csharp
[LlmProvider.MyProvider] =
[
    ("My Models", [
        new("my-model-id", "My Model Name", false),
    ]),
],
```

---

## Configuring MCP Servers

Edit `~/.bse-code/mcp.json` to add MCP servers. Each server entry specifies a command to run:

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem@latest", "/path/to/allow"],
      "env": {},
      "disabled": false
    },
    "git": {
      "command": "uvx",
      "args": ["mcp-server-git", "--repository", "/path/to/repo"],
      "env": {
        "GIT_AUTHOR_NAME": "BSE-Code"
      },
      "disabled": false
    }
  }
}
```

Field reference:
- `command`: the executable to run (e.g., `npx`, `uvx`, `python`, `node`)
- `args`: command-line arguments passed to the executable
- `env`: environment variables injected into the server process (use for secrets/tokens)
- `disabled`: set to `true` to temporarily disable a server without removing it

Once configured, tools from MCP servers appear as `mcp__serverName__toolName` in the tool list. Use `/mcp reload` in the REPL to pick up config changes without restarting.

---

## Testing New Tools

Create `tests/Tools/MyToolTests.cs`:

```csharp
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;

namespace BSE_Code.Tests.Tools;

public class MyToolTests
{
    private readonly MyTool _tool = new();

    [Fact]
    public async Task ExecuteAsync_ValidInput_ReturnsResult()
    {
        var result = await _tool.ExecuteAsync("""{"input": "hello"}""");
        result.Should().Contain("hello");
    }

    [Fact]
    public async Task ExecuteAsync_MissingInput_ThrowsArgumentException()
    {
        var act = async () => await _tool.ExecuteAsync("{}");
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*input*");
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutScenario_ReturnsErrorString()
    {
        // For tools that call external processes, test timeout behavior
        // by using a very short timeout and a slow command.
        // Tools should return an error string, never throw.
        var result = await _tool.ExecuteAsync("""{"input": "test"}""");
        result.Should().NotBeNull();
    }

    // Optional: add a property test for invariants
    [Property(MaxTest = 100)]
    public bool MyTool_OutputAlwaysNonNull(NonEmptyString input)
    {
        var argsJson = System.Text.Json.JsonSerializer.Serialize(new { input = input.Get });
        var result = _tool.ExecuteAsync(argsJson).GetAwaiter().GetResult();
        return result is not null;
    }
}
```

For tools requiring external binaries, use `[Fact(Skip = "requires external binary")]` to skip gracefully in CI:

```csharp
[Fact(Skip = "requires npx")]
public async Task ExecuteAsync_WithRealServer_ReturnsContent() { ... }
```
