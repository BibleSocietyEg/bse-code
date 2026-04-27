# Design Document: bse-code Improvements

## Overview

This document describes the technical design for 12 improvements to bse-code, a .NET 10 CLI AI coding assistant. The improvements span four categories: technical gaps (T1–T6), performance (P1–P2), security (S1–S2), and documentation/testing (N1–N2). They are organized into three implementation phases.

**Phase 1 (Immediate):** Persistent MCP sessions (T1), EditFileTool (T2), Encrypted config (S2)
**Phase 2 (Short-Term):** Semantic search (T3), Diagnostic tool (T5)
**Phase 3 (Long-Term):** Rich text rendering (T6), Parallel tool dispatch (P1)
**Remaining:** BashTool stdin (T4), Token-aware compaction (P2), Shell safeguards (S1), Docs (N1), Integration tests (N2)

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│  Program.cs (entry point)                                           │
│    ├── ConfigManager.LoadOrSetupAsync()  [S2: encrypted ApiKey]     │
│    ├── McpManager.LoadAsync()            [T1: persistent sessions]  │
│    ├── ToolRegistry.CreateDefault()      [T2,T3,T4,T5: new tools]   │
│    └── ReplEngine.RunAsync()             [T6,P1: rendering/parallel]│
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  McpManager (static)                                                │
│    ├── _sessions: Dictionary<string, McpSession>  [T1 NEW]         │
│    ├── LoadAsync()  → spawns McpSession per server                  │
│    ├── CallToolAsync()  → reuses session stdin/stdout               │
│    └── DisposeAsync()  → terminates all sessions                    │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  ToolRegistry                                                       │
│    ├── ReadFileTool, WriteFileTool, BashTool [existing]             │
│    ├── ListDirTool, GlobTool, GrepTool       [existing]             │
│    ├── EditFileTool                          [T2 NEW]               │
│    ├── SemanticSearchTool                    [T3 NEW]               │
│    ├── DiagnosticTool                        [T5 NEW]               │
│    └── BashTool (stdin + safeguards)         [T4+S1 enhanced]       │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  ReplEngine.RunTurnAsync()                                          │
│    ├── Stream LLM response                                          │
│    ├── Buffer markdown → MarkdownRenderer.Render()  [T6 NEW]        │
│    └── Task.WhenAll() parallel dispatch             [P1 enhanced]   │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  ConfigManager                                                      │
│    ├── Save()  → encrypts ApiKey before write  [S2 enhanced]        │
│    └── Load()  → detects version, decrypts     [S2 enhanced]        │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  SlashCommandHandler.HandleCompactAsync()                           │
│    └── EstimateTokens() + selective pruning    [P2 enhanced]        │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Architecture

### Dependency Changes

**New NuGet packages (add to `src/BSE_Code.Core.csproj`):**

```xml
<PackageReference Include="Spectre.Console" Version="0.49.*" />
```

The `OpenAI 2.10.0` SDK already provides `EmbeddingClient` for T3 — no additional package needed.

**Platform-conditional assembly (S2, Windows only):**

```xml
<ItemGroup Condition="'$(OS)' == 'Windows_NT'">
  <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="9.*" />
</ItemGroup>
```

### File Map

| Requirement | New/Modified File |
|---|---|
| T1 | `src/McpManager.cs` (refactor) |
| T2 | `src/Tools/EditFileTool.cs` (new), `src/Tools/ToolRegistry.cs` |
| S2 | `src/ConfigManager.cs` (refactor) |
| T3 | `src/Tools/SemanticSearchTool.cs` (new), `src/Tools/ToolRegistry.cs` |
| T5 | `src/Tools/DiagnosticTool.cs` (new), `src/Tools/ToolRegistry.cs` |
| T6 | `src/MarkdownRenderer.cs` (new), `src/ReplEngine.cs`, `src/BSE_Code.Core.csproj` |
| P1 | `src/ReplEngine.cs` (refactor) |
| T4 | `src/Tools/BashTool.cs` (enhance) |
| P2 | `src/SlashCommandHandler.cs` (enhance) |
| S1 | `src/Tools/BashTool.cs` (enhance) |
| N1 | `CONTRIBUTING.md` (expand) |
| N2 | `tests/McpManagerTests.cs`, `tests/Tools/BashToolTests.cs` |

---

## Components and Interfaces

### Req 1 (T1): Persistent MCP Sessions

**New class `McpSession`** (internal, lives in `McpManager.cs`):

```csharp
internal sealed class McpSession : IAsyncDisposable
{
    public string ServerName { get; }
    public Process Process { get; }
    public StreamWriter Stdin { get; }
    public StreamReader Stdout { get; }
    private int _nextId = 1;
    public int NextId() => Interlocked.Increment(ref _nextId);
    public bool IsAlive => !Process.HasExited;
    public async ValueTask DisposeAsync() { ... }
}
```

**`McpManager` changes:**

- Replace `_activeServers: Dictionary<string, McpServerConfig>` with `_sessions: Dictionary<string, McpSession>` (private, static).
- Add `_restartCounts: Dictionary<string, int>` and `_unavailable: HashSet<string>`.
- `LoadAsync()`: terminate existing sessions, then spawn one `McpSession` per enabled server, run initialize handshake once.
- `CallToolAsync()`: look up session, call `SendMcpRequestAsync(session, ...)` using the session's streams.
- `DisposeAsync()`: iterate `_sessions`, dispose each.
- `SendMcpRequestAsync()` becomes an instance-style helper taking `McpSession` instead of spawning a process.

**Data flow:**

```
LoadAsync()
  ├── foreach enabled server
  │     ├── SpawnSessionAsync(name, config) → McpSession
  │     │     ├── Process.Start()
  │     │     ├── send initialize request
  │     │     ├── read initialize response
  │     │     └── send notifications/initialized
  │     └── DiscoverToolsAsync(session)
  └── _sessions[name] = session

CallToolAsync(serverName, toolName, argsJson)
  ├── _sessions[serverName] → session
  ├── check session.IsAlive → restart if needed (up to 3x)
  └── SendMcpRequestAsync(session, "tools/call", params)
        ├── write JSON-RPC line to session.Stdin
        └── read response line from session.Stdout
```

**Restart logic:**

```csharp
private static async Task<McpSession?> EnsureSessionAliveAsync(string serverName)
{
    if (_sessions.TryGetValue(serverName, out var session) && session.IsAlive)
        return session;

    if (_unavailable.Contains(serverName)) return null;

    _restartCounts.TryGetValue(serverName, out int count);
    if (count >= 3) { _unavailable.Add(serverName); return null; }

    UI.Warn($"🔌 MCP '{serverName}' exited unexpectedly. Restarting (attempt {count + 1}/3)...");
    _restartCounts[serverName] = count + 1;
    // re-spawn and re-initialize
    var newSession = await SpawnSessionAsync(serverName, _config.McpServers[serverName]);
    _sessions[serverName] = newSession;
    return newSession;
}
```

### Req 2 (T2): EditFileTool

**New file `src/Tools/EditFileTool.cs`:**

```csharp
public sealed class EditFileTool : IToolHandler
{
    public string Name => "edit_file";
    public string Description => "Replace the first exact occurrence of old_str with new_str in a file";
    public object ParameterSchema => new {
        type = "object",
        required = new[] { "file_path", "old_str", "new_str" },
        properties = new {
            file_path = new { type = "string" },
            old_str   = new { type = "string" },
            new_str   = new { type = "string" }
        }
    };

    public async Task<string> ExecuteAsync(string argsJson)
    {
        // 1. Parse args
        // 2. File.Exists check → error
        // 3. Read content
        // 4. Count occurrences of old_str → 0: not found error, >1: ambiguous error
        // 5. Replace first occurrence
        // 6. Write back
        // 7. Return confirmation with lines-changed count
    }
}
```

**Lines-changed calculation:** count `\n` in `old_str` vs `new_str`, report `Math.Abs(delta) + 1` lines affected.

**Registration in `ToolRegistry.CreateDefault()`:**

```csharp
new EditFileTool(),
```

### Req 3 (S2): Encrypted API Key Storage

**`AppConfig` changes:**

```csharp
[JsonPropertyName("api_key_encrypted")]
public string ApiKeyEncrypted { get; set; } = "";

[JsonIgnore]
public string ApiKey { get; set; } = "";   // runtime only

[JsonPropertyName("config_version")]
public int ConfigVersion { get; set; } = 1; // 2 = encrypted
```

**`ConfigManager` new private helpers:**

```csharp
private static string EncryptApiKey(string plaintext);   // platform dispatch
private static string DecryptApiKey(string ciphertext);  // platform dispatch
private static string EncryptWindows(string plaintext);  // DPAPI
private static string DecryptWindows(string ciphertext);
private static string EncryptAesGcm(string plaintext);   // AES-256-GCM
private static string DecryptAesGcm(string ciphertext);
private static byte[] DeriveKey();                        // SHA-256(MachineName)
```

**`Save(AppConfig config)` flow:**

```
if config.ApiKey is non-empty:
    config.ApiKeyEncrypted = EncryptApiKey(config.ApiKey)
    config.ConfigVersion = 2
serialize (ApiKey is [JsonIgnore], so not written)
File.WriteAllText(ConfigFile, json)
```

**`Load()` flow:**

```
json = File.ReadAllText(ConfigFile)
config = Deserialize<AppConfig>(json)
if config.ConfigVersion == 2:
    try: config.ApiKey = DecryptApiKey(config.ApiKeyEncrypted)
    catch: UI.Warn(...); config.ApiKey = ""
else:
    config.ApiKey = config.ApiKeyEncrypted (legacy plaintext field)
return config
```

**`LoadOrSetupAsync()` env-var bypass:**

```csharp
var envKey = Environment.GetEnvironmentVariable("BSE_API_KEY") ?? ...;
if (!string.IsNullOrEmpty(envKey))
{
    saved.ApiKey = envKey;  // skip decryption entirely
}
```

**Migration strategy:** On first `Save()` after upgrade, the existing `api_key` field is read by `Load()` as legacy plaintext (version 1), then re-saved encrypted (version 2). No manual migration step needed.

### Req 4 (T3): Semantic Search Tool

**New file `src/Tools/SemanticSearchTool.cs`:**

```csharp
public sealed class SemanticSearchTool : IToolHandler
{
    private static readonly List<CodeChunk> _index = [];
    private static readonly Dictionary<string, DateTime> _fileTimestamps = [];
    private static readonly SemaphoreSlim _indexLock = new(1, 1);

    public string Name => "semantic_search";
    public string Description => "Search the codebase by semantic meaning using embeddings";
    public object ParameterSchema => new {
        type = "object",
        required = new[] { "query" },
        properties = new {
            query = new { type = "string" },
            path  = new { type = "string", description = "Restrict search to this path" },
            top_n = new { type = "integer", description = "Number of results (default 10)" }
        }
    };

    public async Task<string> ExecuteAsync(string argsJson) { ... }

    private static async Task BuildOrRefreshIndexAsync(string rootPath, EmbeddingClient client) { ... }
    private static IEnumerable<CodeChunk> ChunkFile(string filePath) { ... }
    private static float CosineSimilarity(float[] a, float[] b) { ... }
}
```

**Index build flow:**

```
BuildOrRefreshIndexAsync(rootPath, client):
  foreach .cs/.ts/.py/etc file under rootPath:
    if file not in _fileTimestamps or lastWriteTime changed:
      remove old chunks for this file
      chunks = ChunkFile(file)   // ~200-line segments with overlap
      embeddings = client.GenerateEmbeddingsAsync(chunks.Select(c => c.Text))
      store chunks + embeddings in _index
      _fileTimestamps[file] = lastWriteTime
```

**Query flow:**

```
ExecuteAsync:
  await BuildOrRefreshIndexAsync(searchPath, embeddingClient)
  queryEmbedding = client.GenerateEmbeddingAsync(query)
  scores = _index.Select(c => (c, CosineSimilarity(c.Embedding, queryEmbedding)))
  return top_n results sorted by score descending
```

**`EmbeddingClient` construction:** uses `config.ApiKey` and `config.BaseUrl` from the injected `AppConfig`. The tool receives `AppConfig` via constructor injection (passed from `ToolRegistry.CreateDefault(AppConfig config)`).

### Req 5 (T5): DiagnosticTool

**New file `src/Tools/DiagnosticTool.cs`:**

```csharp
public sealed class DiagnosticTool : IToolHandler
{
    // MSBuild: path(line,col): severity code: message
    private static readonly Regex MsBuildRegex = new(
        @"^(.+)\((\d+),(\d+)\):\s+(error|warning|info)\s+(\w+):\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ESLint JSON: parsed separately
    public string Name => "diagnostic";
    public string Description => "Run a build or lint command and return structured diagnostics";
    public object ParameterSchema => new {
        type = "object",
        required = new[] { "command" },
        properties = new {
            command         = new { type = "string" },
            timeout_seconds = new { type = "integer" }
        }
    };

    public Task<string> ExecuteAsync(string argsJson)
    {
        // 1. Parse args, get timeout
        // 2. rawOutput = BashTool.RunShell(command, timeout)
        // 3. exitCode = parse from rawOutput or 0
        // 4. diagnostics = ParseMsBuild(rawOutput) ?? ParseEslintJson(rawOutput) ?? fallback
        // 5. return JsonSerializer.Serialize(new DiagnosticResult { ... })
    }
}
```

**Reuse of `BashTool.RunShell()`:** `DiagnosticTool` calls `BashTool.RunShell(command, timeout)` directly — no duplication of process management.

### Req 6 (T6): Rich Text Rendering

**New file `src/MarkdownRenderer.cs`:**

```csharp
public static class MarkdownRenderer
{
    public static bool IsPlainText =>
        Environment.GetEnvironmentVariable("NO_COLOR") is not null
        || Console.IsOutputRedirected;

    /// <summary>Renders markdown text to the terminal using Spectre.Console.</summary>
    public static void Render(string markdown)
    {
        if (IsPlainText) { Console.Write(markdown); return; }
        // Use Spectre.Console Markup + Panel for code blocks
        AnsiConsole.Write(new Markup(EscapeAndConvert(markdown)));
    }

    private static string EscapeAndConvert(string markdown) { ... }
}
```

**`ReplEngine.RunTurnAsync()` change:** instead of writing each token directly, accumulate into `contentBuilder`, then after the stream ends call `MarkdownRenderer.Render(contentBuilder.ToString())`. The spinner is stopped before rendering begins (no change to existing spinner logic).

**Buffering strategy:** full response is buffered before rendering. This is acceptable because the LLM response is already fully streamed into `contentBuilder` before tool calls are processed.

### Req 7 (P1): Parallel Tool Dispatch

**`ReplEngine.RunTurnAsync()` refactor:**

```csharp
// Group tool calls by file path for serialization
var fileLock = new ConcurrentDictionary<string, SemaphoreSlim>();

SemaphoreSlim GetFileLock(string toolName, string argsJson)
{
    var filePath = ExtractFilePath(toolName, argsJson) ?? "__no_file__";
    return fileLock.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
}

// Execute all tool calls concurrently, serializing same-file calls
var tasks = accumulators.Values.OrderBy(a => a.Index).Select(async acc =>
{
    var sem = GetFileLock(acc.Name, acc.Arguments);
    await sem.WaitAsync();
    try
    {
        return await ExecuteToolAsync(acc);
    }
    finally { sem.Release(); }
}).ToList();

var results = await Task.WhenAll(tasks);
```

**`ExtractFilePath()`:** inspects `file_path` argument for `read_file`, `Write`, `edit_file`; returns `null` for tools with no file path (they get the `__no_file__` bucket and run concurrently with each other).

**Result ordering:** `Task.WhenAll` preserves index order since tasks are created in order.

**Progress display:** before `Task.WhenAll`, print a spinner line per tool call; after completion, update each line with ✓/✗.

### Req 8 (T4): BashTool stdin Support

**`BashTool.RunShell()` signature change:**

```csharp
internal static string RunShell(string command, TimeSpan? timeout = null, string? stdin = null)
```

**`ExecuteAsync()` change:** parse optional `stdin` from args, pass to `RunShell`.

**Process setup:**

```csharp
startInfo.RedirectStandardInput = true;  // always true now

process.Start();

if (stdin is not null)
{
    await process.StandardInput.WriteAsync(stdin);
}
process.StandardInput.Close();  // always close — sends EOF
```

**Schema addition:**

```csharp
stdin = new { type = "string", description = "Optional string to write to stdin before closing" }
```

### Req 9 (P2): Token-Aware Compaction

**`SlashCommandHandler` additions:**

```csharp
private const int DefaultTokenBudget = 80_000;

private static int EstimateTokens(IEnumerable<ChatMessage> messages)
    => messages.Sum(m => GetMessageText(m).Length) / 4;

private static string GetMessageText(ChatMessage m) => m switch
{
    UserChatMessage u      => string.Concat(u.Content.Select(p => p.Text)),
    AssistantChatMessage a => string.Concat(a.Content.Select(p => p.Text)),
    SystemChatMessage s    => string.Concat(s.Content.Select(p => p.Text)),
    _                      => ""
};
```

**`HandleCompactAsync()` new flow:**

```
tokensBefore = EstimateTokens(messages)
UI.Print($"  📊 Estimated tokens: {tokensBefore:N0}")

if tokensBefore < DefaultTokenBudget:
    UI.Print("  ✅ Below budget — no compaction needed.")
    return

// Prune oldest non-system messages, keeping last 4 user/assistant pairs
var systemMessages = messages.Where(m => m is SystemChatMessage).ToList()
var nonSystem = messages.Where(m => m is not SystemChatMessage).ToList()
var protected = nonSystem.TakeLast(8).ToList()  // last 4 pairs = 8 messages
var prunable = nonSystem.SkipLast(8).ToList()

while (EstimateTokens(systemMessages.Concat(protected)) + EstimateTokens(prunable) > DefaultTokenBudget
       && prunable.Count > 0)
    prunable.RemoveAt(0)

messages.Clear()
messages.AddRange(systemMessages)
messages.AddRange(prunable)
messages.AddRange(protected)

// Run summarization
await _runTurn(Client, opts, messages, summarizePrompt)

tokensAfter = EstimateTokens(messages)
UI.Success($"🗜️  Compacted: {tokensBefore:N0} → {tokensAfter:N0} tokens")
```

### Req 10 (S1): Shell Command Safeguards

**`BashTool` additions:**

```csharp
private static readonly string[] Blocklist =
[
    "rm -rf /", "rm -rf ~", "format c:", "mkfs", "dd if=",
    ":(){:|:&};:", "del /f /s /q c:\\"
];

private static readonly string[] Allowlist =
[
    "echo ", "cat ", "ls", "dir", "git status", "git log",
    "git diff", "pwd", "type ", "dotnet build", "dotnet test",
    "dotnet run", "grep ", "find "
];

private static bool IsBlocked(string cmd)
    => Blocklist.Any(b => cmd.Contains(b, StringComparison.OrdinalIgnoreCase));

private static bool IsAllowed(string cmd)
    => Allowlist.Any(a => cmd.StartsWith(a, StringComparison.OrdinalIgnoreCase)
                       || cmd.Contains(a, StringComparison.OrdinalIgnoreCase));

private static readonly string AuditLog = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".bse-code", "audit.log");
```

**`ExecuteAsync()` flow:**

```
if IsBlocked(command): return error string (no execution)
skipConfirm = Environment.GetEnvironmentVariable("BSE_BASH_CONFIRM") == "off"
if !IsAllowed(command) && !skipConfirm:
    Console.Write($"  ⚠️  Allow command? [y/N]: {command}\n  > ")
    answer = Console.ReadLine()
    if answer?.ToLower() != "y": return "Command denied by user."
result = RunShell(command, timeout, stdin)
AppendAuditLog(command, exitCode)
return result
```

**Audit log format:** `{timestamp} | exit:{code} | {command}\n`

### Req 11 (N1): Extensibility Documentation

`CONTRIBUTING.md` gains five new sections. The design for each section's content:

1. **Adding a New Tool** — complete annotated `IToolHandler` implementation with `Name`, `Description`, `ParameterSchema` (JSON Schema object), `ExecuteAsync` with `ArgumentParser.ParseStringMap()` usage, error handling pattern, and registration line in `ToolRegistry.CreateDefault()`.

2. **Adding a New LLM Provider** — step-by-step: add to `LlmProvider` enum, add `ProviderDef` entry to `Providers` array (all fields explained), add `FallbackModels` entry, explain `NeedsApiKey` and `DefaultBaseUrl`.

3. **Configuring MCP Servers** — full `mcp.json` example with `filesystem` and `git` servers, field-by-field explanation, note on `disabled` flag, note on `env` for secrets.

4. **Tool Naming Conventions** — `mcp__serverName__toolName` scheme, `IToolHandler.Name` must be a valid function name (alphanumeric + underscore), case-insensitive dispatch in `ToolRegistry`.

5. **Testing New Tools** — example xunit test class with `[Fact]` for success, missing required param (expects `ArgumentException`), and timeout behavior; note on `[Fact(Skip = "requires external binary")]`.

### Req 12 (N2): Integration Tests

**`tests/McpManagerTests.cs` additions:**

```csharp
[Fact(Skip = "requires npx")]
public async Task McpManager_RealServer_ToolsListReturnsSchema() { ... }

[Fact(Skip = "requires npx")]
public async Task McpManager_RealServer_CallToolReturnsContent() { ... }

[Fact]
public async Task McpManager_SessionExit_TriggersRestart() { ... }
```

**`tests/Tools/BashToolTests.cs` additions:**

```csharp
[Fact]
public void RunShell_NoStdin_ClosesStdinImmediately() { ... }

[Fact]
public void RunShell_WithStdin_CommandReceivesInput() { ... }
```

---

## Data Models

### `McpSession` (new, internal)

```csharp
internal sealed class McpSession(string serverName, Process process) : IAsyncDisposable
{
    public string ServerName { get; } = serverName;
    public Process Process { get; } = process;
    public StreamWriter Stdin { get; } = process.StandardInput;
    public StreamReader Stdout { get; } = process.StandardOutput;
    private int _nextId = 1;
    public int NextId() => Interlocked.Increment(ref _nextId);
    public bool IsAlive => !Process.HasExited;

    public async ValueTask DisposeAsync()
    {
        try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); } catch { }
        await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        Process.Dispose();
    }
}
```

### `AppConfig` (updated)

```csharp
public class AppConfig
{
    [JsonPropertyName("provider")]    public string Provider { get; set; } = "OpenRouter";
    [JsonPropertyName("model")]       public string Model { get; set; } = "z-ai/glm-4.5-air:free";
    [JsonPropertyName("base_url")]    public string BaseUrl { get; set; } = "";
    [JsonPropertyName("theme")]       public string Theme { get; set; } = "default";

    // S2: encrypted storage
    [JsonPropertyName("api_key_encrypted")] public string ApiKeyEncrypted { get; set; } = "";
    [JsonPropertyName("config_version")]    public int ConfigVersion { get; set; } = 1;

    // Runtime only — never serialized
    [JsonIgnore] public string ApiKey { get; set; } = "";

    [JsonIgnore]
    public LlmProvider ProviderEnum =>
        Enum.TryParse<LlmProvider>(Provider, ignoreCase: true, out var p) ? p : LlmProvider.Custom;
}
```

**Migration note:** The old `api_key` JSON field is no longer written. On first load of an old config, `ConfigVersion` will be 1 and `ApiKeyEncrypted` will be empty; `Load()` falls back to reading the legacy `api_key` field via a secondary deserialization pass, then re-saves with encryption.

### `CodeChunk` (new)

```csharp
public sealed class CodeChunk
{
    public string FilePath { get; init; } = "";
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public string Text { get; init; } = "";
    public float[] Embedding { get; set; } = [];
}
```

### `DiagnosticMessage` (new)

```csharp
public sealed class DiagnosticMessage
{
    [JsonPropertyName("file")]     public string File { get; init; } = "";
    [JsonPropertyName("line")]     public int Line { get; init; }
    [JsonPropertyName("column")]   public int Column { get; init; }
    [JsonPropertyName("severity")] public string Severity { get; init; } = "error";
    [JsonPropertyName("code")]     public string Code { get; init; } = "";
    [JsonPropertyName("message")]  public string Message { get; init; } = "";
}
```

### `DiagnosticResult` (new)

```csharp
public sealed class DiagnosticResult
{
    [JsonPropertyName("exit_code")]   public int ExitCode { get; init; }
    [JsonPropertyName("diagnostics")] public List<DiagnosticMessage> Diagnostics { get; init; } = [];
}
```

---

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

This feature uses FsCheck.Xunit (already in `tests/BSE_Code.Tests.csproj`) for property-based testing.

**Property Reflection:** After analyzing all acceptance criteria, several properties were consolidated:
- Req 2.2 and 2.8 both describe the same edit round-trip — merged into Property 2.
- Req 10.1 and 10.2 both describe blocklist denial — merged into Property 8.
- Req 9.2 and 9.3 both describe compaction invariants — kept separate as they test different aspects.

### Property 1: McpManager session count matches enabled server count

*For any* valid MCP config with N enabled servers and M disabled servers, after `LoadAsync()`, the number of active sessions SHALL equal N (not N+M).

**Validates: Requirements 1.1, 1.7**

### Property 2: EditFileTool edit round-trip

*For any* file content string and any substring that appears exactly once in that content, applying `EditFileTool` with that substring as `old_str` and any replacement as `new_str`, then reading the file, SHALL produce content where `old_str` is absent and `new_str` is present exactly once.

**Validates: Requirements 2.2, 2.8**

### Property 3: EditFileTool confirmation contains file path

*For any* valid edit operation (file exists, `old_str` appears exactly once), the returned confirmation string SHALL contain the `file_path` that was passed as input.

**Validates: Requirements 2.6**

### Property 4: ApiKey encryption round-trip

*For any* non-empty string used as an `ApiKey`, calling `EncryptApiKey` then `DecryptApiKey` SHALL produce the original string unchanged.

**Validates: Requirements 3.4, 3.7**

### Property 5: Encrypted config never contains plaintext ApiKey

*For any* non-empty `ApiKey` string, after `ConfigManager.Save()`, reading the raw JSON from disk SHALL NOT contain the original `ApiKey` string as a substring.

**Validates: Requirements 3.1**

### Property 6: SemanticSearchTool result count bounded by top_n

*For any* query string and any value of `top_n`, the number of results returned by `SemanticSearchTool` SHALL be less than or equal to `top_n`.

**Validates: Requirements 4.3**

### Property 7: SemanticSearchTool path restriction

*For any* query and any `path` restriction, all results returned by `SemanticSearchTool` SHALL have a `FilePath` that starts with the specified `path`.

**Validates: Requirements 4.4**

### Property 8: DiagnosticTool result is always valid JSON with required fields

*For any* shell command, `DiagnosticTool.ExecuteAsync()` SHALL return a string that deserializes to a `DiagnosticResult` with a non-negative `ExitCode` and a non-null `Diagnostics` list.

**Validates: Requirements 5.3**

### Property 9: MSBuild diagnostic parsing round-trip

*For any* string matching the MSBuild diagnostic format `path(line,col): severity code: message`, the `DiagnosticTool` parser SHALL produce a `DiagnosticMessage` where `File`, `Line`, `Column`, `Severity`, `Code`, and `Message` fields are non-empty and match the input string.

**Validates: Requirements 5.4**

### Property 10: MarkdownRenderer plain-text fallback contains no ANSI sequences

*For any* markdown string, when `NO_COLOR` environment variable is set or output is redirected, `MarkdownRenderer.Render()` SHALL produce output containing no ANSI escape sequences (no substrings matching `\x1b\[`).

**Validates: Requirements 6.5**

### Property 11: Parallel tool dispatch preserves result order

*For any* list of N tool calls with known results, after parallel execution via `Task.WhenAll()`, the collected results SHALL appear in the same order as the original tool call list (index 0 result first, index N-1 result last).

**Validates: Requirements 7.2**

### Property 12: Parallel tool dispatch tolerates individual failures

*For any* batch of tool calls where a subset throws exceptions, `RunTurnAsync()` SHALL collect results for ALL tool calls (failed ones as error strings, successful ones as normal output) without propagating any exception to the caller.

**Validates: Requirements 7.4**

### Property 13: Same-file tool calls are serialized

*For any* two tool calls targeting the same file path, executing them concurrently SHALL produce the same result as executing them sequentially (no interleaving of file reads/writes).

**Validates: Requirements 7.6**

### Property 14: BashTool stdin is received by command

*For any* non-empty string passed as the `stdin` parameter, a command that reads from stdin and echoes it SHALL return output containing that string.

**Validates: Requirements 8.1**

### Property 15: BashTool without stdin closes immediately (no hang)

*For any* command that reads from stdin until EOF, invoking `BashTool` without a `stdin` parameter SHALL cause the command to receive EOF and exit within the configured timeout.

**Validates: Requirements 8.2**

### Property 16: Blocklisted commands are always denied

*For any* command string that contains a blocklist pattern (e.g., `rm -rf /`, `mkfs`, `dd if=`), `BashTool.ExecuteAsync()` SHALL return an error string without executing the command, regardless of the `BSE_BASH_CONFIRM` environment variable.

**Validates: Requirements 10.1, 10.2**

### Property 17: BSE_BASH_CONFIRM=off skips confirmation for non-blocklisted commands

*For any* command that is not on the blocklist, when `BSE_BASH_CONFIRM=off` is set, `BashTool.ExecuteAsync()` SHALL execute the command without requesting user confirmation.

**Validates: Requirements 10.7**

### Property 18: All executed commands appear in audit log

*For any* command that is executed by `BashTool` (not blocked, confirmed or auto-approved), the command string SHALL appear as a line in `~/.bse-code/audit.log` after execution.

**Validates: Requirements 10.8**

### Property 19: Token estimation is non-negative

*For any* list of `ChatMessage` objects (including empty list), `EstimateTokens()` SHALL return a value greater than or equal to zero.

**Validates: Requirements 9.1**

### Property 20: Compaction reduces token count below budget

*For any* message list where `EstimateTokens()` exceeds `DefaultTokenBudget`, after running the compaction pruning logic (excluding the LLM summarization step), `EstimateTokens()` on the pruned list SHALL be less than or equal to `DefaultTokenBudget`.

**Validates: Requirements 9.2**

### Property 21: Compaction preserves system messages and last 4 pairs

*For any* message list containing at least one `SystemChatMessage` and at least 8 non-system messages, after compaction, ALL `SystemChatMessage` entries SHALL be present and the last 4 user/assistant pairs SHALL be present.

**Validates: Requirements 9.3**

---

## Error Handling

Consistent error handling patterns across all new components:

### Tool Handlers (T2, T3, T4, T5)

- Missing required parameters: throw `ArgumentException("'param_name' is required.")` — consistent with existing tools.
- File not found / IO errors: return error string (do not throw) — consistent with existing `ReadFileTool`.
- External service failures (embedding API, shell timeout): return error string with descriptive message.
- Never propagate exceptions to `ReplEngine` — all exceptions are caught in `ToolRegistry.ExecuteAsync()` and returned as error strings.

### McpManager (T1)

- Session spawn failure: log warning via `UI.Warn()`, skip server (do not crash).
- Session unexpected exit: log warning, attempt restart up to 3 times, then mark unavailable.
- Request timeout: return `"ERROR: No response from MCP server."` (existing behavior preserved).
- `DisposeAsync()`: best-effort cleanup — catch all exceptions during process termination.

### ConfigManager (S2)

- Decryption failure: `UI.Warn()` + return `AppConfig` with empty `ApiKey` (user prompted to re-run setup).
- Missing `api_key_encrypted` field in v2 config: treat as empty key.
- Legacy v1 config: read `api_key` field directly, re-save as v2 on next `Save()`.

### BashTool (S1, T4)

- Blocked command: return `"ERROR: Command blocked: {pattern matched}"` without executing.
- User denial: return `"ERROR: Command denied by user."`.
- Audit log write failure: silently ignore (best-effort logging).

### MarkdownRenderer (T6)

- Spectre.Console markup errors (malformed markdown): fall back to plain text output for that segment.
- `Console.IsOutputRedirected` check: always evaluated at render time, not cached.

---

## Testing Strategy

### Unit Tests

Each new component gets a dedicated test file:

| Component | Test File |
|---|---|
| EditFileTool | `tests/Tools/EditFileToolTests.cs` |
| ConfigManager (S2) | `tests/ConfigManagerEncryptionTests.cs` |
| SemanticSearchTool | `tests/Tools/SemanticSearchToolTests.cs` |
| DiagnosticTool | `tests/Tools/DiagnosticToolTests.cs` |
| MarkdownRenderer | `tests/MarkdownRendererTests.cs` |
| BashTool (T4+S1) | `tests/Tools/BashToolTests.cs` (extend existing) |
| SlashCommandHandler (P2) | `tests/SlashCommandHandlerCompactTests.cs` |
| McpManager (T1) | `tests/McpManagerTests.cs` (extend existing) |

Unit tests focus on:
- Specific examples demonstrating correct behavior
- Error conditions (missing params, file not found, etc.)
- Edge cases (empty input, boundary values)

### Property-Based Tests

Using FsCheck.Xunit with `[Property(MaxTest = 100)]` (minimum 100 iterations per property).

Each property test is tagged with a comment:

```csharp
// Feature: bse-code-improvements, Property N: <property text>
[Property(MaxTest = 100)]
public Property PropertyName() { ... }
```

**Properties by component:**

| Property | Component | FsCheck Generator |
|---|---|---|
| 1 (session count) | McpManager | `Gen.ListOf(Arb.Generate<bool>())` for enabled flags |
| 2 (edit round-trip) | EditFileTool | `NonEmptyString` for content and old_str |
| 3 (confirmation contains path) | EditFileTool | `NonEmptyString` for file path |
| 4 (ApiKey round-trip) | ConfigManager | `NonEmptyString` for ApiKey |
| 5 (no plaintext in JSON) | ConfigManager | `NonEmptyString` for ApiKey |
| 6 (result count bounded) | SemanticSearchTool | `PositiveInt` for top_n |
| 7 (path restriction) | SemanticSearchTool | `NonEmptyString` for path |
| 8 (valid JSON result) | DiagnosticTool | `NonEmptyString` for command |
| 9 (MSBuild parse round-trip) | DiagnosticTool | Custom generator for MSBuild strings |
| 10 (no ANSI in plain mode) | MarkdownRenderer | `NonEmptyString` for markdown |
| 11 (result order) | ReplEngine | `Gen.ListOf` for tool calls |
| 12 (tolerates failures) | ReplEngine | Mixed failing/succeeding tools |
| 13 (same-file serialization) | ReplEngine | Shared file path, concurrent writes |
| 14 (stdin received) | BashTool | `NonEmptyString` for stdin |
| 15 (EOF without stdin) | BashTool | Commands that read stdin |
| 16 (blocklist denial) | BashTool | Blocklist pattern + random suffix |
| 17 (BSE_BASH_CONFIRM=off) | BashTool | Non-blocklisted commands |
| 18 (audit log) | BashTool | `NonEmptyString` for command |
| 19 (token estimation >= 0) | SlashCommandHandler | `Gen.ListOf<ChatMessage>` |
| 20 (compaction reduces tokens) | SlashCommandHandler | Large message lists |
| 21 (preserves system + last 4) | SlashCommandHandler | Message lists with system messages |

### Integration Tests

Tests requiring external processes use `[Fact(Skip = "requires npx")]` or `[Fact(Skip = "requires external binary")]`:

```csharp
[Fact(Skip = "requires npx")]
public async Task McpManager_RealEchoServer_ToolsListReturnsSchema() { ... }

[Fact(Skip = "requires npx")]
public async Task McpManager_RealEchoServer_CallToolReturnsContent() { ... }
```

Tests that can run without external binaries (using mock processes or in-process echo servers) use plain `[Fact]`:

```csharp
[Fact]
public async Task McpManager_SessionExit_TriggersRestartAttempt() { ... }

[Fact]
public void BashTool_NoStdin_CommandReceivesEof() { ... }

[Fact]
public void BashTool_WithStdin_CommandReceivesInput() { ... }
```

### Test Configuration

`tests/xunit.runner.json` — no changes needed; existing sequential collection attribute handles McpManager state isolation.

Property tests that involve file I/O use `Path.GetTempPath()` for isolation and clean up in `finally` blocks (pattern already established in `McpManagerTests.cs`).
