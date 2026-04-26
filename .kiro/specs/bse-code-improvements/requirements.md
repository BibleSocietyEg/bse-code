# Requirements Document

## Introduction

This document formalizes the improvement requirements for the bse-code project — a .NET CLI AI coding assistant. A comprehensive evaluation identified 12 confirmed deficiencies spanning technical gaps, performance and scalability issues, security vulnerabilities, and process/documentation shortfalls. These requirements address all 12 items, organized into three implementation phases aligned with the evaluation report's phased recommendations.

**Phase 1 (Immediate):** T1 — MCP persistent sessions, T2 — EditFileTool, S2 — encrypted config  
**Phase 2 (Short-Term):** T3 — semantic search, T5 — linter/diagnostic integration  
**Phase 3 (Long-Term):** T6 — rich text rendering, P1 — parallel tool dispatch  
**Remaining (prioritized separately):** T4, P2, S1, N1, N2

---

## Glossary

- **McpManager**: The static class in `src/McpManager.cs` responsible for loading MCP server configurations and dispatching tool calls.
- **MCP_Server**: An external process implementing the Model Context Protocol (stdio transport), configured in `~/.bse-code/mcp.json`.
- **MCP_Session**: A persistent, initialized connection to a single MCP_Server process, reused across multiple tool calls.
- **ReplEngine**: The class in `src/ReplEngine.cs` that drives the interactive REPL loop and executes conversation turns.
- **ToolRegistry**: The class in `src/Tools/ToolRegistry.cs` that registers and dispatches built-in tool handlers.
- **EditFileTool**: A new tool handler that applies targeted search-and-replace edits to files without full overwrites.
- **WriteFileTool**: The existing tool in `src/Tools/WriteFileTool.cs` that performs full file overwrites.
- **BashTool**: The existing tool in `src/Tools/BashTool.cs` that executes shell commands.
- **DiagnosticTool**: A new tool handler that runs build or lint commands and returns structured error output.
- **SemanticSearchTool**: A new tool handler that performs embedding-based vector search over the workspace.
- **ConfigManager**: The static class in `src/ConfigManager.cs` that loads, saves, and manages `~/.bse-code/config.json`.
- **AppConfig**: The configuration model class holding provider, API key, model, base URL, and theme.
- **SlashCommandHandler**: The class in `src/SlashCommandHandler.cs` that handles `/command` inputs in the REPL.
- **UI**: The static class in `src/UI.cs` providing console color helpers and print utilities.
- **Spectre_Console**: The [Spectre.Console](https://spectreconsole.net/) NuGet library for rich terminal rendering.
- **DPAPI**: The Windows Data Protection API (`System.Security.Cryptography.ProtectedData`) for OS-managed secret encryption.
- **Tool_Call**: A single invocation of a named tool by the LLM, carrying a tool name and JSON arguments.
- **Conversation_Turn**: One complete request-response cycle in the REPL, potentially involving multiple Tool_Calls.
- **BSE_md**: A `BSE.md` memory file loaded by `MemoryManager` to inject project context into the system prompt.
- **Compact_Command**: The `/compact` slash command in `SlashCommandHandler` that summarizes conversation history.
- **Token_Budget**: A configurable maximum number of tokens allowed in the active conversation context window.

---

## Requirements

---

### Requirement 1 (T1): Persistent MCP Server Sessions

**Priority:** Critical — Phase 1 (Immediate)  
**Affected file:** `src/McpManager.cs` — `SendMcpRequestAsync()`, `CallToolAsync()`, `DiscoverToolsAsync()`

**User Story:** As a developer using bse-code, I want MCP server processes to remain alive across multiple tool calls, so that tool invocations are fast and stateful MCP servers function correctly.

**Context:** `SendMcpRequestAsync()` currently spawns a new `Process`, sends `initialize`, sends the actual request, then immediately calls `process.Kill()`. Every tool call pays full process-startup and initialization overhead, and any server-side state is destroyed between calls.

#### Acceptance Criteria

1. WHEN `McpManager` is initialized, THE `McpManager` SHALL spawn one persistent `MCP_Session` process per enabled `MCP_Server` and keep it alive for the duration of the application session.
2. WHEN a `Tool_Call` targeting an `MCP_Server` is dispatched, THE `McpManager` SHALL reuse the existing `MCP_Session` process rather than spawning a new process.
3. WHEN an `MCP_Session` process exits unexpectedly, THE `McpManager` SHALL detect the exit, log a warning to the console, and attempt to restart the `MCP_Session` up to 3 times before marking the server as unavailable.
4. IF an `MCP_Session` restart attempt fails 3 consecutive times, THEN THE `McpManager` SHALL mark that server as unavailable and return an error string for any subsequent `Tool_Call` targeting it.
5. WHEN the application exits, THE `McpManager` SHALL gracefully terminate all active `MCP_Session` processes.
6. THE `McpManager` SHALL expose a `DisposeAsync()` method that terminates all `MCP_Session` processes and releases associated resources.
7. WHEN `McpManager.LoadAsync()` is called while active `MCP_Session` processes exist, THE `McpManager` SHALL terminate all existing sessions before starting new ones.

---

### Requirement 2 (T2): EditFileTool for Targeted File Edits

**Priority:** High — Phase 1 (Immediate)  
**Affected files:** `src/Tools/` (new file), `src/Tools/ToolRegistry.cs` — `CreateDefault()`

**User Story:** As a developer using bse-code, I want the AI to edit specific sections of a file using search-and-replace hunks, so that large files are modified safely without full overwrites and without wasting tokens.

**Context:** `ToolRegistry.CreateDefault()` only registers `WriteFileTool`, which overwrites the entire file. For large files this wastes tokens (the entire file must be re-sent) and risks data loss if the LLM produces a truncated response.

#### Acceptance Criteria

1. THE `ToolRegistry` SHALL register an `EditFileTool` alongside the existing `WriteFileTool`.
2. WHEN `EditFileTool` is invoked with a `file_path`, `old_str`, and `new_str`, THE `EditFileTool` SHALL replace the first exact occurrence of `old_str` in the file with `new_str` and write the result back to disk.
3. IF the specified `file_path` does not exist, THEN THE `EditFileTool` SHALL return an error string containing the path and the message "file not found".
4. IF `old_str` does not appear in the file, THEN THE `EditFileTool` SHALL return an error string indicating the search string was not found, without modifying the file.
5. IF `old_str` appears more than once in the file, THEN THE `EditFileTool` SHALL return an error string indicating the match is ambiguous, without modifying the file.
6. WHEN `EditFileTool` successfully applies an edit, THE `EditFileTool` SHALL return a confirmation string that includes the `file_path` and the number of lines changed.
7. THE `EditFileTool` parameter schema SHALL declare `file_path`, `old_str`, and `new_str` as required string parameters.
8. FOR ALL valid files and non-ambiguous `old_str` values, applying `EditFileTool` then reading the file SHALL produce content where `old_str` is replaced by `new_str` exactly once (round-trip correctness).

---

### Requirement 3 (S2): Encrypted API Key Storage

**Priority:** High — Phase 1 (Immediate)  
**Affected file:** `src/ConfigManager.cs` — `Save()`, `Load()`, `AppConfig`

**User Story:** As a developer using bse-code, I want my API keys stored with OS-level encryption, so that plaintext credentials are not exposed in `~/.bse-code/config.json`.

**Context:** `ConfigManager.Save()` serializes `AppConfig` (including `ApiKey`) as plain JSON. Any process or user with read access to `~/.bse-code/config.json` can extract the API key.

#### Acceptance Criteria

1. WHEN `ConfigManager` saves an `AppConfig` with a non-empty `ApiKey`, THE `ConfigManager` SHALL encrypt the `ApiKey` value using the platform's OS-managed secret store before writing it to `config.json`.
2. WHERE the platform is Windows, THE `ConfigManager` SHALL use DPAPI (`ProtectedData.Protect` with `DataProtectionScope.CurrentUser`) to encrypt the `ApiKey`.
3. WHERE the platform is macOS or Linux, THE `ConfigManager` SHALL use AES-256-GCM with a key derived from a machine-unique secret (e.g., machine GUID or hostname hash) to encrypt the `ApiKey`.
4. WHEN `ConfigManager` loads an `AppConfig`, THE `ConfigManager` SHALL detect whether the stored `ApiKey` is encrypted and decrypt it before returning the `AppConfig` to callers.
5. IF decryption of the stored `ApiKey` fails, THEN THE `ConfigManager` SHALL log a warning and return an `AppConfig` with an empty `ApiKey`, prompting the user to re-run the setup wizard.
6. WHEN an `ApiKey` is provided via the `BSE_API_KEY` environment variable, THE `ConfigManager` SHALL use the environment variable value directly without attempting decryption.
7. FOR ALL valid `ApiKey` strings, encrypting then decrypting SHALL produce the original `ApiKey` value (round-trip correctness).
8. THE `ConfigManager` SHALL store an encryption format version marker in `config.json` so that future format migrations can be detected and handled.

---

### Requirement 4 (T3): Semantic Search Tool

**Priority:** High — Phase 2 (Short-Term)  
**Affected files:** `src/Tools/` (new file), `src/Tools/ToolRegistry.cs` — `CreateDefault()`

**User Story:** As a developer using bse-code, I want to search the codebase by semantic meaning rather than exact text patterns, so that the AI can find relevant code even when the exact keywords are unknown.

**Context:** `ToolRegistry.CreateDefault()` registers only `GrepTool` for text search. No embedding or vector search capability exists, limiting the AI's ability to locate semantically related code.

#### Acceptance Criteria

1. THE `ToolRegistry` SHALL register a `SemanticSearchTool` that accepts a natural-language `query` and an optional `path` parameter.
2. WHEN `SemanticSearchTool` is invoked, THE `SemanticSearchTool` SHALL generate an embedding for the `query` using a configurable embedding model or provider.
3. WHEN `SemanticSearchTool` is invoked, THE `SemanticSearchTool` SHALL return the top-N most semantically similar code chunks from the workspace, where N is configurable and defaults to 10.
4. WHEN `SemanticSearchTool` is invoked with a `path` parameter, THE `SemanticSearchTool` SHALL restrict the search scope to files under the specified path.
5. IF the embedding provider is unavailable or returns an error, THEN THE `SemanticSearchTool` SHALL return an error string describing the failure without crashing the application.
6. THE `SemanticSearchTool` SHALL build and cache a vector index of the workspace on first invocation, and invalidate the cache when files are modified.
7. WHEN `SemanticSearchTool` returns results, THE `SemanticSearchTool` SHALL include the file path, line range, and a relevance score for each result.
8. THE `SemanticSearchTool` parameter schema SHALL declare `query` as a required string parameter and `path` and `top_n` as optional parameters.

---

### Requirement 5 (T5): Linter and Diagnostic Integration Tool

**Priority:** Medium — Phase 2 (Short-Term)  
**Affected files:** `src/Tools/` (new file), `src/Tools/ToolRegistry.cs` — `CreateDefault()`

**User Story:** As a developer using bse-code, I want the AI to run build and lint commands and receive structured diagnostic output, so that it can automatically detect and fix compilation errors and code quality issues.

**Context:** No tool in `src/Tools/` runs build or lint commands and feeds structured error output back to the LLM. The AI must rely on the user to manually report errors.

#### Acceptance Criteria

1. THE `ToolRegistry` SHALL register a `DiagnosticTool` that accepts a `command` parameter specifying the build or lint command to run.
2. WHEN `DiagnosticTool` is invoked, THE `DiagnosticTool` SHALL execute the specified `command` using the platform shell and capture both stdout and stderr.
3. WHEN `DiagnosticTool` completes execution, THE `DiagnosticTool` SHALL return a structured result containing: exit code, list of diagnostic messages each with file path, line number, column number, severity, and message text.
4. WHEN the `command` output contains MSBuild-format diagnostics (`file(line,col): severity code: message`), THE `DiagnosticTool` SHALL parse them into the structured diagnostic format.
5. WHEN the `command` output contains `dotnet format` or ESLint-format diagnostics, THE `DiagnosticTool` SHALL parse them into the structured diagnostic format.
6. IF the `command` exits with a non-zero code and produces no parseable diagnostics, THEN THE `DiagnosticTool` SHALL return the raw output as a single diagnostic message with severity "error".
7. THE `DiagnosticTool` SHALL apply the same configurable timeout mechanism as `BashTool`, defaulting to 60 seconds.
8. THE `DiagnosticTool` parameter schema SHALL declare `command` as a required string parameter and `timeout_seconds` as an optional integer parameter.

---

### Requirement 6 (T6): Rich Text and Markdown Rendering

**Priority:** Low — Phase 3 (Long-Term)  
**Affected file:** `src/UI.cs`, `src/ReplEngine.cs` — streaming output section

**User Story:** As a developer using bse-code, I want LLM responses rendered with markdown formatting (bold, code blocks, headers), so that code snippets and structured output are visually clear in the terminal.

**Context:** `UI.cs` uses only `Console.ForegroundColor` and `Console.WriteLine()`. LLM responses containing markdown are printed as raw text, making code blocks and headers hard to read.

#### Acceptance Criteria

1. THE `UI` SHALL render LLM response text containing markdown syntax using `Spectre_Console` markup when the terminal supports ANSI escape codes.
2. WHEN an LLM response contains a fenced code block (` ``` `), THE `UI` SHALL render it with syntax highlighting appropriate to the declared language.
3. WHEN an LLM response contains markdown headers (`#`, `##`, `###`), THE `UI` SHALL render them with visually distinct formatting (e.g., bold or colored text).
4. WHEN an LLM response contains bold (`**text**`) or italic (`*text*`) markdown, THE `UI` SHALL render the appropriate terminal formatting.
5. IF the terminal does not support ANSI escape codes (e.g., `NO_COLOR` environment variable is set, or output is redirected), THEN THE `UI` SHALL fall back to plain text rendering without ANSI sequences.
6. WHEN streaming LLM response tokens, THE `UI` SHALL buffer complete markdown constructs before rendering them, so that partial markdown syntax is not displayed as raw characters.
7. THE `UI` SHALL preserve the existing `ConsoleColor`-based theme system for non-response output (prompts, tool call indicators, status messages).

---

### Requirement 7 (P1): Parallel Tool Call Dispatch

**Priority:** Medium — Phase 3 (Long-Term)  
**Affected file:** `src/ReplEngine.cs` — `RunTurnAsync()` tool dispatch loop

**User Story:** As a developer using bse-code, I want independent tool calls within a single LLM response to execute in parallel, so that multi-tool turns complete faster.

**Context:** `RunTurnAsync()` processes tool calls in a sequential `foreach` loop with `await` inside. When the LLM requests multiple independent tools (e.g., reading several files), each call waits for the previous one to complete.

#### Acceptance Criteria

1. WHEN an LLM response contains multiple `Tool_Call` items, THE `ReplEngine` SHALL execute all `Tool_Call` items concurrently using `Task.WhenAll()`.
2. WHEN parallel `Tool_Call` execution completes, THE `ReplEngine` SHALL collect all results and add them to the message list in the same order as the original `Tool_Call` sequence.
3. WHILE parallel `Tool_Call` items are executing, THE `ReplEngine` SHALL display a progress indicator for each in-flight tool call.
4. IF any `Tool_Call` in a parallel batch throws an exception, THEN THE `ReplEngine` SHALL capture the exception as an error result for that specific call and continue executing the remaining calls.
5. WHEN all `Tool_Call` items in a batch complete (successfully or with errors), THE `ReplEngine` SHALL add all tool result messages to the conversation before sending the next LLM request.
6. THE `ReplEngine` SHALL preserve sequential execution order for `Tool_Call` items that target the same file path, to prevent read-write race conditions.

---

### Requirement 8 (T4): Non-Blocking BashTool with stdin Support

**Priority:** Medium  
**Affected file:** `src/Tools/BashTool.cs` — `RunShell()`

**User Story:** As a developer using bse-code, I want shell commands that require stdin input to either receive it or fail gracefully, so that interactive commands do not hang the application indefinitely.

**Context:** `BashTool.RunShell()` redirects stdin (`RedirectStandardInput = true`) but never writes to it. Commands that read from stdin (e.g., `git commit` without `-m`, `npm init`) will block waiting for input that never arrives, eventually timing out after 30 seconds.

#### Acceptance Criteria

1. WHEN `BashTool` is invoked with a `stdin` parameter, THE `BashTool` SHALL write the provided string to the process's standard input stream before reading output.
2. WHEN `BashTool` is invoked without a `stdin` parameter, THE `BashTool` SHALL close the process's standard input stream immediately after process start, so that commands reading from stdin receive EOF rather than blocking.
3. WHEN a command exits within the configured timeout after receiving EOF on stdin, THE `BashTool` SHALL return the command's output normally.
4. IF a command does not exit within the configured timeout, THEN THE `BashTool` SHALL kill the process tree and return an error string containing the command and elapsed time.
5. THE `BashTool` parameter schema SHALL declare `stdin` as an optional string parameter.
6. THE `BashTool` `DefaultTimeout` SHALL remain 30 seconds for backward compatibility.

---

### Requirement 9 (P2): Token-Aware Context Compaction

**Priority:** Medium  
**Affected file:** `src/SlashCommandHandler.cs` — `HandleCompactAsync()`

**User Story:** As a developer using bse-code, I want the `/compact` command to intelligently prune the conversation based on token counts and message importance, so that the context window is managed efficiently without losing critical information.

**Context:** `HandleCompactAsync()` sends a single "summarize everything" prompt and then removes all non-system messages. There is no token counting, selective pruning, or hierarchical memory — the entire history is discarded after one summary.

#### Acceptance Criteria

1. WHEN `/compact` is invoked, THE `SlashCommandHandler` SHALL estimate the current conversation token count using a character-based approximation (4 characters per token) before deciding whether compaction is needed.
2. WHEN the estimated token count exceeds the configured `Token_Budget`, THE `SlashCommandHandler` SHALL selectively remove the oldest non-system messages until the estimated count falls below the `Token_Budget`.
3. WHEN `/compact` is invoked, THE `SlashCommandHandler` SHALL preserve all `SystemChatMessage` entries and the most recent 4 `UserChatMessage`/`AssistantChatMessage` pairs regardless of token count.
4. WHEN `/compact` is invoked with a hint argument, THE `SlashCommandHandler` SHALL include the hint in the summarization prompt sent to the LLM.
5. WHEN compaction completes, THE `SlashCommandHandler` SHALL display the estimated token count before and after compaction.
6. THE `SlashCommandHandler` SHALL support a configurable `Token_Budget` with a default value of 80,000 tokens.
7. IF the estimated token count is below the `Token_Budget`, THEN THE `SlashCommandHandler` SHALL inform the user that compaction is not needed and display the current estimated count.

---

### Requirement 10 (S1): Shell Command Execution Safeguards

**Priority:** High  
**Affected file:** `src/Tools/BashTool.cs` — `ExecuteAsync()`, `RunShell()`

**User Story:** As a developer using bse-code, I want dangerous shell commands to require explicit confirmation before execution, so that the AI cannot silently execute destructive operations with full user privileges.

**Context:** `BashTool.RunShell()` explicitly documents no sandboxing. Any command the LLM requests is executed immediately with full user privileges. The existing security warning in the code acknowledges this risk but provides no mitigation.

#### Acceptance Criteria

1. THE `BashTool` SHALL maintain a configurable blocklist of command patterns that are always denied without user confirmation (e.g., `rm -rf /`, `format`, `mkfs`, `dd if=`).
2. WHEN `BashTool` receives a command matching a blocklist pattern, THE `BashTool` SHALL return an error string describing the blocked command without executing it.
3. THE `BashTool` SHALL maintain a configurable allowlist of command patterns that are always permitted without confirmation (e.g., `echo`, `cat`, `ls`, `git status`).
4. WHEN `BashTool` receives a command that is not on the allowlist and not on the blocklist, THE `BashTool` SHALL prompt the user for confirmation before executing the command.
5. WHEN the user denies confirmation for a command, THE `BashTool` SHALL return an error string indicating the command was denied by the user, without executing it.
6. WHEN the user approves confirmation for a command, THE `BashTool` SHALL execute the command and return its output.
7. WHERE the `BSE_BASH_CONFIRM` environment variable is set to `"off"`, THE `BashTool` SHALL skip the confirmation prompt and execute all non-blocklisted commands directly (for CI/automation use).
8. THE `BashTool` SHALL log all executed commands with their exit codes to a session audit log at `~/.bse-code/audit.log`.

---

### Requirement 11 (N1): Extensibility Documentation

**Priority:** Medium  
**Affected file:** `CONTRIBUTING.md`

**User Story:** As a contributor to bse-code, I want comprehensive guides for adding new tools, providers, and MCP servers, so that I can extend the application without reverse-engineering the codebase.

**Context:** `CONTRIBUTING.md` contains only 5 lines on adding tools ("Create `src/Tools/YourTool.cs` implementing `IToolHandler`, register it in `ToolRegistry.CreateDefault()`, add tests"). There is no guide for adding providers, MCP servers, or custom tools with examples.

#### Acceptance Criteria

1. THE `CONTRIBUTING.md` SHALL contain a "Adding a New Tool" section with a complete, annotated code example implementing `IToolHandler`, including the `Name`, `Description`, `ParameterSchema`, and `ExecuteAsync` members.
2. THE `CONTRIBUTING.md` SHALL contain an "Adding a New LLM Provider" section describing how to add a new entry to the `LlmProvider` enum, `ProviderDef` array, and `FallbackModels` dictionary in `ConfigManager.cs`.
3. THE `CONTRIBUTING.md` SHALL contain a "Configuring MCP Servers" section with a complete `mcp.json` example, explanation of all fields (`command`, `args`, `env`, `disabled`), and at least two real-world MCP server examples.
4. THE `CONTRIBUTING.md` SHALL contain a "Tool Naming Conventions" section documenting the `mcp__serverName__toolName` naming scheme and the `IToolHandler.Name` constraints.
5. THE `CONTRIBUTING.md` SHALL contain a "Testing New Tools" section with an example test class for a new tool, covering at least: successful execution, missing required parameter, and timeout behavior.
6. WHEN a new tool is added following the documented steps, THE `ToolRegistry` SHALL register it without requiring changes to any file other than the new tool file and `ToolRegistry.CreateDefault()`.

---

### Requirement 12 (N2): MCP Server Lifecycle Integration Tests

**Priority:** Low  
**Affected files:** `tests/McpManagerTests.cs`, `tests/Tools/BashToolTests.cs`

**User Story:** As a maintainer of bse-code, I want integration tests that exercise real MCP server process lifecycle and interactive BashTool behavior, so that regressions in process management are caught before release.

**Context:** `tests/McpManagerTests.cs` only tests config deserialization and error paths with non-existent executables. `tests/Tools/BashToolTests.cs` only tests non-interactive commands. No test exercises a real MCP server initialize/tools/list/tools/call lifecycle, and no test exercises `BashTool` with stdin input.

#### Acceptance Criteria

1. THE test suite SHALL contain an integration test that starts a real MCP server process (using a minimal in-process echo server or a well-known test MCP server), calls `tools/list`, and verifies the returned tool names match the expected schema.
2. THE test suite SHALL contain an integration test that calls `McpManager.CallToolAsync()` against a real MCP server process and verifies the response content is non-empty and well-formed JSON or plain text.
3. THE test suite SHALL contain an integration test that verifies `McpManager` detects an `MCP_Session` process exit and attempts a restart (per Requirement 1, criterion 3).
4. THE test suite SHALL contain a unit test that verifies `BashTool` closes stdin immediately when no `stdin` parameter is provided (per Requirement 8, criterion 2).
5. THE test suite SHALL contain a unit test that verifies `BashTool` writes the provided `stdin` string to the process and the command receives it (per Requirement 8, criterion 1).
6. WHEN the integration tests require an external MCP server binary, THE test suite SHALL skip those tests gracefully when the binary is not present, using `xunit` skip conditions.
