# Implementation Plan: Codebase Quality Improvements

## Overview

Implement targeted quality improvements in dependency order: project structure first (everything else depends on it), then runtime reliability fixes with tests, then new test classes, then security documentation, then developer-experience polish.

## Tasks

- [x] 1. Create `src/BSE_Code.Core.csproj` library project
  - Create `src/BSE_Code.Core.csproj` as `<OutputType>Library</OutputType>` targeting `net10.0`
  - Set `<AssemblyName>BSE_Code.Core</AssemblyName>` and `<RootNamespace>BSE_Code</RootNamespace>`
  - Add `<ImplicitUsings>enable</ImplicitUsings>` and `<Nullable>enable</Nullable>`
  - Add `[InternalsVisibleTo("BSE_Code.Tests")]` via `<AssemblyAttribute>` item
  - Add `<PackageReference Include="OpenAI" Version="2.10.0" />`
  - The project compiles all `src/**/*.cs` files via the default SDK glob (no explicit `<Compile>` items needed)
  - _Requirements: 1.1, 1.6_

- [x] 2. Update `tests/BSE_Code.Tests.csproj` to reference Core
  - Add `<ProjectReference Include="../src/BSE_Code.Core.csproj" />`
  - Remove all `<Compile Include="../src/...">` items (the 17 entries currently in the file)
  - Remove the duplicate `<PackageReference Include="OpenAI" ...>` (now provided transitively by Core)
  - _Requirements: 1.3_

- [x] 3. Update `BSE_Code.csproj` to reference Core
  - Add `<ProjectReference Include="src/BSE_Code.Core.csproj" />`
  - Remove the `<AssemblyAttribute>` `InternalsVisibleTo` block (it moves to Core)
  - Remove the `<Compile Remove="tests/**" />` guard (no longer needed)
  - Keep `<PackageReference Include="OpenAI" ...>` only if the Exe project still needs it directly; otherwise remove it (Core provides it transitively)
  - _Requirements: 1.2_

- [x] 4. Add `BSE_Code.Core` to `BSE_Code.sln`
  - Add a new `Project(...)` entry for `src/BSE_Code.Core.csproj` with a new GUID
  - Add all six `Debug|Any CPU` / `Release|Any CPU` / etc. configuration mappings in `GlobalSection(ProjectConfigurationPlatforms)`
  - Verify `dotnet build BSE_Code.sln` exits 0 with no duplicate-symbol errors
  - _Requirements: 1.5_

- [x] 5. Create `src/ReplEngine.cs` with logic extracted from `Program.cs`
  - Create `src/ReplEngine.cs` as a `public sealed class ReplEngine` in the Core library
  - Constructor accepts `AppConfig`, `ToolRegistry`, `Func<ChatClient>`, `Func<string>` (system prompt), `Func<ChatCompletionOptions>`
  - Implement `public Task RunAsync()` (interactive REPL loop)
  - Implement `public Task RunOneShotAsync(string prompt, string outputFormat)`
  - Implement `internal Task RunTurnAsync(ChatClient, ChatCompletionOptions, List<ChatMessage>, string, StringBuilder?)` — extracted from the current inline turn logic in `Program.cs`
  - Implement `internal static string? InjectAtPath(string atPath, string rest)` — wraps file content in fenced code block; caps directory injection at 20 files; returns `null` for missing paths
  - Implement `internal static async Task<string> HandleMcpToolAsync(string fullName, string argsJson)`
  - Implement `internal static void ValidateUnknownFlags(string[] args, string? inlinePrompt, string? modelOverride)` — throws `ArgumentException` instead of calling `Environment.Exit`
  - Move all UI-helper static methods (`PrintBanner`, `PrintStats`, `PrintToolCall`, `PrintToolResult`, `GetGitBranch`, `Truncate`) into `ReplEngine` as `internal static`
  - _Requirements: 2.1, 2.3, 2.4, 2.5, 2.6_

- [x] 6. Reduce `Program.cs` to ≤50 lines entry point
  - Rewrite `Program.cs` as top-level statements that: handle `--version`/`-v`, `--help`/`-h`, `--config` flags; parse `--model`, `--theme`, `--output-format`, `-p` flags; call `ReplEngine.ValidateUnknownFlags` in a try/catch that calls `Environment.Exit(1)` on `ArgumentException`; construct a `ReplEngine` and call `RunOneShotAsync` or `RunAsync`
  - Verify the file is ≤50 lines
  - _Requirements: 2.2_

- [x] 7. Fix `SessionManager.Save` to strip tool-call assistant messages
  - In `src/SessionManager.cs`, change the `.Where` predicate in `Save()` from `m is UserChatMessage or AssistantChatMessage` to a switch expression that keeps `UserChatMessage` and `AssistantChatMessage` only when `ToolCalls.Count == 0`
  - New predicate: `m switch { UserChatMessage => true, AssistantChatMessage a when a.ToolCalls.Count == 0 => true, _ => false }`
  - _Requirements: 5.1, 5.4_

- [x] 8. Add `SessionManagerTests`
  - Create `tests/SessionManagerTests.cs`
  - `[Fact] Save_TextOnlyMessages_RoundTripsUnchanged()` — save a list of user+assistant text messages, resume, assert content identical
  - `[Fact] Save_ToolCallAssistantMessage_IsStripped()` — save a list containing an `AssistantChatMessage` with a tool call, resume, assert no assistant message with tool calls in result
  - `[Fact] Resume_OrphanedToolCallInFile_IsDropped()` — write a session JSON file manually with an assistant message that has a non-empty `tool_calls` field, call `Resume`, assert result contains no orphaned references
  - `[Fact] Save_EmptyContentMessages_AreExcluded()` — assert messages with whitespace-only content are not persisted

  - [x] 8.1 Write property test for session round-trip (Property 7)
    - **Property 7: Session save+resume round-trip produces no orphaned tool-call references**
    - Use FsCheck to generate arbitrary `List<ChatMessage>` including messages with tool calls
    - Assert: after `Save` + `Resume`, no `AssistantChatMessage` in result references a tool call ID lacking a corresponding `ToolChatMessage`
    - Tag: `// Feature: codebase-quality-improvements, Property 7: Session save+resume round-trip produces no orphaned tool-call references`
    - **Validates: Requirements 5.1, 5.2**

  - _Requirements: 5.2, 5.3_

- [x] 9. Fix `McpManager.CallToolAsync` to call `UI.Warn` on errors
  - In `src/McpManager.cs`, in the `catch (Exception ex)` block of `CallToolAsync`, add `UI.Warn($"🔌 MCP '{serverName}/{toolName}' failed: {ex.Message}")` before the `return`
  - Add a null-response guard: `if (result is null) { UI.Warn($"🔌 MCP '{serverName}/{toolName}': no response (timeout or empty)."); return "ERROR: No response from MCP server."; }`
  - _Requirements: 7.1, 7.3_

- [x] 10. Add `McpManagerTests`
  - Create `tests/McpManagerTests.cs`
  - `[Fact] CallToolAsync_UnknownServer_ReturnsErrorString()` — call with a server name not in `_activeServers`, assert return starts with `"❌ ERROR:"`
  - `[Fact] CallToolAsync_ExceptionDuringCall_ReturnsErrorAndWarns()` — inject a server config that causes an exception, assert return starts with `"ERROR: "` and `UI.Warn` was called
  - `[Fact] CallToolAsync_NullResponse_ReturnsErrorAndWarns()` — simulate a null response from `SendMcpRequestAsync`, assert return starts with `"ERROR: No response"` and `UI.Warn` was called

  - [x] 10.1 Write property test for McpManager error surfacing (Property 8)
    - **Property 8: McpManager always surfaces errors visibly**
    - Use FsCheck to generate arbitrary exception messages
    - Assert: for any exception thrown during `CallToolAsync`, the returned string starts with `"ERROR: "` and `UI.Warn` was called
    - Tag: `// Feature: codebase-quality-improvements, Property 8: McpManager always surfaces errors visibly`
    - **Validates: Requirements 7.1**

  - _Requirements: 7.1, 7.3, 7.4_

- [x] 11. Add `ConfigManager.ValidateBaseUrl` and call it in `LoadOrSetupAsync`
  - In `src/ConfigManager.cs`, add `private static void ValidateBaseUrl(AppConfig config)` that: returns immediately if `config.BaseUrl` is null/whitespace; calls `Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out _)`; if false, writes a human-readable error to `Console.Error` and calls `Environment.Exit(1)`
  - Call `ValidateBaseUrl(saved)` in `LoadOrSetupAsync` after all env var overrides are applied, before returning
  - Call `ValidateBaseUrl(config)` at the end of `RunSetupWizardAsync` before calling `Save(config)`
  - _Requirements: 8.1, 8.2, 8.3, 8.5_

- [x] 12. Add `ConfigManagerTests`
  - Create `tests/ConfigManagerTests.cs`
  - `[Fact] ValidateBaseUrl_ValidAbsoluteUri_DoesNotExit()` — call with `"https://api.openai.com/v1"`, assert no exception and no exit
  - `[Fact] ValidateBaseUrl_InvalidUri_ExitsWithNonZero()` — call with `"not-a-url"`, assert `Environment.Exit` is triggered (use a testable wrapper or catch the exit)
  - `[Fact] ValidateBaseUrl_EmptyBaseUrl_DoesNotExit()` — call with empty string, assert passes (wizard will prompt)

  - [x] 12.1 Write property test for BaseUrl validation (Property 9)
    - **Property 9: BaseUrl validation accepts valid URIs and rejects invalid ones**
    - Use FsCheck to generate random strings; separately generate valid absolute URIs
    - Assert: valid absolute URIs pass without error; strings that fail `Uri.TryCreate(..., UriKind.Absolute, ...)` trigger the exit path
    - Tag: `// Feature: codebase-quality-improvements, Property 9: BaseUrl validation accepts valid URIs and rejects invalid ones`
    - **Validates: Requirements 8.1, 8.3, 8.5**

  - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5_

- [x] 13. Checkpoint — Ensure all tests pass
  - Run `dotnet build BSE_Code.sln` and confirm zero errors
  - Run `dotnet test tests/BSE_Code.Tests.csproj -c Release` and confirm all tests pass
  - Confirm `coverage.cobertura.xml` is produced and references `BSE_Code.Core`
  - Ask the user if any questions arise before continuing.

- [x] 14. Add `SlashCommandHandlerTests`
  - Create `tests/SlashCommandHandlerTests.cs`
  - Add a `MakeHandler(...)` factory that wires stub `Func<>` delegates and an in-memory `List<ChatMessage>`
  - `[Fact] Exit_ReturnsOne()`
  - `[Fact] Quit_ReturnsOne()`
  - `[Fact] Clear_RemovesNonSystemMessages()`
  - `[Fact] Clear_PreservesSystemMessage()`
  - `[Fact] Model_WithArg_UpdatesConfigAndRebuildsClient()`
  - `[Fact] Model_NoArg_PrintsCurrentModel()`
  - `[Fact] Save_WithTag_CallsSessionManagerSave()`
  - `[Fact] Resume_WithTag_LoadsMessages()`
  - `[Fact] Compact_FewerThanThreeUserMessages_ReturnsZeroWithoutCallingRunTurn()`
  - `[Fact] UnknownCommand_NoSkill_ReturnsZero()`

  - [x] 14.1 Write property test for /clear (Property 4)
    - **Property 4: /clear removes all non-system messages from any message list**
    - Use FsCheck to generate arbitrary mixes of `SystemChatMessage`, `UserChatMessage`, `AssistantChatMessage`
    - Assert: after `/clear` is handled, the list contains only `SystemChatMessage` entries
    - Tag: `// Feature: codebase-quality-improvements, Property 4: /clear removes all non-system messages from any message list`
    - **Validates: Requirements 3.2**

  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

- [x] 15. Add `InteractiveInputTests` (make `GetSlashItems` internal first)
  - In `src/InteractiveInput.cs`, change `private static List<PickerItem> GetSlashItems(string filter)` to `internal static`
  - Create `tests/InteractiveInputTests.cs`
  - `[Fact] History_NewLine_AddedAtEnd()`
  - `[Fact] History_ConsecutiveDuplicate_StoredOnce()`
  - `[Fact] History_NonConsecutiveDuplicate_BothStored()`
  - `[Fact] GetSlashItems_EmptyFilter_ReturnsAllBuiltins()`
  - `[Fact] GetSlashItems_MatchingFilter_ReturnsOnlyMatches()`
  - `[Fact] GetSlashItems_NonMatchingFilter_ReturnsEmpty()`
  - `[Fact] GetSlashItems_FilterIsCaseInsensitive()`

  - [x] 15.1 Write property test for history deduplication (Property 5)
    - **Property 5: History deduplication invariant**
    - Use FsCheck to generate arbitrary sequences of strings including consecutive duplicates
    - Assert: the history list never contains the same string as two consecutive entries
    - Tag: `// Feature: codebase-quality-improvements, Property 5: History deduplication invariant`
    - **Validates: Requirements 4.2, 4.3**

  - [x] 15.2 Write property test for GetSlashItems filter (Property 6)
    - **Property 6: GetSlashItems filter returns only matching items**
    - Use FsCheck to generate arbitrary non-empty filter strings
    - Assert: every item returned by `GetSlashItems(filter)` has a label or value containing the filter (case-insensitive); no non-matching item appears
    - Tag: `// Feature: codebase-quality-improvements, Property 6: GetSlashItems filter returns only matching items`
    - **Validates: Requirements 4.5**

  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7_

- [x] 16. Add `ReplEngineTests`
  - Create `tests/ReplEngineTests.cs`
  - `[Fact] ValidateUnknownFlags_KnownFlags_DoesNotThrow()` — pass `["-p", "hello", "--model", "gpt-4o"]`, assert no exception
  - `[Fact] ValidateUnknownFlags_UnknownFlag_ThrowsArgumentException()` — pass `["--foo"]`, assert `ArgumentException`
  - `[Fact] InjectAtPath_ExistingFile_ReturnsFencedCodeBlock()` — write a temp file, call `InjectAtPath`, assert result contains content and `` ``` `` markers
  - `[Fact] InjectAtPath_MissingPath_ReturnsNull()` — call with a non-existent path, assert `null`
  - `[Fact] InjectAtPath_Directory_ListsUpToTwentyFiles()` — create a temp dir with 25 files, assert result references ≤20

  - [x] 16.1 Write property test for ValidateUnknownFlags (Property 1)
    - **Property 1: ValidateUnknownFlags rejects any unrecognised flag**
    - Use FsCheck to generate random strings starting with `--` or `-` that are not in the known flags set
    - Assert: `ValidateUnknownFlags` throws `ArgumentException` for every such string
    - Tag: `// Feature: codebase-quality-improvements, Property 1: ValidateUnknownFlags rejects any unrecognised flag`
    - **Validates: Requirements 2.3**

  - [x] 16.2 Write property test for InjectAtPath file wrapping (Property 2)
    - **Property 2: InjectAtPath wraps any file content in a fenced code block**
    - Use FsCheck to generate arbitrary file content strings; write each to a temp file
    - Assert: `InjectAtPath` returns a non-null string containing the content and `` ``` `` markers
    - Tag: `// Feature: codebase-quality-improvements, Property 2: InjectAtPath wraps any file content in a fenced code block`
    - **Validates: Requirements 2.4**

  - [x] 16.3 Write property test for InjectAtPath directory cap (Property 3)
    - **Property 3: InjectAtPath caps directory injection at 20 files**
    - Use FsCheck to generate N in range 0–50; create a temp directory with N files
    - Assert: `InjectAtPath` returns a string referencing at most 20 files
    - Tag: `// Feature: codebase-quality-improvements, Property 3: InjectAtPath caps directory injection at 20 files`
    - **Validates: Requirements 2.5**

  - _Requirements: 2.3, 2.4, 2.5, 2.6, 2.7_

- [x] 17. Checkpoint — Ensure all tests pass
  - Run `dotnet test tests/BSE_Code.Tests.csproj -c Release` and confirm all tests pass
  - Ask the user if any questions arise before continuing.

- [x] 18. Add security comments to `BashTool`
  - In `src/Tools/BashTool.cs`, replace the existing `<summary>` on `RunShell` with an expanded XML doc comment that includes a `<remarks>` block stating: the method executes arbitrary shell commands supplied by the LLM; no input sanitization is performed; callers are responsible for ensuring the command originates from a trusted source
  - Add an inline comment on the `command` extraction line in `ExecuteAsync`: `// WARNING: 'command' is passed directly to the platform shell without sanitization.`
  - _Requirements: 6.1, 6.2_

- [x] 19. Create `SECURITY.md`
  - Create `SECURITY.md` at the repository root with sections:
    1. **Security-Sensitive Capabilities** — BashTool arbitrary shell execution, ReadFileTool/WriteFileTool arbitrary file access, MCP server subprocess spawning
    2. **Supported Versions** — latest published NuGet/npm release receives security fixes
    3. **Reporting a Vulnerability** — GitHub private security advisory link, acknowledgement within 7 days, fix within 90 days for critical issues
    4. **Scope of Security Guarantees** — BSE-Code is a local developer tool; users are responsible for reviewing tool calls before approving them
  - _Requirements: 6.3, 11.1, 11.2, 11.3, 11.4, 11.5_

- [x] 20. Fix `cliff.toml` to use template variables
  - In `cliff.toml`, replace the hardcoded `https://github.com/BibleSocietyEg/bse-code/commit/{{ commit.id }}` URL with `https://github.com/{{ remote.github.owner }}/{{ remote.github.repo }}/commit/{{ commit.id }}`
  - _Requirements: 9.1, 9.2_

- [x] 21. Create `.githooks/pre-commit.ps1`
  - Create `.githooks/pre-commit.ps1` as a PowerShell script with `$ErrorActionPreference = 'Stop'`
  - Run `dotnet format --verify-no-changes --no-restore`; on non-zero exit, print remediation message and `exit 1`
  - Run `dotnet test tests/BSE_Code.Tests.csproj --no-restore -c Release --verbosity quiet`; on non-zero exit, print remediation message and `exit 1`
  - Print `✅ All checks passed` on success
  - _Requirements: 10.1, 10.4, 10.5_

- [x] 22. Update `.githooks/pre-commit` with Windows detection note
  - Add a Windows detection block near the top of `.githooks/pre-commit` (after `set -e`) that checks `uname -s` or `$OS` and, if Windows is detected without a POSIX shell, prints a message directing the user to run `.githooks/pre-commit.ps1` manually
  - _Requirements: 10.2_

- [x] 23. Update `CONTRIBUTING.md` with Windows hook instructions
  - Add a "Windows Contributors" section to `CONTRIBUTING.md` documenting: how to install the PowerShell hook (`git config core.hooksPath .githooks`), how to run it manually (`pwsh .githooks/pre-commit.ps1`), and what it checks
  - _Requirements: 10.3_

- [x] 24. Run `dotnet format` to fix existing formatting violations
  - Run `dotnet format` (without `--verify-no-changes`) to apply all formatting fixes to `src/` and `tests/`
  - Verify `dotnet format --verify-no-changes` exits 0 afterwards
  - _Requirements: 12.1, 12.2_

- [x] 25. Add CI badge to `README.md`
  - In `README.md`, add the following badge after the existing npm badge in the badges section:
    `[![CI](https://github.com/BibleSocietyEg/bse-code/actions/workflows/ci.yml/badge.svg)](https://github.com/BibleSocietyEg/bse-code/actions/workflows/ci.yml)`
  - _Requirements: 13.1, 13.2, 13.3_

- [x] 26. Final checkpoint — Ensure all tests pass
  - Run `dotnet build BSE_Code.sln` — confirm zero errors
  - Run `dotnet test tests/BSE_Code.Tests.csproj --collect:"XPlat Code Coverage" -c Release` — confirm all tests pass and `coverage.cobertura.xml` shows >0% line coverage for `BSE_Code.Core`
  - Run `dotnet format --verify-no-changes` — confirm exit 0
  - Ask the user if any questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for a faster MVP
- Property tests use [FsCheck](https://fscheck.github.io/FsCheck/) with a minimum of 100 iterations per property; add `<PackageReference Include="FsCheck.Xunit" Version="3.*" />` to `tests/BSE_Code.Tests.csproj` before implementing them
- Each property test must be tagged with `// Feature: codebase-quality-improvements, Property N: ...`
- Tasks 1–4 are strict prerequisites for everything else — do not reorder them
- Tasks 5–6 (ReplEngine extraction) can be done in parallel with tasks 7–12 once the Core project exists
