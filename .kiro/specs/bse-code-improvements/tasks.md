# Implementation Plan: bse-code Improvements

## Overview

Implement 12 improvements to bse-code across four categories: technical gaps (T1–T6), performance (P1–P2), security (S1–S2), and documentation/testing (N1–N2). Tasks follow the phased order from the design: Phase 1 first, then Phase 2, Phase 3, then Remaining.

## Tasks

<!-- ═══════════════════════════════════════════════════════════════════ -->
<!-- PHASE 1 (Immediate): T1, T2, S2                                   -->
<!-- ═══════════════════════════════════════════════════════════════════ -->

- [ ] 1. T1 — Implement McpSession class and persistent MCP sessions
  - Add `internal sealed class McpSession : IAsyncDisposable` to `src/McpManager.cs`
  - Fields: `ServerName`, `Process`, `Stdin` (StreamWriter), `Stdout` (StreamReader), `_nextId` (int with `Interlocked.Increment`)
  - Properties: `IsAlive => !Process.HasExited`, `NextId()`
  - `DisposeAsync()`: kill process tree (best-effort), await `WaitForExitAsync` with 2s timeout, dispose process
  - _Requirements: 1.1, 1.6_

  - [ ] 1.1 Refactor McpManager to use persistent sessions
    - Replace `_activeServers: Dictionary<string, McpServerConfig>` with `_sessions: Dictionary<string, McpSession>`
    - Add `_restartCounts: Dictionary<string, int>` and `_unavailable: HashSet<string>`
    - Add `SpawnSessionAsync(name, config) → McpSession`: start process, send `initialize`, read response, send `notifications/initialized`
    - Add `EnsureSessionAliveAsync(serverName)`: check `IsAlive`, restart up to 3 times, mark unavailable on 3rd failure
    - Refactor `SendMcpRequestAsync` to accept `McpSession` instead of spawning a new process
    - Update `LoadAsync()`: terminate existing sessions first, then spawn new ones via `SpawnSessionAsync`
    - Update `CallToolAsync()`: look up session via `EnsureSessionAliveAsync`, call `SendMcpRequestAsync(session, ...)`
    - Add `static async ValueTask DisposeAsync()`: iterate `_sessions`, dispose each
    - Update `DiscoverToolsAsync` to accept `McpSession` instead of `McpServerConfig`
    - Keep `Servers` property returning server names from `_sessions` keys
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7_

  - [ ] 1.2 Wire McpManager.DisposeAsync() into Program.cs
    - Call `await McpManager.DisposeAsync()` before application exit in `Program.cs`
    - _Requirements: 1.5, 1.6_

  - [ ] 1.3 Write property test for McpManager session count (Property 1)
    - **Property 1: McpManager session count matches enabled server count**
    - For any valid MCP config with N enabled and M disabled servers, after `LoadAsync()`, active session count SHALL equal N
    - Use a temp mcp.json with a mix of enabled/disabled entries pointing to a fast-exit command
    - **Validates: Requirements 1.1, 1.7**
    - Add to `tests/McpManagerTests.cs`

- [ ] 2. T2 — Implement EditFileTool
  - Create `src/Tools/EditFileTool.cs` implementing `IToolHandler`
  - `Name = "edit_file"`, required params: `file_path`, `old_str`, `new_str`
  - Logic: read file → count occurrences of `old_str` → 0: return "not found" error, >1: return "ambiguous" error, 1: replace first occurrence → write back
  - Lines-changed = `Math.Abs(newStr.Count('\n') - oldStr.Count('\n')) + 1`
  - Return confirmation string containing `file_path` and lines-changed count
  - Register `new EditFileTool()` in `ToolRegistry.CreateDefault()`
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7_

  - [ ] 2.1 Write property test for EditFileTool edit round-trip (Property 2)
    - **Property 2: EditFileTool edit round-trip**
    - For any file content and any substring appearing exactly once, applying `EditFileTool` then reading the file SHALL produce content where `old_str` is absent and `new_str` is present exactly once
    - Use temp files; clean up in `finally`
    - **Validates: Requirements 2.2, 2.8**
    - Create `tests/Tools/EditFileToolTests.cs`

  - [ ] 2.2 Write property test for EditFileTool confirmation contains path (Property 3)
    - **Property 3: EditFileTool confirmation contains file path**
    - For any valid edit operation, the returned confirmation string SHALL contain the `file_path` passed as input
    - **Validates: Requirements 2.6**
    - Add to `tests/Tools/EditFileToolTests.cs`

- [ ] 3. S2 — Implement encrypted API key storage
  - Update `AppConfig` in `src/ConfigManager.cs`:
    - Remove `[JsonPropertyName("api_key")] public string ApiKey` (serialized field)
    - Add `[JsonPropertyName("api_key_encrypted")] public string ApiKeyEncrypted { get; set; } = ""`
    - Add `[JsonPropertyName("config_version")] public int ConfigVersion { get; set; } = 1`
    - Add `[JsonIgnore] public string ApiKey { get; set; } = ""` (runtime-only)
  - Add platform-conditional NuGet to `src/BSE_Code.Core.csproj`:
    ```xml
    <ItemGroup Condition="'$(OS)' == 'Windows_NT'">
      <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="9.*" />
    </ItemGroup>
    ```
  - Add private helpers to `ConfigManager`: `EncryptApiKey`, `DecryptApiKey`, `EncryptWindows`, `DecryptWindows`, `EncryptAesGcm`, `DecryptAesGcm`, `DeriveKey` (SHA-256 of `MachineName`)
  - Update `Save()`: encrypt `ApiKey` → store in `ApiKeyEncrypted`, set `ConfigVersion = 2`
  - Update `Load()`: if `ConfigVersion == 2` decrypt `ApiKeyEncrypted`; else read legacy `api_key` field via secondary deserialization pass, re-save as v2
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.8_

  - [ ] 3.1 Write property test for ApiKey encryption round-trip (Property 4)
    - **Property 4: ApiKey encryption round-trip**
    - For any non-empty string used as `ApiKey`, `EncryptApiKey` then `DecryptApiKey` SHALL produce the original string unchanged
    - Use `InternalsVisibleTo` to access private helpers via `internal` visibility
    - **Validates: Requirements 3.4, 3.7**
    - Create `tests/ConfigManagerEncryptionTests.cs`

  - [ ] 3.2 Write property test for no plaintext ApiKey in JSON (Property 5)
    - **Property 5: Encrypted config never contains plaintext ApiKey**
    - For any non-empty `ApiKey`, after `ConfigManager.Save()`, the raw JSON on disk SHALL NOT contain the original `ApiKey` as a substring
    - Use temp config path; clean up in `finally`
    - **Validates: Requirements 3.1**
    - Add to `tests/ConfigManagerEncryptionTests.cs`

- [ ] 4. Checkpoint — Phase 1 complete
  - Ensure all tests pass, ask the user if questions arise.

<!-- ═══════════════════════════════════════════════════════════════════ -->
<!-- PHASE 2 (Short-Term): T3, T5                                      -->
<!-- ═══════════════════════════════════════════════════════════════════ -->

- [ ] 5. T3 — Implement SemanticSearchTool
  - Create `src/Tools/SemanticSearchTool.cs` implementing `IToolHandler`
  - Constructor takes `AppConfig` (for `ApiKey` + `BaseUrl` to build `EmbeddingClient`)
  - `Name = "semantic_search"`, required: `query`; optional: `path`, `top_n` (default 10)
  - Add `CodeChunk` record: `FilePath`, `StartLine`, `EndLine`, `Text`, `Embedding` (float[])
  - Static fields: `_index: List<CodeChunk>`, `_fileTimestamps: Dictionary<string, DateTime>`, `_indexLock: SemaphoreSlim(1,1)`
  - `ChunkFile(filePath)`: ~200-line segments with 20-line overlap
  - `BuildOrRefreshIndexAsync(rootPath, EmbeddingClient)`: check timestamps, re-embed changed files
  - `CosineSimilarity(float[], float[])`: dot product / (|a| * |b|)
  - Change `ToolRegistry.CreateDefault()` to `CreateDefault(AppConfig config)` and update the call site in `Program.cs`
  - Register `new SemanticSearchTool(config)` in `ToolRegistry.CreateDefault(AppConfig config)`
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8_

  - [ ] 5.1 Write property test for SemanticSearchTool result count bounded (Property 6)
    - **Property 6: SemanticSearchTool result count bounded by top_n**
    - For any query and any value of `top_n`, the number of results returned SHALL be ≤ `top_n`
    - Use a pre-built in-memory index stub to avoid real embedding calls
    - **Validates: Requirements 4.3**
    - Create `tests/Tools/SemanticSearchToolTests.cs`

  - [ ] 5.2 Write property test for SemanticSearchTool path restriction (Property 7)
    - **Property 7: SemanticSearchTool path restriction**
    - For any query and any `path` restriction, all results SHALL have `FilePath` starting with the specified `path`
    - **Validates: Requirements 4.4**
    - Add to `tests/Tools/SemanticSearchToolTests.cs`

- [ ] 6. T5 — Implement DiagnosticTool
  - Create `src/Tools/DiagnosticTool.cs` implementing `IToolHandler`
  - `Name = "diagnostic"`, required: `command`; optional: `timeout_seconds` (default 60)
  - Add `DiagnosticMessage` record: `File`, `Line`, `Column`, `Severity`, `Code`, `Message` (all with `[JsonPropertyName]`)
  - Add `DiagnosticResult` record: `ExitCode`, `Diagnostics: List<DiagnosticMessage>` (with `[JsonPropertyName]`)
  - MSBuild regex: `^(.+)\((\d+),(\d+)\):\s+(error|warning|info)\s+(\w+):\s+(.+)$`
  - ESLint JSON: try `JsonSerializer.Deserialize` of output as ESLint JSON array
  - Reuse `BashTool.RunShell(command, timeout)` for execution
  - If no parseable diagnostics and non-zero exit: return raw output as single `DiagnosticMessage` with severity "error"
  - Return `JsonSerializer.Serialize(result)`
  - Register `new DiagnosticTool()` in `ToolRegistry.CreateDefault(AppConfig config)`
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8_

  - [ ] 6.1 Write property test for DiagnosticTool valid JSON result (Property 8)
    - **Property 8: DiagnosticTool result is always valid JSON with required fields**
    - For any shell command, `DiagnosticTool.ExecuteAsync()` SHALL return a string deserializable to `DiagnosticResult` with non-negative `ExitCode` and non-null `Diagnostics`
    - **Validates: Requirements 5.3**
    - Create `tests/Tools/DiagnosticToolTests.cs`

  - [ ] 6.2 Write property test for MSBuild diagnostic parsing round-trip (Property 9)
    - **Property 9: MSBuild diagnostic parsing round-trip**
    - For any string matching the MSBuild format `path(line,col): severity code: message`, the parser SHALL produce a `DiagnosticMessage` where all fields are non-empty and match the input
    - Use a custom FsCheck generator for valid MSBuild diagnostic strings
    - **Validates: Requirements 5.4**
    - Add to `tests/Tools/DiagnosticToolTests.cs`

- [ ] 7. Checkpoint — Phase 2 complete
  - Ensure all tests pass, ask the user if questions arise.

<!-- ═══════════════════════════════════════════════════════════════════ -->
<!-- PHASE 3 (Long-Term): T6, P1                                       -->
<!-- ═══════════════════════════════════════════════════════════════════ -->

- [ ] 8. T6 — Implement rich text markdown rendering
  - Add `<PackageReference Include="Spectre.Console" Version="0.49.*" />` to `src/BSE_Code.Core.csproj`
  - Create `src/MarkdownRenderer.cs` (static class)
  - `IsPlainText` property: `NO_COLOR` env var set OR `Console.IsOutputRedirected`
  - `Render(string markdown)`: if plain text → `Console.Write(markdown)`; else use `AnsiConsole.Write(new Markup(EscapeAndConvert(markdown)))`
  - `EscapeAndConvert(string markdown)`: escape Spectre markup special chars, convert markdown to Spectre markup syntax
  - On Spectre markup error: fall back to plain text for that segment
  - Update `ReplEngine.RunTurnAsync()`: after stream ends, call `MarkdownRenderer.Render(contentBuilder.ToString())` instead of writing tokens directly during streaming; remove per-token `Console.Write` calls for content
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7_

  - [ ] 8.1 Write property test for MarkdownRenderer plain-text fallback (Property 10)
    - **Property 10: MarkdownRenderer plain-text fallback contains no ANSI sequences**
    - For any markdown string, when `NO_COLOR` is set or output is redirected, `MarkdownRenderer.Render()` SHALL produce output with no ANSI escape sequences (no `\x1b[` substrings)
    - Capture `Console.Out` to a `StringWriter` during the test
    - **Validates: Requirements 6.5**
    - Create `tests/MarkdownRendererTests.cs`

- [ ] 9. P1 — Implement parallel tool dispatch
  - Update `ReplEngine.RunTurnAsync()` in `src/ReplEngine.cs`:
    - Add `ConcurrentDictionary<string, SemaphoreSlim>` for per-file-path serialization
    - Add `ExtractFilePath(toolName, argsJson)`: returns `file_path` for `read_file`, `Write`, `edit_file`; null otherwise
    - Replace sequential `foreach` tool dispatch with `Task.WhenAll()` over tasks that acquire per-file semaphores
    - Same file path → sequential (via `SemaphoreSlim`); different paths → concurrent
    - Collect results maintaining original index order (`Task.WhenAll` preserves order)
    - Capture exceptions per-task as error strings; do not let one failure abort others
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6_

  - [ ] 9.1 Write property test for parallel dispatch result order (Property 11)
    - **Property 11: Parallel tool dispatch preserves result order**
    - For any list of N tool calls with known results, after `Task.WhenAll()`, results SHALL appear in the same order as the original tool call list
    - **Validates: Requirements 7.2**
    - Create `tests/ReplEngineParallelTests.cs`

  - [ ] 9.2 Write property test for parallel dispatch tolerates failures (Property 12)
    - **Property 12: Parallel tool dispatch tolerates individual failures**
    - For any batch where a subset throws exceptions, `RunTurnAsync()` SHALL collect results for ALL tool calls (failed ones as error strings)
    - **Validates: Requirements 7.4**
    - Add to `tests/ReplEngineParallelTests.cs`

  - [ ] 9.3 Write property test for same-file serialization (Property 13)
    - **Property 13: Same-file tool calls are serialized**
    - For any two tool calls targeting the same file path, concurrent execution SHALL produce the same result as sequential execution
    - **Validates: Requirements 7.6**
    - Add to `tests/ReplEngineParallelTests.cs`

- [ ] 10. Checkpoint — Phase 3 complete
  - Ensure all tests pass, ask the user if questions arise.

<!-- ═══════════════════════════════════════════════════════════════════ -->
<!-- REMAINING: T4, P2, S1, N1, N2                                     -->
<!-- ═══════════════════════════════════════════════════════════════════ -->

- [ ] 11. T4 — Add stdin support to BashTool
  - Update `BashTool.RunShell()` signature: `internal static string RunShell(string command, TimeSpan? timeout = null, string? stdin = null)`
  - Process setup: always set `RedirectStandardInput = true`
  - After `process.Start()`: if `stdin` is not null, write it to `process.StandardInput`; always call `process.StandardInput.Close()` to send EOF
  - Update `ExecuteAsync()`: parse optional `stdin` from args, pass to `RunShell`
  - Update `ParameterSchema` to include `stdin` as optional string parameter
  - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_

  - [ ] 11.1 Write property test for BashTool stdin received (Property 14)
    - **Property 14: BashTool stdin is received by command**
    - For any non-empty string passed as `stdin`, a command that reads from stdin and echoes it SHALL return output containing that string
    - **Validates: Requirements 8.1**
    - Add to `tests/Tools/BashToolTests.cs`

  - [ ] 11.2 Write property test for BashTool EOF without stdin (Property 15)
    - **Property 15: BashTool without stdin closes immediately (no hang)**
    - For any command that reads from stdin until EOF, invoking `BashTool` without `stdin` SHALL cause the command to receive EOF and exit within the configured timeout
    - **Validates: Requirements 8.2**
    - Add to `tests/Tools/BashToolTests.cs`

- [ ] 12. P2 — Implement token-aware context compaction
  - Update `SlashCommandHandler.HandleCompactAsync()` in `src/SlashCommandHandler.cs`:
    - Add `private const int DefaultTokenBudget = 80_000`
    - Add `private static int EstimateTokens(IEnumerable<ChatMessage> messages)`: sum char lengths / 4
    - Add `private static string GetMessageText(ChatMessage m)`: pattern match on `UserChatMessage`, `AssistantChatMessage`, `SystemChatMessage`
    - New flow: estimate tokens → if below budget, inform user and return; else prune oldest non-system messages keeping last 8 (4 pairs); run LLM summarization; display before/after counts
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7_

  - [ ] 12.1 Write property test for token estimation non-negative (Property 19)
    - **Property 19: Token estimation is non-negative**
    - For any list of `ChatMessage` objects (including empty list), `EstimateTokens()` SHALL return a value ≥ 0
    - **Validates: Requirements 9.1**
    - Create `tests/SlashCommandHandlerCompactTests.cs`

  - [ ] 12.2 Write property test for compaction reduces below budget (Property 20)
    - **Property 20: Compaction reduces token count below budget**
    - For any message list where `EstimateTokens()` exceeds `DefaultTokenBudget`, after pruning (excluding LLM summarization), `EstimateTokens()` on the pruned list SHALL be ≤ `DefaultTokenBudget`
    - **Validates: Requirements 9.2**
    - Add to `tests/SlashCommandHandlerCompactTests.cs`

  - [ ] 12.3 Write property test for compaction preserves system + last 4 pairs (Property 21)
    - **Property 21: Compaction preserves system messages and last 4 pairs**
    - For any message list with ≥1 `SystemChatMessage` and ≥8 non-system messages, after compaction ALL `SystemChatMessage` entries SHALL be present and the last 4 user/assistant pairs SHALL be present
    - **Validates: Requirements 9.3**
    - Add to `tests/SlashCommandHandlerCompactTests.cs`

- [ ] 13. S1 — Implement shell command safeguards
  - Update `src/Tools/BashTool.cs`:
    - Add `private static readonly string[] Blocklist` with patterns: `rm -rf /`, `rm -rf ~`, `format c:`, `mkfs`, `dd if=`, `:(){:|:&};:`, `del /f /s /q c:\`
    - Add `private static readonly string[] Allowlist` with patterns: `echo `, `cat `, `ls`, `dir`, `git status`, `git log`, `git diff`, `pwd`, `type `, `dotnet build`, `dotnet test`, `dotnet run`, `grep `, `find `
    - Add `private static bool IsBlocked(string cmd)`: any blocklist pattern contained (case-insensitive)
    - Add `private static bool IsAllowed(string cmd)`: any allowlist pattern starts/contains (case-insensitive)
    - Add `private static readonly string AuditLog` path: `~/.bse-code/audit.log`
    - Update `ExecuteAsync()`: check blocklist → check allowlist → prompt confirmation (unless `BSE_BASH_CONFIRM=off`) → execute → append to audit log
    - Audit log format: `{timestamp} | exit:{code} | {command}\n`
  - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8_

  - [ ] 13.1 Write property test for blocklist denial (Property 16)
    - **Property 16: Blocklisted commands are always denied**
    - For any command string containing a blocklist pattern, `BashTool.ExecuteAsync()` SHALL return an error string without executing, regardless of `BSE_BASH_CONFIRM`
    - **Validates: Requirements 10.1, 10.2**
    - Add to `tests/Tools/BashToolTests.cs`

  - [ ] 13.2 Write property test for BSE_BASH_CONFIRM=off skips confirmation (Property 17)
    - **Property 17: BSE_BASH_CONFIRM=off skips confirmation for non-blocklisted commands**
    - For any non-blocklisted command, when `BSE_BASH_CONFIRM=off`, `BashTool.ExecuteAsync()` SHALL execute without requesting user confirmation
    - **Validates: Requirements 10.7**
    - Add to `tests/Tools/BashToolTests.cs`

  - [ ] 13.3 Write property test for audit log (Property 18)
    - **Property 18: All executed commands appear in audit log**
    - For any command that is executed (not blocked, confirmed or auto-approved), the command string SHALL appear in `~/.bse-code/audit.log` after execution
    - Use a temp audit log path; restore original after test
    - **Validates: Requirements 10.8**
    - Add to `tests/Tools/BashToolTests.cs`

- [ ] 14. N1 — Expand CONTRIBUTING.md with extensibility documentation
  - Add "Adding a New Tool" section to `CONTRIBUTING.md` with complete annotated `IToolHandler` example including `Name`, `Description`, `ParameterSchema`, `ExecuteAsync`, `ArgumentParser.ParseStringMap()` usage, error handling, and registration in `ToolRegistry.CreateDefault()`
  - Add "Adding a New LLM Provider" section: steps for `LlmProvider` enum, `ProviderDef` array entry, `FallbackModels` entry, `NeedsApiKey` and `DefaultBaseUrl` explanation
  - Add "Configuring MCP Servers" section: complete `mcp.json` example with `filesystem` and `git` servers, field-by-field explanation, `disabled` flag, `env` for secrets
  - Add "Tool Naming Conventions" section: `mcp__serverName__toolName` scheme, `IToolHandler.Name` constraints, case-insensitive dispatch
  - Add "Testing New Tools" section: example xunit test class with `[Fact]` for success, missing required param (`ArgumentException`), timeout; note on `[Fact(Skip = "requires external binary")]`
  - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

- [ ] 15. N2 — Add MCP lifecycle and BashTool stdin integration tests
  - Add to `tests/McpManagerTests.cs`:
    - `[Fact(Skip = "requires npx")] McpManager_RealServer_ToolsListReturnsSchema()`: start real MCP server, call `tools/list`, verify returned tool names match expected schema
    - `[Fact(Skip = "requires npx")] McpManager_RealServer_CallToolReturnsContent()`: call `McpManager.CallToolAsync()` against real server, verify response is non-empty and well-formed
    - `[Fact] McpManager_SessionExit_TriggersRestart()`: verify `McpManager` detects session exit and attempts restart (per Requirement 1.3)
  - Add to `tests/Tools/BashToolTests.cs`:
    - `[Fact] RunShell_NoStdin_ClosesStdinImmediately()`: verify stdin is closed immediately when no `stdin` param provided
    - `[Fact] RunShell_WithStdin_CommandReceivesInput()`: verify provided `stdin` string is written to process and command receives it
  - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5, 12.6_

- [ ] 16. Final checkpoint — Ensure all tests pass
  - Run `dotnet test` and verify all tests pass (including property-based tests)
  - Ensure all properties pass with `MaxTest = 100` iterations
  - Ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation at the end of each phase
- Property tests validate universal correctness properties (Properties 1–21 from design.md)
- Unit tests validate specific examples and edge cases
- Integration tests requiring external binaries use `[Fact(Skip = "requires npx")]`
- `ToolRegistry.CreateDefault()` becomes `CreateDefault(AppConfig config)` in Task 5 — update all call sites
